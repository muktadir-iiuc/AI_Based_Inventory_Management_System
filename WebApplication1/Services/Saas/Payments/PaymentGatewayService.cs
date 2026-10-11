using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services.Saas.Payments;

public record StartPaymentResult(bool Succeeded, string? RedirectUrl, string? AttemptReference, string? Error);

public record CallbackResult(string Outcome, string? AttemptReference, PaymentAttemptStatus? AttemptStatus);

public interface IPaymentGatewayService
{
    /// <summary>Begins an online payment for the tenant's own invoice (the full outstanding balance). Amount, tenant and invoice come from our records only.</summary>
    Task<StartPaymentResult> StartAsync(int tenantId, int invoiceId, string providerCode, string publicBaseUrl, string? initiatedBy, string? ipAddress, CancellationToken ct = default);

    /// <summary>Handles a provider callback or IPN. Never trusts the posted status: it asks the provider, then validates tenant, invoice, amount and currency against our attempt.</summary>
    Task<CallbackResult> HandleCallbackAsync(string providerCode, string source, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default);

    /// <summary>Re-checks attempts whose result never arrived, and expires old ones. Safe to run repeatedly.</summary>
    Task<int> ReconcileAsync(CancellationToken ct = default);

    /// <summary>Platform admin accepts a payment the provider held as risky.</summary>
    Task<PaymentResult> ReleaseHeldAsync(int attemptId, string? actor = null, CancellationToken ct = default);
}

