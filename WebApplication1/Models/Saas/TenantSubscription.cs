using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Tenancy;

namespace WebApplication1.Models.Saas;

/// <summary>
/// A tenant's single current subscription. Price, interval, grace and tax are copied from the plan
/// when it is assigned or renewed, so editing the plan never changes what an existing tenant pays.
/// </summary>
public class TenantSubscription
{
    public int Id { get; set; }

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int PlanId { get; set; }
    public SubscriptionPlan? Plan { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;

    // Terms in force (copied from the plan; an enterprise contract may override them).
    [Column(TypeName = "decimal(18,2)")] public decimal Price { get; set; }
    [StringLength(3)] public string Currency { get; set; } = "BDT";
    public int IntervalCount { get; set; } = 1;
    public BillingIntervalUnit IntervalUnit { get; set; } = BillingIntervalUnit.Month;
    public int GraceDays { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal TaxPercent { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal DiscountPercent { get; set; }
    public bool AutoRenew { get; set; } = true;

    // Per-tenant limit overrides (null = use the plan's limit).
    public int? MaxUsersOverride { get; set; }
    public int? MaxProductsOverride { get; set; }
    public int? MaxWarehousesOverride { get; set; }
    public int? MaxStorageMbOverride { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>Paid period currently covered. Null while on an unpaid trial.</summary>
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }

    /// <summary>When the oldest unpaid invoice's grace ends and the tenant is suspended.</summary>
    public DateTime? GraceEndsAt { get; set; }

    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? SuspendedAt { get; set; }

    /// <summary>Plan chosen for the next renewal (a downgrade takes effect at period end).</summary>
    public int? PendingPlanId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [Timestamp] public byte[]? RowVersion { get; set; }
}

public class TenantFeatureOverride
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, StringLength(60)] public string FeatureKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class SubscriptionHistory
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? SubscriptionId { get; set; }
    [Required, StringLength(60)] public string Event { get; set; } = string.Empty;
    public SubscriptionStatus? FromStatus { get; set; }
    public SubscriptionStatus? ToStatus { get; set; }
    public int? PlanId { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    [StringLength(256)] public string? Actor { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
