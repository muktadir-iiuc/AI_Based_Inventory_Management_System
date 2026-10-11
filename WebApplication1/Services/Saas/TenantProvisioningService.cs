using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services.Saas;

public record TenantCreateRequest(
    string Name,
    string OwnerName,
    string OwnerEmail,
    string OwnerPassword,
    int PlanId,
    StartMode StartMode,
    string? Code = null,
    string? ContactPhone = null,
    string? Address = null,
    string? City = null,
    string? TaxId = null);

public record TenantCreateResult(bool Succeeded, Tenant? Tenant, IReadOnlyList<string> Errors);

public interface ITenantProvisioningService
{
    /// <summary>Creates a tenant with its company profile, chart of accounts, first warehouse, owner (tenant Admin) and subscription.</summary>
    Task<TenantCreateResult> CreateAsync(TenantCreateRequest request, string? actor = null);
}

public partial class TenantProvisioningService(
    ApplicationDbContext db,
    ITenantContext tenantContext,
    UserManager<ApplicationUser> userManager,
    ISubscriptionService subscriptions,
    ISaasNotifier notifier,
    ISaasAudit audit,
    TimeProvider time) : ITenantProvisioningService
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    public static string Slugify(string name)
    {
        var slug = NonSlug().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "business" : slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }

    public async Task<TenantCreateResult> CreateAsync(TenantCreateRequest r, string? actor = null)
    {
        var errors = new List<string>();
        var name = r.Name?.Trim() ?? string.Empty;
        var email = r.OwnerEmail?.Trim() ?? string.Empty;
        if (name.Length < 2) errors.Add("Business name is required.");
        if (string.IsNullOrWhiteSpace(r.OwnerName)) errors.Add("Owner name is required.");
        if (!email.Contains('@')) errors.Add("A valid owner email is required.");
        if (errors.Count > 0) return new(false, null, errors);

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return new(false, null, ["That email address is already registered."]);
        }

        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == r.PlanId);
        if (plan is null) return new(false, null, ["Selected plan does not exist."]);

        var baseCode = string.IsNullOrWhiteSpace(r.Code) ? Slugify(name) : Slugify(r.Code);
        var code = baseCode;
        for (var i = 2; await db.Tenants.AnyAsync(t => t.Code == code); i++)
        {
            if (!string.IsNullOrWhiteSpace(r.Code)) return new(false, null, ["That business code is already taken."]);
            code = $"{baseCode}-{i}";
        }

        var now = time.GetUtcNow().UtcDateTime;
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            var tenant = new Tenant
            {
                Name = name, Code = code, Status = TenantStatus.Active, ContactName = r.OwnerName.Trim(), ContactEmail = email,
                BillingEmail = email, ContactPhone = r.ContactPhone, Address = r.Address, City = r.City, TaxId = r.TaxId,
                CreatedAt = now, CreatedBy = actor ?? audit.Actor
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            using (tenantContext.UseTenant(tenant.Id))
            {
                db.CompanySettings.Add(new CompanySetting { CompanyName = name, ShortName = name.Length > 50 ? name[..50] : name, Address = r.Address, Phone = r.ContactPhone });
                db.Warehouses.Add(new Warehouse { Code = "WH-01", Name = "Main Warehouse", Location = r.City });
                await db.SaveChangesAsync();
                await DbInitializer.SeedChartOfAccountsAsync(db, null!);
            }

            var owner = new ApplicationUser
            {
                UserName = email, Email = email, FullName = r.OwnerName.Trim(), EmailConfirmed = true, IsActive = true, TenantId = tenant.Id
            };
            var created = await userManager.CreateAsync(owner, r.OwnerPassword);
            if (!created.Succeeded)
            {
                if (tx is not null) await tx.RollbackAsync();
                db.ChangeTracker.Clear();
                return new(false, null, created.Errors.Select(e => e.Description).ToList());
            }
            await userManager.AddToRoleAsync(owner, Roles.Admin);
            tenant.OwnerUserId = owner.Id;
            await db.SaveChangesAsync();

            await subscriptions.StartAsync(tenant.Id, plan.Id, r.StartMode, actor);

            audit.Add("tenant.created", nameof(Tenant), tenant.Id.ToString(), tenant.Id, details: $"{tenant.Name} ({tenant.Code}), plan {plan.Code}, {r.StartMode}");
            await notifier.QueueAsync(tenant.Id, "tenant-registered", "Welcome",
                $"Your business {tenant.Name} is set up. Sign in with {email}.", $"tenant-registered:{tenant.Id}");
            await db.SaveChangesAsync();

            if (tx is not null) await tx.CommitAsync();
            return new(true, tenant, []);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
