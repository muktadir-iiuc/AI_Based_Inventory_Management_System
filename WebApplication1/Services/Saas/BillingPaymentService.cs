using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;

namespace WebApplication1.Services.Saas;

public enum PaymentOutcome
{
    /// <summary>New payment recorded and applied to the invoice.</summary>
    Applied,
    /// <summary>The same provider transaction / idempotency key was already recorded; nothing changed.</summary>
    Duplicate,
    /// <summary>Validation failed (wrong tenant, amount, currency, invoice state...). Nothing changed.</summary>
    Rejected,
    /// <summary>Manual payment stored, waiting for an authorised person to confirm it.</summary>
    AwaitingConfirmation
}

public record PaymentRequest(
    int InvoiceId,
    decimal Amount,
    string Currency,
    string Method,
    string Provider,
    string? ProviderTransactionId = null,
    string? IdempotencyKey = null,
    string? Reference = null,
    string? Note = null,
    /// <summary>When set, the invoice must belong to this tenant (gateway callbacks and the tenant portal pass it).</summary>
    int? ExpectedTenantId = null,
    /// <summary>True for tenant-submitted manual payments that someone must verify before they count.</summary>
    bool RequiresConfirmation = false,
    DateTime? ReceivedAt = null);

public record PaymentResult(PaymentOutcome Outcome, SaasPayment? Payment, string? Message)
{
    public bool Succeeded => Outcome is PaymentOutcome.Applied or PaymentOutcome.Duplicate or PaymentOutcome.AwaitingConfirmation;
}

public interface IBillingPaymentService
{
    /// <summary>
    /// The single place a payment reaches an invoice. Idempotent (same provider transaction or idempotency key
    /// never records twice), transactional, and the only code that extends or reactivates a subscription.
    /// Callers must have already VERIFIED the payment with the provider - never pass a browser-reported status.
    /// </summary>
    Task<PaymentResult> ApplyPaymentAsync(PaymentRequest request);

    Task<PaymentResult> ConfirmManualPaymentAsync(int paymentId, string? actor = null);
    Task RejectManualPaymentAsync(int paymentId, string reason, string? actor = null);
}

