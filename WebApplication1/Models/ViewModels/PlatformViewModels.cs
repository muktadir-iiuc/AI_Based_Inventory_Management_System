using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Saas;

namespace WebApplication1.Models.ViewModels;

/// <summary>What the shared pager partial needs.</summary>
public record PagerInfo(int Page, int TotalPages, int Total);

public class PagedResult<T>
{
    public PagerInfo Pager => new(Page, TotalPages, Total);
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize)
    {
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pages);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<T> { Items = items, Page = page, PageSize = pageSize, Total = total };
    }
}

// -------------------------------------------------------------------- tenants

public class TenantListItem
{
    public Tenant Tenant { get; set; } = null!;
    public TenantSubscription? Subscription { get; set; }
    public string? PlanName { get; set; }
    public int Users { get; set; }
}

public class TenantListViewModel
{
    public PagedResult<TenantListItem> Page { get; set; } = new();
    public string? Q { get; set; }
    public string? Status { get; set; }
    public string? SubStatus { get; set; }
    public string Sort { get; set; } = "created";
}

public class TenantCreateViewModel
{
    [Required, StringLength(200), Display(Name = "Business name")] public string Name { get; set; } = string.Empty;
    [StringLength(40), Display(Name = "Business code (optional)")] public string? Code { get; set; }
    [Required, StringLength(200), Display(Name = "Owner name")] public string OwnerName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256), Display(Name = "Owner email")] public string OwnerEmail { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Initial password")] public string OwnerPassword { get; set; } = string.Empty;
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(50), Display(Name = "Tax / BIN")] public string? TaxId { get; set; }
    [Required, Display(Name = "Plan")] public int PlanId { get; set; }
    [Display(Name = "Start")] public StartMode StartMode { get; set; } = StartMode.Trial;
    public List<SubscriptionPlan> Plans { get; set; } = [];
}

public class TenantProfileViewModel
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(200), Display(Name = "Contact person")] public string? ContactName { get; set; }
    [EmailAddress, StringLength(256), Display(Name = "Contact email")] public string? ContactEmail { get; set; }
    [EmailAddress, StringLength(256), Display(Name = "Billing email")] public string? BillingEmail { get; set; }
    [StringLength(50)] public string? ContactPhone { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [StringLength(50), Display(Name = "Tax / BIN")] public string? TaxId { get; set; }
}

public class TenantUserRow
{
    public ApplicationUser User { get; set; } = null!;
    public List<string> Roles { get; set; } = [];
    public bool IsOwner { get; set; }
}

public class TenantDetailsViewModel
{
    public Tenant Tenant { get; set; } = null!;
    public TenantSubscription? Subscription { get; set; }
    public SubscriptionPlan? Plan { get; set; }
    public List<SubscriptionPlan> AllPlans { get; set; } = [];
    public AccessDecision Access { get; set; } = null!;
    public PlanLimits Limits { get; set; } = new(null, null, null, null);
    public UsageSnapshot Usage { get; set; } = new(0, 0, 0);
    public HashSet<string> PlanFeatures { get; set; } = [];
    public Dictionary<string, bool> FeatureOverrides { get; set; } = [];
    public List<SaasInvoice> Invoices { get; set; } = [];
    public List<SaasPayment> Payments { get; set; } = [];
    public List<SubscriptionHistory> History { get; set; } = [];
    public List<SaasAuditLog> Audit { get; set; } = [];
    public List<TenantUserRow> Users { get; set; } = [];
    public DateTime? PurgeEligibleOn { get; set; }
    public bool IsAdmin { get; set; }
    public TenantLinks Links { get; set; } = null!;
}

public record PasswordResetViewModel(int TenantId, string TenantName, string Email, string Password, string SignInUrl);

// -------------------------------------------------------------------- plans

