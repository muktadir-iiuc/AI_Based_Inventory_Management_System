using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services.Saas.Payments;

public record RefundOutcome(bool Succeeded, SaasRefund? Refund, string? Error);

public interface IRefundService
{
    /// <summary>
    /// Refunds all or part of a payment. Gateway payments are refunded through the provider (asynchronous on its side);
    /// manual payments are recorded as returned outside the system. Platform administrators only - the caller authorizes.
    /// Refunding does not itself suspend the tenant; do that explicitly if the service should stop.
    /// </summary>
    Task<RefundOutcome> RefundAsync(int paymentId, decimal amount, string reason, string? actor = null, CancellationToken ct = default);

    /// <summary>Polls the provider for refunds still in progress.</summary>
    Task<int> RefreshPendingAsync(CancellationToken ct = default);
}

public class RefundService(
    ApplicationDbContext db,
    ITenantContext tenantContext,
    IPaymentProviderRegistry providers,
    ISaasNotifier notifier,
    ISaasAudit audit,
    TimeProvider time) : IRefundService
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<RefundOutcome> RefundAsync(int paymentId, decimal amount, string reason, string? actor = null, CancellationToken ct = default)
    {
        using var _ = tenantContext.BypassFilters();
        amount = BillingMath.Round(amount);
        if (amount <= 0) return new(false, null, "Refund amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(reason)) return new(false, null, "A reason is required.");

        var payment = await db.SaasPayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return new(false, null, "Payment not found.");
        if (payment.Status is not (SaasPaymentStatus.Succeeded or SaasPaymentStatus.Refunded)) return new(false, null, "Only a successful payment can be refunded.");
        if (amount > payment.Amount - payment.RefundedAmount) return new(false, null, "Refund exceeds the amount still refundable on this payment.");

        var refund = new SaasRefund
        {
            PaymentId = payment.Id, InvoiceId = payment.InvoiceId, TenantId = payment.TenantId, Amount = amount, Reason = reason.Trim(),
            CreatedAt = Now, CreatedBy = actor ?? audit.Actor,
            RefundTransId = "RF" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10))
        };

        if (payment.Provider == "manual")
        {
            // Cash/bank money handed back outside the system; the authorized admin records that it happened.
            refund.Status = RefundStatus.Succeeded;
            refund.CompletedAt = Now;
        }
        else
        {
            var provider = providers.Find(payment.Provider);
            if (provider is null || string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
            {
                return new(false, null, "This payment cannot be refunded through its provider.");
            }

            var result = await provider.RefundAsync(new ProviderRefundRequest(payment.ProviderTransactionId, refund.RefundTransId!, amount, refund.Reason, payment.ReceiptNumber), ct);
            if (!result.Accepted)
            {
                audit.Add("refund.failed", nameof(SaasPayment), payment.Id.ToString(), payment.TenantId, "Failed", result.Error);
                await db.SaveChangesAsync(ct);
                return new(false, null, result.Error ?? "The provider did not accept the refund.");
            }
            refund.ProviderRefundRef = result.ProviderRefundRef;
            refund.Status = RefundStatus.Pending; // the provider completes it later; RefreshPendingAsync follows up
        }

        db.SaasRefunds.Add(refund);
        payment.RefundedAmount += amount;
        await UpdateInvoiceAndPaymentStatusAsync(payment, ct);

        audit.Add("refund.created", nameof(SaasRefund), null, payment.TenantId, details: $"payment {payment.Id}, {amount:0.00} {payment.Currency}, {refund.Status}: {refund.Reason}");
        await notifier.QueueAsync(payment.TenantId, "refund", "Refund issued",
            $"A refund of {amount:0.00} {payment.Currency} was issued for your payment. Reason: {refund.Reason}", $"refund:{refund.RefundTransId}");
        await db.SaveChangesAsync(ct);
        return new(true, refund, null);
    }

    public async Task<int> RefreshPendingAsync(CancellationToken ct = default)
    {
        using var _ = tenantContext.BypassFilters();
        var pending = await db.SaasRefunds.Where(r => r.Status == RefundStatus.Pending && r.ProviderRefundRef != null).Take(50).ToListAsync(ct);
        var changed = 0;
        foreach (var refund in pending)
        {
            var payment = await db.SaasPayments.FirstAsync(p => p.Id == refund.PaymentId, ct);
            var provider = providers.Find(payment.Provider);
            if (provider is null) continue;

            switch (await provider.GetRefundStateAsync(refund.ProviderRefundRef!, ct))
            {
                case ProviderRefundState.Refunded:
                    refund.Status = RefundStatus.Succeeded;
                    refund.CompletedAt = Now;
                    audit.Add("refund.completed", nameof(SaasRefund), refund.Id.ToString(), refund.TenantId);
                    changed++;
                    break;
                case ProviderRefundState.Cancelled:
                    refund.Status = RefundStatus.Failed;
                    refund.FailureReason = "Cancelled by the provider.";
                    refund.CompletedAt = Now;
                    payment.RefundedAmount -= refund.Amount;       // the money was not returned
                    await UpdateInvoiceAndPaymentStatusAsync(payment, ct);
                    audit.Add("refund.cancelled", nameof(SaasRefund), refund.Id.ToString(), refund.TenantId, "Failed");
                    changed++;
                    break;
            }
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    private async Task UpdateInvoiceAndPaymentStatusAsync(SaasPayment payment, CancellationToken ct)
    {
        payment.Status = payment.RefundedAmount >= payment.Amount ? SaasPaymentStatus.Refunded : SaasPaymentStatus.Succeeded;

        // The invoice shows Refunded only when every payment on it has been fully refunded.
        var invoice = await db.SaasInvoices.Include(i => i.Payments).FirstAsync(i => i.Id == payment.InvoiceId, ct);
        var live = invoice.Payments.Where(p => p.Status is SaasPaymentStatus.Succeeded or SaasPaymentStatus.Refunded).ToList();
        if (live.Count > 0 && live.All(p => p.RefundedAmount >= p.Amount))
        {
            invoice.Status = InvoiceStatus.Refunded;
        }
        else if (invoice.Status == InvoiceStatus.Refunded)
        {
            invoice.Status = invoice.AmountPaid >= invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        }
    }
}