public class BillingPaymentService(
    ApplicationDbContext db,
    ISubscriptionService subscriptions,
    ISaasNotifier notifier,
    ISaasAudit audit,
    TimeProvider time) : IBillingPaymentService
{
    private const int MaxAttempts = 3;

    public async Task<PaymentResult> ApplyPaymentAsync(PaymentRequest request)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(request);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Another callback touched the same invoice/subscription first: reload and re-evaluate.
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Most likely the unique index on provider transaction / idempotency key: a concurrent duplicate.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<PaymentResult> ApplyOnceAsync(PaymentRequest r)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var amount = BillingMath.Round(r.Amount);
        var currency = (r.Currency ?? string.Empty).Trim().ToUpperInvariant();

        if (amount <= 0)
        {
            return await RejectAsync(r, null, "Amount must be greater than zero.");
        }

        // Idempotency first: a replayed notification returns the original result without touching anything.
        if (!string.IsNullOrWhiteSpace(r.ProviderTransactionId))
        {
            var dup = await db.SaasPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Provider == r.Provider && p.ProviderTransactionId == r.ProviderTransactionId);
            if (dup is not null) return new(PaymentOutcome.Duplicate, dup, "Payment already recorded.");
        }
        if (!string.IsNullOrWhiteSpace(r.IdempotencyKey))
        {
            var dup = await db.SaasPayments.AsNoTracking().FirstOrDefaultAsync(p => p.IdempotencyKey == r.IdempotencyKey);
            if (dup is not null) return new(PaymentOutcome.Duplicate, dup, "Payment already recorded.");
        }

        var invoice = await db.SaasInvoices.FirstOrDefaultAsync(i => i.Id == r.InvoiceId);
        if (invoice is null) return await RejectAsync(r, null, "Invoice not found.");
        if (r.ExpectedTenantId is int expected && invoice.TenantId != expected) return await RejectAsync(r, invoice, "Invoice does not belong to this tenant.");
        if (invoice.Status is InvoiceStatus.Cancelled or InvoiceStatus.Refunded or InvoiceStatus.Draft) return await RejectAsync(r, invoice, $"Invoice is {invoice.Status}.");
        if (!string.Equals(invoice.Currency, currency, StringComparison.OrdinalIgnoreCase)) return await RejectAsync(r, invoice, $"Currency {currency} does not match invoice currency {invoice.Currency}.");
        if (invoice.Status == InvoiceStatus.Paid) return await RejectAsync(r, invoice, "Invoice is already paid.");
        if (amount > invoice.Balance) return await RejectAsync(r, invoice, "Amount exceeds the outstanding balance.");

        var payment = new SaasPayment
        {
            InvoiceId = invoice.Id, TenantId = invoice.TenantId, Amount = amount, Currency = invoice.Currency,
            Method = r.Method, Provider = r.Provider, ProviderTransactionId = r.ProviderTransactionId, IdempotencyKey = r.IdempotencyKey,
            Reference = r.Reference, Note = r.Note, ReceivedAt = r.ReceivedAt ?? now, CreatedAt = now, RecordedBy = audit.Actor
        };

        if (r.RequiresConfirmation)
        {
            payment.Status = SaasPaymentStatus.AwaitingConfirmation;
            db.SaasPayments.Add(payment);
            audit.Add("payment.submitted", nameof(SaasPayment), null, invoice.TenantId, details: $"invoice {invoice.InvoiceNumber}, {amount:0.00} {invoice.Currency}, {r.Method}, ref {r.Reference}");
            await db.SaveChangesAsync();
            return new(PaymentOutcome.AwaitingConfirmation, payment, "Payment submitted for confirmation.");
        }

        payment.Status = SaasPaymentStatus.Succeeded;
        payment.ConfirmedAt = now;
        payment.ConfirmedBy = audit.Actor;
        payment.IsReconciled = r.Provider != "manual";
        db.SaasPayments.Add(payment);
        await SettleAsync(invoice, payment, now);
        return new(PaymentOutcome.Applied, payment, null);
    }

    public async Task<PaymentResult> ConfirmManualPaymentAsync(int paymentId, string? actor = null)
    {
        var payment = await db.SaasPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment is null) return new(PaymentOutcome.Rejected, null, "Payment not found.");
        if (payment.Status != SaasPaymentStatus.AwaitingConfirmation) return new(PaymentOutcome.Duplicate, payment, "Payment is not awaiting confirmation.");

        var invoice = await db.SaasInvoices.FirstAsync(i => i.Id == payment.InvoiceId);
        if (invoice.Status is InvoiceStatus.Cancelled or InvoiceStatus.Refunded || payment.Amount > invoice.Balance)
        {
            return new(PaymentOutcome.Rejected, payment, "Invoice can no longer accept this payment.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        payment.Status = SaasPaymentStatus.Succeeded;
        payment.ConfirmedAt = now;
        payment.ConfirmedBy = actor ?? audit.Actor;
        payment.IsReconciled = true;
        audit.Add("payment.manual-confirmed", nameof(SaasPayment), payment.Id.ToString(), payment.TenantId, details: $"{payment.Amount:0.00} {payment.Currency} ref {payment.Reference}");
        try
        {
            await SettleAsync(invoice, payment, now);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.SaasPayments.AsNoTracking().FirstAsync(p => p.Id == paymentId);
            return new(PaymentOutcome.Duplicate, current, "Payment was already processed.");
        }
        return new(PaymentOutcome.Applied, payment, null);
    }

    public async Task RejectManualPaymentAsync(int paymentId, string reason, string? actor = null)
    {
        var payment = await db.SaasPayments.FirstOrDefaultAsync(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Payment not found.");
        if (payment.Status != SaasPaymentStatus.AwaitingConfirmation)
        {
            throw new InvalidOperationException("Payment is not awaiting confirmation.");
        }
        payment.Status = SaasPaymentStatus.Failed;
        payment.Note = string.IsNullOrWhiteSpace(reason) ? payment.Note : $"Rejected: {reason}";
        audit.Add("payment.manual-rejected", nameof(SaasPayment), payment.Id.ToString(), payment.TenantId, "Rejected", reason);
        await notifier.QueueAsync(payment.TenantId, "payment-failed", "Payment was not confirmed",
            $"Your payment of {payment.Amount:0.00} {payment.Currency} (ref {payment.Reference}) could not be confirmed. {reason}", $"payment-rejected:{payment.Id}");
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- internals

    private async Task<PaymentResult> RejectAsync(PaymentRequest r, SaasInvoice? invoice, string reason)
    {
        await audit.LogAsync("payment.rejected", nameof(SaasInvoice), r.InvoiceId.ToString(), invoice?.TenantId, "Rejected",
            $"{reason} provider={r.Provider} txn={r.ProviderTransactionId} amount={r.Amount:0.00} {r.Currency}");
        return new(PaymentOutcome.Rejected, null, reason);
    }

    /// <summary>Adds the payment to the invoice, and when it is fully paid applies the invoice to the subscription. One save = one transaction.</summary>
    private async Task SettleAsync(SaasInvoice invoice, SaasPayment payment, DateTime now)
    {
        invoice.AmountPaid += payment.Amount;
        invoice.Status = invoice.AmountPaid >= invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        if (invoice.Status == InvoiceStatus.Paid)
        {
            invoice.PaidAt = now;
        }

        audit.Add("payment.recorded", nameof(SaasPayment), null, invoice.TenantId,
            details: $"invoice {invoice.InvoiceNumber}, {payment.Amount:0.00} {payment.Currency}, {payment.Method}/{payment.Provider}, txn {payment.ProviderTransactionId}");

        if (invoice.Status == InvoiceStatus.Paid && !invoice.AppliedToSubscription)
        {
            await ApplyInvoiceToSubscriptionAsync(invoice, now);
            invoice.AppliedToSubscription = true;
        }

        await notifier.QueueAsync(invoice.TenantId, "payment-received", "Payment received",
            $"We received {payment.Amount:0.00} {payment.Currency} for invoice {invoice.InvoiceNumber}. " +
            (invoice.Status == InvoiceStatus.Paid ? "The invoice is paid in full." : $"Outstanding balance: {invoice.Balance:0.00} {invoice.Currency}."),
            $"payment-received:{invoice.Id}:{invoice.AmountPaid:0.00}");

        // Save invoice + payment + subscription + tenant + audit together; the RowVersion on the invoice and
        // subscription makes a racing duplicate fail here and be retried as a duplicate.
        await db.SaveChangesAsync();

        if (payment.ReceiptNumber is null)
        {
            payment.ReceiptNumber = $"RCT-{now:yyyyMM}-{payment.Id:D6}";
            await db.SaveChangesAsync();
        }

        subscriptions.InvalidateAccess(invoice.TenantId);
    }

    /// <returns>True when access was restored from a non-payment suspension.</returns>
    private async Task<bool> ApplyInvoiceToSubscriptionAsync(SaasInvoice invoice, DateTime now)
    {
        if (invoice.Purpose == InvoicePurpose.Manual || invoice.SubscriptionId is null)
        {
            return false;
        }

        var sub = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == invoice.TenantId);
        if (sub is null || tenant is null)
        {
            return false;
        }

        var from = sub.Status;
        var plan = invoice.PlanId is int planId ? await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId) : null;

        if (plan is not null && plan.Id != sub.PlanId)
        {
            sub.PlanId = plan.Id;
            SubscriptionService.CopyTerms(sub, plan);
            sub.AutoRenew = plan.AutoRenew;
        }
        if (sub.PendingPlanId == invoice.PlanId)
        {
            sub.PendingPlanId = null;
        }

        sub.CurrentPeriodStart = invoice.PeriodStart;
        sub.CurrentPeriodEnd = invoice.PeriodEnd;
        sub.TrialEndsAt = null;
        sub.GraceEndsAt = null;
        sub.SuspendedAt = null;
        sub.UpdatedAt = now;

        // Other invoices still overdue keep the subscription behind on payment.
        var otherOverdue = await db.SaasInvoices.AnyAsync(i => i.SubscriptionId == sub.Id && i.Id != invoice.Id
            && (i.Status == InvoiceStatus.Overdue || ((i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate < now)));

        var restored = false;
        if (tenant.Status is TenantStatus.Disabled or TenantStatus.Terminated || sub.Status == SubscriptionStatus.Disabled)
        {
            // A payment never re-enables a disabled/terminated tenant.
        }
        else if (otherOverdue)
        {
            sub.Status = SubscriptionStatus.PastDue;
            var oldest = await db.SaasInvoices.Where(i => i.SubscriptionId == sub.Id && i.Id != invoice.Id && i.Total - i.AmountPaid > 0 && i.Status != InvoiceStatus.Cancelled).MinAsync(i => (DateTime?)i.DueDate);
            sub.GraceEndsAt = oldest?.AddDays(sub.GraceDays);
        }
        else
        {
            sub.Status = SubscriptionStatus.Active;
            if (tenant.SuspendedForNonPayment)
            {
                tenant.SuspendedForNonPayment = false;
                if (!tenant.IsAdminHold && tenant.Status == TenantStatus.Suspended)
                {
                    tenant.Status = TenantStatus.Active;
                    restored = true;
                }
                tenant.UpdatedAt = now;
            }
        }

        db.SubscriptionHistories.Add(new SubscriptionHistory
        {
            TenantId = sub.TenantId, SubscriptionId = sub.Id, Event = restored ? "subscription.reactivated" : "subscription.renewed",
            FromStatus = from, ToStatus = sub.Status, PlanId = sub.PlanId, Note = $"invoice {invoice.InvoiceNumber}", Actor = audit.Actor, CreatedAt = now
        });
        audit.Add(restored ? "subscription.reactivated" : "subscription.renewed", nameof(TenantSubscription), sub.Id.ToString(), sub.TenantId,
            details: $"invoice {invoice.InvoiceNumber}, period {invoice.PeriodStart:yyyy-MM-dd}..{invoice.PeriodEnd:yyyy-MM-dd}, status {from} -> {sub.Status}");

        await notifier.QueueAsync(sub.TenantId, restored ? "subscription-reactivated" : "subscription-activated",
            restored ? "Access restored" : "Subscription active",
            restored ? "Thank you - your payment was received and your account access is restored."
                     : $"Your subscription is active until {invoice.PeriodEnd:dd MMM yyyy}.",
            $"subscription-applied:{invoice.Id}");
        return restored;
    }
}
