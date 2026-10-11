using System.ComponentModel.DataAnnotations;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Saas;

namespace WebApplication1.Models.ViewModels;

public class BillingIndexViewModel
{
    public Tenant Tenant { get; set; } = null!;
    public TenantSubscription? Subscription { get; set; }
    public SubscriptionPlan? Plan { get; set; }
    public SubscriptionPlan? PendingPlan { get; set; }
    public AccessDecision Access { get; set; } = null!;
    public PlanLimits Limits { get; set; } = new(null, null, null, null);
    public UsageSnapshot Usage { get; set; } = new(0, 0, 0);
    public IReadOnlySet<string> Features { get; set; } = new HashSet<string>();
    public List<SaasInvoice> OutstandingInvoices { get; set; } = [];
    public List<SaasNotification> Notifications { get; set; } = [];
    public bool IsOwner { get; set; }
}

public class BillingPlansViewModel
{
    public TenantSubscription? Subscription { get; set; }
    public List<SubscriptionPlan> Plans { get; set; } = [];
}

public class BillingProfileViewModel
{
    [Required, StringLength(200), Display(Name = "Business name")] public string Name { get; set; } = string.Empty;
    [StringLength(200), Display(Name = "Contact person")] public string? ContactName { get; set; }
    [EmailAddress, StringLength(256), Display(Name = "Contact email")] public string? ContactEmail { get; set; }
    [EmailAddress, StringLength(256), Display(Name = "Billing email (invoices and receipts)")] public string? BillingEmail { get; set; }
    [StringLength(50), Display(Name = "Phone")] public string? ContactPhone { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(50), Display(Name = "Tax / BIN number")] public string? TaxId { get; set; }
}
