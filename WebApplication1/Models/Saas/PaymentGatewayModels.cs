using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Saas;

public enum PaymentAttemptStatus
{
    /// <summary>Created locally, provider not called yet.</summary>
    Initiated = 0,
    /// <summary>Customer sent to the provider; waiting for a verified result.</summary>
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Expired = 5,
    /// <summary>Provider flagged the payment as risky; money is held until a platform admin releases or refunds it.</summary>
    UnderReview = 6,
    /// <summary>Provider confirms payment but it could not be applied (invoice already paid/cancelled...). Needs a human: usually a refund.</summary>
    NeedsReview = 7
}

public enum RefundStatus { Pending = 0, Succeeded = 1, Failed = 2 }

/// <summary>
/// Which providers exist and whether they are switched on. Credentials never live here - they come from
/// configuration / environment variables (Payments:SslCommerz:StoreId ...).
/// </summary>
public class PaymentProviderConfig
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string Code { get; set; } = string.Empty;
    [Required, StringLength(120)] public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    /// <summary>Human-readable list shown to tenants, e.g. "bKash, Nagad, Rocket, cards, internet banking".</summary>
    [StringLength(300)] public string? SupportedMethods { get; set; }
    public int SortOrder { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [StringLength(256)] public string? UpdatedBy { get; set; }
}

/// <summary>One try to pay one invoice through a gateway. The reference is our tran_id and the idempotency key of the resulting payment.</summary>
public class PaymentAttempt
{
    public int Id { get; set; }

    [Required, StringLength(30)] public string Reference { get; set; } = string.Empty;

    public int InvoiceId { get; set; }
    public SaasInvoice? Invoice { get; set; }
    public int TenantId { get; set; }

    [Required, StringLength(40)] public string Provider { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, StringLength(3)] public string Currency { get; set; } = "BDT";

    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Initiated;

    /// <summary>Provider's session id, kept to query the transaction later.</summary>
    [StringLength(100)] public string? SessionKey { get; set; }
    [StringLength(600)] public string? GatewayUrl { get; set; }
    [StringLength(100)] public string? ProviderValId { get; set; }
    [StringLength(100)] public string? ProviderTransactionId { get; set; }
    [StringLength(500)] public string? FailureReason { get; set; }

    public int? SavedPaymentId { get; set; }

    [StringLength(256)] public string? InitiatedBy { get; set; }
    [StringLength(60)] public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }

    [Timestamp] public byte[]? RowVersion { get; set; }
}

/// <summary>Every callback/IPN we received, de-duplicated by <see cref="EventKey"/> (replays only bump ReceiveCount).</summary>
public class PaymentWebhookEvent
{
    public long Id { get; set; }
    [Required, StringLength(40)] public string Provider { get; set; } = string.Empty;
    [Required, StringLength(20)] public string Source { get; set; } = string.Empty;
    [Required, StringLength(80)] public string EventKey { get; set; } = string.Empty;
    [StringLength(30)] public string? AttemptReference { get; set; }
    [StringLength(30)] public string? ReportedStatus { get; set; }
    /// <summary>True/false when the provider signature was present and checked; null when the payload carried none.</summary>
    public bool? SignatureValid { get; set; }
    [StringLength(60)] public string? Outcome { get; set; }
    /// <summary>Received fields as JSON, minus anything secret.</summary>
    [StringLength(4000)] public string? Payload { get; set; }
    public int ReceiveCount { get; set; } = 1;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public class SaasRefund
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public int InvoiceId { get; set; }
    public int TenantId { get; set; }

    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    /// <summary>Our unique id sent to the provider (refund_trans_id).</summary>
    [StringLength(30)] public string? RefundTransId { get; set; }
    /// <summary>Provider's reference for the refund (refund_ref_id), used to poll its status.</summary>
    [StringLength(80)] public string? ProviderRefundRef { get; set; }
    [StringLength(500)] public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(256)] public string? CreatedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
}
