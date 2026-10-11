using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Tenancy;

namespace WebApplication1.Models.Saas;

/// <summary>A subscription invoice. Plan name, prices, tax and period are stored on it and never recomputed.</summary>
public class SaasInvoice
{
    public int Id { get; set; }

    [Required, StringLength(40)] public string InvoiceNumber { get; set; } = string.Empty;

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int? SubscriptionId { get; set; }
    public int? PlanId { get; set; }
    [StringLength(120)] public string PlanName { get; set; } = string.Empty;

    public InvoicePurpose Purpose { get; set; } = InvoicePurpose.Renewal;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; }

    [Required, StringLength(3)] public string Currency { get; set; } = "BDT";

    [Column(TypeName = "decimal(18,2)")] public decimal SubTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DiscountTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Total { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AmountPaid { get; set; }

    [NotMapped] public decimal Balance => Total - AmountPaid;

    // Billing details as at issue time.
    [StringLength(200)] public string? BillToName { get; set; }
    [StringLength(256)] public string? BillToEmail { get; set; }
    [StringLength(300)] public string? BillToAddress { get; set; }
    [StringLength(50)] public string? BillToTaxId { get; set; }

    [StringLength(1000)] public string? Notes { get; set; }

    /// <summary>True once the subscription was extended/activated because of this invoice, so a repeated payment can never extend it twice.</summary>
    public bool AppliedToSubscription { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(256)] public string? CreatedBy { get; set; }

    /// <summary>Reminders already queued for this invoice ("due-3d", "overdue" ...), comma separated.</summary>
    [StringLength(400)] public string? RemindersSent { get; set; }

    [Timestamp] public byte[]? RowVersion { get; set; }

    public ICollection<SaasInvoiceItem> Items { get; set; } = [];
    public ICollection<SaasPayment> Payments { get; set; } = [];
}

public class SaasInvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public SaasInvoice? Invoice { get; set; }
    public InvoiceItemType ItemType { get; set; } = InvoiceItemType.Subscription;
    [Required, StringLength(300)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal Quantity { get; set; } = 1;
    [Column(TypeName = "decimal(18,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}

public class SaasPayment
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public SaasInvoice? Invoice { get; set; }
    public int TenantId { get; set; }

    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, StringLength(3)] public string Currency { get; set; } = "BDT";

    public SaasPaymentStatus Status { get; set; } = SaasPaymentStatus.Succeeded;

    /// <summary>Cash, BankTransfer, bKash, Nagad, Rocket, Card ...</summary>
    [Required, StringLength(40)] public string Method { get; set; } = "Cash";

    /// <summary>"manual" or the gateway that processed it ("sslcommerz").</summary>
    [Required, StringLength(40)] public string Provider { get; set; } = "manual";

    /// <summary>The provider's own transaction id. Unique per provider where present: a repeated notification cannot create a second payment.</summary>
    [StringLength(100)] public string? ProviderTransactionId { get; set; }

    /// <summary>Caller-chosen key (e.g. payment attempt reference). Unique where present.</summary>
    [StringLength(100)] public string? IdempotencyKey { get; set; }

    /// <summary>Bank slip / receipt number etc. for manual payments.</summary>
    [StringLength(200)] public string? Reference { get; set; }

    [Column(TypeName = "decimal(18,2)")] public decimal RefundedAmount { get; set; }

    [StringLength(30)] public string? ReceiptNumber { get; set; }
    [StringLength(500)] public string? Note { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(256)] public string? RecordedBy { get; set; }
    [StringLength(256)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public bool IsReconciled { get; set; }
}