public class PaymentGatewayService(
    ApplicationDbContext db,
    ITenantContext tenantContext,
    IPaymentProviderRegistry providers,
    IBillingPaymentService billingPayments,
    IPlatformSettings settings,
    ISaasNotifier notifier,
    ISaasAudit audit,
    TimeProvider time,
    ILogger<PaymentGatewayService> logger) : IPaymentGatewayService
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private static readonly InvoiceStatus[] Payable = [InvoiceStatus.Pending, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    // ------------------------------------------------------------------ start

    public async Task<StartPaymentResult> StartAsync(int tenantId, int invoiceId, string providerCode, string publicBaseUrl, string? initiatedBy, string? ipAddress, CancellationToken ct = default)
    {
        var provider = providers.Find(providerCode);
        var config = await db.PaymentProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Code == providerCode, ct);
        if (provider is null || !provider.IsConfigured || config is not { IsEnabled: true })
        {
            return new(false, null, null, "This payment method is not available.");
        }

        // The invoice must belong to the caller's tenant; the amount is whatever is still owed - never a client value.
        var invoice = await db.SaasInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, ct);
        if (invoice is null) return new(false, null, null, "Invoice not found.");
        if (!Payable.Contains(invoice.Status)) return new(false, null, null, $"Invoice is {invoice.Status} and cannot be paid.");
        var balance = invoice.Total - invoice.AmountPaid;
        if (balance <= 0) return new(false, null, null, "Nothing is owed on this invoice.");
        if (balance < provider.MinimumAmount) return new(false, null, null, $"The online minimum is {provider.MinimumAmount:0.00} {invoice.Currency}. Pay this balance by bank transfer or cash instead.");

        var now = Now;
        var open = await db.PaymentAttempts.CountAsync(a => a.InvoiceId == invoice.Id && (a.Status == PaymentAttemptStatus.Initiated || a.Status == PaymentAttemptStatus.Pending) && a.ExpiresAt > now, ct);
        if (open >= 5) return new(false, null, null, "Too many unfinished payment attempts for this invoice. Wait a few minutes and try again.");

        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, ct);
        var expiryMinutes = await settings.GetIntAsync(PlatformSettingKeys.AttemptExpiryMinutes, 120);
        var attempt = new PaymentAttempt
        {
            Reference = "PA" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)), // 26 chars, unguessable, fits the provider's 30-char tran_id
            InvoiceId = invoice.Id, TenantId = tenantId, Provider = provider.Code, Amount = balance, Currency = invoice.Currency,
            Status = PaymentAttemptStatus.Initiated, InitiatedBy = initiatedBy, IpAddress = ipAddress, CreatedAt = now, ExpiresAt = now.AddMinutes(expiryMinutes)
        };
        db.PaymentAttempts.Add(attempt);
        audit.Add("payment.attempt-created", nameof(PaymentAttempt), attempt.Reference, tenantId, details: $"invoice {invoice.InvoiceNumber}, {balance:0.00} {invoice.Currency}, {provider.Code}");
        await db.SaveChangesAsync(ct);

        var baseUrl = publicBaseUrl.TrimEnd('/');
        string Url(string kind) => $"{baseUrl}/Payments/Callback/{provider.Code}/{kind}";
        var result = await provider.InitiateAsync(new ProviderInitRequest(
            attempt.Reference, balance, invoice.Currency, invoice.InvoiceNumber,
            tenant.Name, tenant.BillingEmail ?? tenant.ContactEmail, tenant.ContactPhone, tenant.Address, tenant.City,
            Url("success"), Url("fail"), Url("cancel"), Url("ipn")), ct);

        if (!result.Succeeded)
        {
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.FailureReason = result.Error;
            attempt.CompletedAt = Now;
            audit.Add("payment.attempt-failed", nameof(PaymentAttempt), attempt.Reference, tenantId, "Failed", result.Error);
            await db.SaveChangesAsync(ct);
            return new(false, null, attempt.Reference, result.Error ?? "Could not start the payment.");
        }

        attempt.Status = PaymentAttemptStatus.Pending;
        attempt.SessionKey = result.SessionKey;
        attempt.GatewayUrl = result.RedirectUrl is { Length: > 600 } ? result.RedirectUrl[..600] : result.RedirectUrl;
        await db.SaveChangesAsync(ct);
        return new(true, result.RedirectUrl, attempt.Reference, null);
    }

    // ------------------------------------------------------------------ callback

    public async Task<CallbackResult> HandleCallbackAsync(string providerCode, string source, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        // Callbacks arrive anonymously: bind to the attempt's tenant explicitly, never to anything in the request.
        using var _ = tenantContext.BypassFilters();

        var provider = providers.Find(providerCode);
        if (provider is null) return new("unknown-provider", null, null);

        var callback = provider.ParseCallback(fields);
        var webhook = await RecordEventAsync(provider.Code, source, callback, fields, ct);

        async Task<CallbackResult> Done(string outcome, PaymentAttempt? attempt)
        {
            webhook.Outcome = outcome;
            webhook.ProcessedAt = Now;
            await db.SaveChangesAsync(ct);
            return new(outcome, attempt?.Reference, attempt?.Status);
        }

        if (callback.SignatureValid == false)
        {
            await audit.LogAsync("payment.callback-bad-signature", nameof(PaymentWebhookEvent), webhook.Id.ToString(), null, "Rejected", $"{provider.Code} {source} ref {callback.Reference}");
            return await Done("bad-signature", null);
        }
        if (callback.Reference is null)
        {
            return await Done("missing-reference", null);
        }

        var attempt = await db.PaymentAttempts.FirstOrDefaultAsync(a => a.Reference == callback.Reference && a.Provider == provider.Code, ct);
        if (attempt is null)
        {
            return await Done("unknown-reference", null);
        }

        if (attempt.Status == PaymentAttemptStatus.Succeeded)
        {
            return await Done("already-processed", attempt);
        }

        // Ask the provider. The posted status/amount is only a hint about where to look.
        var verification = await provider.VerifyAsync(attempt.Reference, callback.ValId, attempt.SessionKey, ct);
        attempt.LastCheckedAt = Now;
        return await ApplyVerificationAsync(attempt, verification, source, callback.ReportedStatus, Done, ct);
    }

    private async Task<CallbackResult> ApplyVerificationAsync(
        PaymentAttempt attempt, ProviderVerification v, string source, string? reportedStatus,
        Func<string, PaymentAttempt?, Task<CallbackResult>> done, CancellationToken ct)
    {
        switch (v.Status)
        {
            case VerificationStatus.Error:
                await db.SaveChangesAsync(ct);
                return await done("verification-unavailable", attempt);   // reconcile job retries

            case VerificationStatus.Paid or VerificationStatus.PaidHeld:
                // Everything below is checked against OUR attempt, not against the request.
                if (!string.Equals(v.Reference, attempt.Reference, StringComparison.Ordinal)
                    || v.Amount != attempt.Amount
                    || !string.Equals(v.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    attempt.Status = PaymentAttemptStatus.NeedsReview;
                    attempt.FailureReason = $"Provider reported {v.Amount:0.00} {v.Currency} (ref {v.Reference}); expected {attempt.Amount:0.00} {attempt.Currency}.";
                    audit.Add("payment.verification-mismatch", nameof(PaymentAttempt), attempt.Reference, attempt.TenantId, "Rejected", attempt.FailureReason);
                    return await done("amount-or-reference-mismatch", attempt);
                }

                attempt.ProviderValId = v.ValId;
                attempt.ProviderTransactionId = v.TransactionId;

                if (v.Status == VerificationStatus.PaidHeld)
                {
                    attempt.Status = PaymentAttemptStatus.UnderReview;
                    attempt.FailureReason = v.Message;
                    audit.Add("payment.held-for-review", nameof(PaymentAttempt), attempt.Reference, attempt.TenantId, details: v.Message);
                    return await done("held-for-review", attempt);
                }

                return await SettleAsync(attempt, v, done, ct);

            case VerificationStatus.Pending:
                attempt.Status = PaymentAttemptStatus.Pending;
                return await done("pending", attempt);

            default: // Failed or NotFound
                // A cancel/fail redirect with nothing paid on the provider's side closes the attempt; an IPN/other source only
                // closes it when the provider itself says failed (NotFound can simply mean "customer has not paid yet").
                var cancelled = source == "cancel" || string.Equals(reportedStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase);
                if (v.Status == VerificationStatus.Failed || source is "cancel" or "fail" || reportedStatus is "FAILED" or "CANCELLED" or "EXPIRED")
                {
                    attempt.Status = cancelled ? PaymentAttemptStatus.Cancelled : PaymentAttemptStatus.Failed;
                    attempt.FailureReason = v.Message ?? reportedStatus;
                    attempt.CompletedAt = Now;
                    audit.Add(cancelled ? "payment.attempt-cancelled" : "payment.attempt-failed", nameof(PaymentAttempt), attempt.Reference, attempt.TenantId, "Failed", attempt.FailureReason);
                    await notifier.QueueAsync(attempt.TenantId, "payment-failed", cancelled ? "Payment cancelled" : "Payment failed",
                        cancelled ? "You cancelled the payment. No money was taken. You can try again from the invoice."
                                  : "Your payment did not go through and no money was taken. You can try again from the invoice.",
                        $"attempt-failed:{attempt.Reference}");
                    return await done(cancelled ? "cancelled" : "failed", attempt);
                }
                return await done("not-paid-yet", attempt);
        }
    }

    /// <summary>Records the verified payment through the single idempotent billing path and closes the attempt.</summary>
    private async Task<CallbackResult> SettleAsync(PaymentAttempt attempt, ProviderVerification v,
        Func<string, PaymentAttempt?, Task<CallbackResult>> done, CancellationToken ct)
    {
        var attemptId = attempt.Id;
        var reference = attempt.Reference;
        var request = new PaymentRequest(
            attempt.InvoiceId, attempt.Amount, attempt.Currency, string.IsNullOrWhiteSpace(v.Method) ? "Online" : v.Method!, attempt.Provider,
            ProviderTransactionId: v.TransactionId ?? v.ValId, IdempotencyKey: reference, Reference: v.ValId,
            ExpectedTenantId: attempt.TenantId, ReceivedAt: Now);

        // The payment service may clear the change tracker on a concurrent-duplicate retry: reload the attempt afterwards.
        var result = await billingPayments.ApplyPaymentAsync(request);
        attempt = await db.PaymentAttempts.FirstAsync(a => a.Id == attemptId, ct);

        if (result.Outcome is PaymentOutcome.Applied or PaymentOutcome.Duplicate)
        {
            attempt.Status = PaymentAttemptStatus.Succeeded;
            attempt.ProviderValId = v.ValId;
            attempt.ProviderTransactionId = v.TransactionId;
            attempt.SavedPaymentId = result.Payment?.Id;
            attempt.CompletedAt ??= Now;
            return await done(result.Outcome == PaymentOutcome.Applied ? "applied" : "duplicate", attempt);
        }

        // The provider took the money but the invoice can no longer accept it (e.g. paid twice). A human must refund or reassign it.
        attempt.Status = PaymentAttemptStatus.NeedsReview;
        attempt.FailureReason = result.Message;
        attempt.ProviderValId = v.ValId;
        attempt.ProviderTransactionId = v.TransactionId;
        await audit.LogAsync("payment.needs-review", nameof(PaymentAttempt), reference, attempt.TenantId, "Rejected",
            $"Provider confirmed payment {v.TransactionId} ({v.Amount:0.00} {v.Currency}) but it could not be applied: {result.Message}");
        logger.LogWarning("Verified payment for attempt {Reference} could not be applied: {Reason}", reference, result.Message);
        return await done("needs-review", attempt);
    }

    private async Task<PaymentWebhookEvent> RecordEventAsync(string provider, string source, ProviderCallback cb, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{provider}|{source}|{cb.Reference}|{cb.ValId}|{cb.ReportedStatus}"))).ToLowerInvariant()[..40];

        var existing = await db.PaymentWebhookEvents.FirstOrDefaultAsync(e => e.Provider == provider && e.EventKey == key, ct);
        if (existing is not null)
        {
            existing.ReceiveCount++;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        // Never persist anything secret-looking, and cap the size.
        var blocked = new[] { "store_passwd", "password", "card_no", "verify_sign" };
        var safe = fields.Where(kv => !blocked.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var payload = JsonSerializer.Serialize(safe);

        var evt = new PaymentWebhookEvent
        {
            Provider = provider, Source = source, EventKey = key, AttemptReference = cb.Reference, ReportedStatus = cb.ReportedStatus,
            SignatureValid = cb.SignatureValid, Payload = payload.Length > 4000 ? payload[..4000] : payload, ReceivedAt = Now
        };
        db.PaymentWebhookEvents.Add(evt);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent identical callback inserted it first.
            db.Entry(evt).State = EntityState.Detached;
            var winner = await db.PaymentWebhookEvents.FirstAsync(e => e.Provider == provider && e.EventKey == key, ct);
            winner.ReceiveCount++;
            await db.SaveChangesAsync(ct);
            return winner;
        }
        return evt;
    }

    // ------------------------------------------------------------------ reconcile

    public async Task<int> ReconcileAsync(CancellationToken ct = default)
    {
        using var _ = tenantContext.BypassFilters();
        var now = Now;
        var changed = 0;

        // Anything unfinished for more than a few minutes: ask the provider directly (covers lost callbacks and crashes mid-settlement).
        var stale = await db.PaymentAttempts
            .Where(a => (a.Status == PaymentAttemptStatus.Pending || a.Status == PaymentAttemptStatus.Initiated) && a.CreatedAt <= now.AddMinutes(-5))
            .OrderBy(a => a.LastCheckedAt ?? a.CreatedAt).Take(50).ToListAsync(ct);

        foreach (var attempt in stale)
        {
            var provider = providers.Find(attempt.Provider);
            if (provider is null) continue;

            var verification = await provider.VerifyAsync(attempt.Reference, attempt.ProviderValId, attempt.SessionKey, ct);
            attempt.LastCheckedAt = now;

            var expired = attempt.ExpiresAt <= now;
            if (verification.Status is VerificationStatus.Paid or VerificationStatus.PaidHeld)
            {
                await ApplyVerificationAsync(attempt, verification, "reconcile", null, (o, a) => Task.FromResult(new CallbackResult(o, a?.Reference, a?.Status)), ct);
                changed++;
            }
            else if (verification.Status == VerificationStatus.Error)
            {
                continue;
            }
            else if (expired && verification.Status != VerificationStatus.Pending)
            {
                attempt.Status = PaymentAttemptStatus.Expired;
                attempt.CompletedAt = now;
                audit.Add("payment.attempt-expired", nameof(PaymentAttempt), attempt.Reference, attempt.TenantId);
                changed++;
            }
            await db.SaveChangesAsync(ct);
        }
        return changed;
    }

    public async Task<PaymentResult> ReleaseHeldAsync(int attemptId, string? actor = null, CancellationToken ct = default)
    {
        using var _ = tenantContext.BypassFilters();
        var attempt = await db.PaymentAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException("Attempt not found.");
        if (attempt.Status != PaymentAttemptStatus.UnderReview)
        {
            throw new InvalidOperationException("Only a payment under review can be released.");
        }

        var provider = providers.Find(attempt.Provider) ?? throw new InvalidOperationException("Provider not available.");
        // Re-verify at release time: never release on stored data alone.
        var v = await provider.VerifyAsync(attempt.Reference, attempt.ProviderValId, attempt.SessionKey, ct);
        if (v.Status is not (VerificationStatus.Paid or VerificationStatus.PaidHeld)
            || v.Amount != attempt.Amount || !string.Equals(v.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return new(PaymentOutcome.Rejected, null, "The provider no longer confirms this payment.");
        }

        audit.Add("payment.held-released", nameof(PaymentAttempt), attempt.Reference, attempt.TenantId, details: $"released by {actor ?? audit.Actor}");
        var result = await billingPayments.ApplyPaymentAsync(new PaymentRequest(attempt.InvoiceId, attempt.Amount, attempt.Currency,
            string.IsNullOrWhiteSpace(v.Method) ? "Online" : v.Method!, attempt.Provider, v.TransactionId ?? v.ValId, attempt.Reference, v.ValId, ExpectedTenantId: attempt.TenantId, ReceivedAt: Now));

        attempt = await db.PaymentAttempts.FirstAsync(a => a.Id == attemptId, ct);
        if (result.Succeeded)
        {
            attempt.Status = PaymentAttemptStatus.Succeeded;
            attempt.SavedPaymentId = result.Payment?.Id;
            attempt.CompletedAt = Now;
            await db.SaveChangesAsync(ct);
        }
        return result;
    }
}
