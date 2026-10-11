namespace WebApplication1.Services.Saas.Payments;

public record ProviderInitRequest(
    string Reference, decimal Amount, string Currency, string InvoiceNumber,
    string CustomerName, string? CustomerEmail, string? CustomerPhone, string? CustomerAddress, string? CustomerCity,
    string SuccessUrl, string FailUrl, string CancelUrl, string IpnUrl);

public record ProviderInitResult(bool Succeeded, string? RedirectUrl, string? SessionKey, string? Error);

/// <summary>What the provider's server told us about a payment, after we asked it directly.</summary>
public enum VerificationStatus
{
    Paid,
    /// <summary>Paid but the provider marks it risky: hold until reviewed.</summary>
    PaidHeld,
    Pending,
    Failed,
    NotFound,
    /// <summary>Could not reach / understand the provider. Nothing is concluded; try again later.</summary>
    Error
}

public record ProviderVerification(
    VerificationStatus Status,
    string? Reference,
    string? ValId,
    string? TransactionId,
    decimal Amount,
    string Currency,
    string? Method,
    string? Message);

/// <summary>A callback/IPN as received. Nothing in it is trusted until <see cref="IPaymentProvider.VerifyAsync"/> confirms it.</summary>
public record ProviderCallback(
    string? Reference,
    string? ReportedStatus,
    string? ValId,
    /// <summary>true = signature present and correct, false = present and wrong, null = none supplied.</summary>
    bool? SignatureValid);

public record ProviderRefundRequest(string ProviderTransactionId, string RefundTransId, decimal Amount, string Reason, string? PaymentReference);

public record ProviderRefundResult(bool Accepted, string? ProviderRefundRef, bool AlreadyDone, string? Error);

public enum ProviderRefundState { Processing, Refunded, Cancelled, Unknown }

/// <summary>
/// One payment gateway. Billing code talks only to this interface, so bKash direct, Nagad or a bank can be
/// added later as another implementation without touching invoices, subscriptions or the callback flow.
/// </summary>
public interface IPaymentProvider
{
    string Code { get; }
    string DisplayName { get; }
    /// <summary>Credentials are present. An unconfigured provider is never offered.</summary>
    bool IsConfigured { get; }
    /// <summary>Provider's smallest chargeable amount in BDT (0 if none).</summary>
    decimal MinimumAmount { get; }

    Task<ProviderInitResult> InitiateAsync(ProviderInitRequest request, CancellationToken ct = default);

    ProviderCallback ParseCallback(IReadOnlyDictionary<string, string> fields);

    /// <summary>Server-to-server check of a payment. Prefers the provider's validation id, else queries by our reference / session.</summary>
    Task<ProviderVerification> VerifyAsync(string reference, string? valId, string? sessionKey, CancellationToken ct = default);

    Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken ct = default);
    Task<ProviderRefundState> GetRefundStateAsync(string providerRefundRef, CancellationToken ct = default);
}

public interface IPaymentProviderRegistry
{
    IPaymentProvider? Find(string code);
    IReadOnlyList<IPaymentProvider> All { get; }
}

public class PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers) : IPaymentProviderRegistry
{
    private readonly IReadOnlyList<IPaymentProvider> _providers = providers.ToList();
    public IReadOnlyList<IPaymentProvider> All => _providers;
    public IPaymentProvider? Find(string code) => _providers.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
}
