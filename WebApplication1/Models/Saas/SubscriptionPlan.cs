using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Saas;

/// <summary>A sellable plan. Editable by the platform admin; invoices copy what they need so later edits never rewrite history.</summary>
public class SubscriptionPlan
{
    public int Id { get; set; }

    [Required, StringLength(40)] public string Code { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }

    [Column(TypeName = "decimal(18,2)"), Range(0, 100000000)] public decimal Price { get; set; }
    [Required, StringLength(3)] public string Currency { get; set; } = "BDT";

    /// <summary>One billing cycle is IntervalCount x IntervalUnit (1 month, 12 months, 90 days...).</summary>
    [Range(1, 1200)] public int IntervalCount { get; set; } = 1;
    public BillingIntervalUnit IntervalUnit { get; set; } = BillingIntervalUnit.Month;

    /// <summary>Free trial length in days when a tenant starts on this plan. 0 = no trial.</summary>
    [Range(0, 365)] public int TrialDays { get; set; }

    /// <summary>Days after an invoice's due date before an unpaid tenant is suspended.</summary>
    [Range(0, 90)] public int GraceDays { get; set; } = 7;

    [Column(TypeName = "decimal(5,2)"), Range(0, 100)] public decimal TaxPercent { get; set; }
    [Column(TypeName = "decimal(5,2)"), Range(0, 100)] public decimal DiscountPercent { get; set; }

    // Null = unlimited.
    public int? MaxUsers { get; set; }
    public int? MaxProducts { get; set; }
    public int? MaxWarehouses { get; set; }
    public int? MaxStorageMb { get; set; }

    public bool AutoRenew { get; set; } = true;

    /// <summary>Shown to tenants on the Available Plans page. Custom/enterprise contracts are assigned by a platform admin instead.</summary>
    public bool IsPublic { get; set; } = true;

    public PlanStatus Status { get; set; } = PlanStatus.Active;
    public DateTime? AvailableFrom { get; set; }
    public DateTime? AvailableTo { get; set; }
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<PlanFeature> Features { get; set; } = [];

    public bool IsAvailable(DateTime utcNow) =>
        Status == PlanStatus.Active
        && (AvailableFrom is null || AvailableFrom <= utcNow)
        && (AvailableTo is null || AvailableTo >= utcNow);
}

public class PlanFeature
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public SubscriptionPlan? Plan { get; set; }
    [Required, StringLength(60)] public string FeatureKey { get; set; } = string.Empty;
}