public class PlanEditViewModel
{
    public int Id { get; set; }
    [Required, StringLength(40), RegularExpression("^[a-z0-9][a-z0-9-]*$", ErrorMessage = "Lowercase letters, digits and dashes only.")] public string Code { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
    [Range(0, 100000000)] public decimal Price { get; set; }
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "BDT";
    [Range(1, 1200), Display(Name = "Every")] public int IntervalCount { get; set; } = 1;
    [Display(Name = "Interval unit")] public BillingIntervalUnit IntervalUnit { get; set; } = BillingIntervalUnit.Month;
    [Range(0, 365), Display(Name = "Free trial (days)")] public int TrialDays { get; set; }
    [Range(0, 90), Display(Name = "Grace period (days)")] public int GraceDays { get; set; } = 7;
    [Range(0, 100), Display(Name = "Tax %")] public decimal TaxPercent { get; set; }
    [Range(0, 100), Display(Name = "Discount %")] public decimal DiscountPercent { get; set; }
    [Range(0, int.MaxValue), Display(Name = "Max users (blank = unlimited)")] public int? MaxUsers { get; set; }
    [Range(0, int.MaxValue), Display(Name = "Max products")] public int? MaxProducts { get; set; }
    [Range(0, int.MaxValue), Display(Name = "Max warehouses")] public int? MaxWarehouses { get; set; }
    [Range(0, int.MaxValue), Display(Name = "Max storage (MB)")] public int? MaxStorageMb { get; set; }
    [Display(Name = "Auto-renew")] public bool AutoRenew { get; set; } = true;
    [Display(Name = "Visible to tenants")] public bool IsPublic { get; set; } = true;
    public PlanStatus Status { get; set; } = PlanStatus.Active;
    [Display(Name = "Available from")] public DateTime? AvailableFrom { get; set; }
    [Display(Name = "Available until")] public DateTime? AvailableTo { get; set; }
    [Display(Name = "Sort order")] public int SortOrder { get; set; }
    public List<string> FeatureKeys { get; set; } = [];
}

// -------------------------------------------------------------------- invoices, payments

public class InvoiceListViewModel
{
    public PagedResult<SaasInvoice> Page { get; set; } = new();
    public string? Q { get; set; }
    public string? Status { get; set; }
    public int? TenantId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class PaymentsListViewModel
{
    public string Tab { get; set; } = "payments";
    public PagedResult<SaasPayment> Payments { get; set; } = new();
    public PagedResult<PaymentAttempt> Attempts { get; set; } = new();
    public Dictionary<int, string> TenantNames { get; set; } = [];
    public int AwaitingCount { get; set; }
    public int ReviewCount { get; set; }
    public bool IsAdmin { get; set; }
}

public class AuditListViewModel
{
    public PagedResult<SaasAuditLog> Page { get; set; } = new();
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public string? Result { get; set; }
    public int? TenantId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class SettingsViewModel
{
    public Dictionary<string, string> Values { get; set; } = [];
}

public class RegisterViewModel
{
    [Required, StringLength(200), Display(Name = "Business name")] public string BusinessName { get; set; } = string.Empty;
    [Required, StringLength(200), Display(Name = "Your name")] public string OwnerName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256), Display(Name = "Email")] public string Email { get; set; } = string.Empty;
    [StringLength(50), Display(Name = "Phone")] public string? Phone { get; set; }
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = string.Empty;
    [Display(Name = "I accept the terms of service and privacy policy")] public bool AcceptTerms { get; set; }
    /// <summary>Honeypot: real users never see or fill this; bots do.</summary>
    public string? Website { get; set; }
    public string? Message { get; set; }
}

public class PlatformCompanyViewModel : CompanySettingsViewModel
{
    public int TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    [Required, StringLength(200), Display(Name = "Business name (platform record)")] public string TenantName { get; set; } = string.Empty;
}

public class PlatformLoginViewModel
{
    [Required, EmailAddress, Display(Name = "Email")] public string Email { get; set; } = string.Empty;
    [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public bool HasLogo { get; set; }
}

public class StaffCreateViewModel
{
    [Required, StringLength(200), Display(Name = "Full name")] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = Roles.PlatformBilling;
}
