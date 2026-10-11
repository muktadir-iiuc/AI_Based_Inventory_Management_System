using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.Tenancy;

public enum TenantStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2,
    Disabled = 3,
    Terminated = 4
}

/// <summary>
/// One subscribing business. Every business-data row carries this tenant's id (shadow property
/// "TenantId", see ApplicationDbContext). Suspension (non-payment or administrative), disablement
/// and termination are separate states with separate effects - see TenantAccessPolicy.
/// </summary>
public class Tenant
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Short unique slug, e.g. "bd-tyre".</summary>
    [Required, StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [StringLength(200)] public string? ContactName { get; set; }
    [StringLength(256)] public string? ContactEmail { get; set; }
    [StringLength(50)] public string? ContactPhone { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Country { get; set; } = "Bangladesh";
    [StringLength(50)] public string? TaxId { get; set; }
    /// <summary>Where invoices and billing notices are sent (falls back to ContactEmail).</summary>
    [StringLength(256)] public string? BillingEmail { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>
    /// Independent administrative restriction (fraud, security, abuse). While set, no payment
    /// restores access; only a platform administrator can clear it.
    /// </summary>
    public bool IsAdminHold { get; set; }
    [StringLength(500)] public string? AdminHoldReason { get; set; }

    /// <summary>Set when the lifecycle job suspended the tenant for non-payment (never for admin holds).</summary>
    public bool SuspendedForNonPayment { get; set; }

    /// <summary>The tenant's own web address (e.g. https://shop.example.com). Sign-in on that host is limited to this tenant.</summary>
    [StringLength(300)] public string? PortalUrl { get; set; }
    /// <summary>Lower-case host of <see cref="PortalUrl"/>; unique, used to recognise the tenant from the request.</summary>
    [StringLength(253)] public string? PortalHost { get; set; }

    /// <summary>Identity user id of the tenant owner.</summary>
    [StringLength(450)] public string? OwnerUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(256)] public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Soft delete / termination time. Data kept until the retention period ends.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Set when the business data was permanently erased after the retention period. Invoices, payments and audit records are kept.</summary>
    public DateTime? PurgedAt { get; set; }

    [Timestamp] public byte[]? RowVersion { get; set; }
}
