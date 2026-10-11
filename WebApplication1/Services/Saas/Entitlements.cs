using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services.Saas;

public enum LimitKind { Users, Products, Warehouses }

public record UsageSnapshot(int Users, int Products, int Warehouses);

public interface IEntitlementService
{
    Task<bool> HasFeatureAsync(string featureKey);
    /// <summary>True when one more of this kind fits within the tenant's plan limits.</summary>
    Task<bool> CanAddAsync(LimitKind kind);
    Task<UsageSnapshot> GetUsageAsync();
    /// <summary>Same as <see cref="CanAddAsync"/> for a named tenant (platform staff adding a user on a tenant's behalf).</summary>
    Task<bool> CanAddForTenantAsync(int tenantId, LimitKind kind);
}

public class EntitlementService(ApplicationDbContext db, ITenantContext tenant, ISubscriptionService subscriptions) : IEntitlementService
{
    public async Task<bool> HasFeatureAsync(string featureKey) =>
        tenant.TenantId > 0 && (await subscriptions.GetFeaturesAsync(tenant.TenantId)).Contains(featureKey);

    public async Task<UsageSnapshot> GetUsageAsync()
    {
        var tenantId = tenant.TenantId;
        return new UsageSnapshot(
            await db.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive),
            await db.Products.CountAsync(),     // tenant-filtered
            await db.Warehouses.CountAsync());  // tenant-filtered
    }

    public async Task<bool> CanAddForTenantAsync(int tenantId, LimitKind kind)
    {
        using var _ = tenant.UseTenant(tenantId);
        return await CanAddAsync(kind);
    }

    public async Task<bool> CanAddAsync(LimitKind kind)
    {
        if (tenant.TenantId == 0) return false;
        var limits = await subscriptions.GetLimitsAsync(tenant.TenantId);
        var usage = await GetUsageAsync();
        return kind switch
        {
            LimitKind.Users => limits.MaxUsers is null || usage.Users < limits.MaxUsers,
            LimitKind.Products => limits.MaxProducts is null || usage.Products < limits.MaxProducts,
            LimitKind.Warehouses => limits.MaxWarehouses is null || usage.Warehouses < limits.MaxWarehouses,
            _ => true
        };
    }
}

/// <summary>Server-side gate: the controller/action is only reachable when the tenant's plan includes the feature.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireFeatureAttribute : TypeFilterAttribute
{
    public RequireFeatureAttribute(string featureKey) : base(typeof(RequireFeatureFilter))
    {
        FeatureKey = featureKey;
        Arguments = [featureKey]; // passed to RequireFeatureFilter's constructor
    }

    public string FeatureKey { get; }
}

public class RequireFeatureFilter(string featureKey, IEntitlementService entitlements) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (await entitlements.HasFeatureAsync(featureKey))
        {
            await next();
            return;
        }

        context.Result = LimitResults.Denied(context, "feature", featureKey);
    }
}

/// <summary>Blocks the POST that would exceed a plan limit (users, products, warehouses).</summary>
[AttributeUsage(AttributeTargets.Method)]
public class EnforceLimitAttribute : TypeFilterAttribute
{
    public EnforceLimitAttribute(LimitKind kind) : base(typeof(EnforceLimitFilter))
    {
        Kind = kind;
        Arguments = [kind];
    }

    public LimitKind Kind { get; }
}

public class EnforceLimitFilter(LimitKind kind, IEntitlementService entitlements) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (await entitlements.CanAddAsync(kind))
        {
            await next();
            return;
        }

        context.Result = LimitResults.Denied(context, "limit", kind.ToString());
    }
}

internal static class LimitResults
{
    public static IActionResult Denied(ActionExecutingContext context, string reason, string what)
    {
        var request = context.HttpContext.Request;
        var message = reason == "feature" ? "This feature is not included in your plan." : $"Your plan's {what} limit has been reached. Upgrade your plan to add more.";

        // Existing JSON endpoints (e.g. quick-add popups) read { ok, error }.
        if (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new JsonResult(new { ok = false, error = message });
        }
        if (request.Headers.Accept.ToString().Contains("json", StringComparison.OrdinalIgnoreCase)
            || request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return new ObjectResult(new { error = message }) { StatusCode = StatusCodes.Status403Forbidden };
        }
        return new RedirectToActionResult(reason == "feature" ? "FeatureLocked" : "LimitReached", "Billing", new { what });
    }
}
