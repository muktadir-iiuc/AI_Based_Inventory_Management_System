using Microsoft.AspNetCore.Identity;
using WebApplication1.Models.Identity;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Extensions;

public static class TenantUserExtensions
{
    /// <summary>
    /// Users of the current tenant only. ApplicationUser has no global query filter (sign-in and
    /// cookie validation look users up before a tenant is known), so every user list or lookup
    /// driven by tenant staff must go through this.
    /// </summary>
    public static IQueryable<ApplicationUser> TenantUsers(this UserManager<ApplicationUser> userManager, ITenantContext tenant)
    {
        var tenantId = tenant.TenantId;
        return userManager.Users.Where(u => u.TenantId == tenantId);
    }

    public static IQueryable<ApplicationUser> TenantUsers(this IQueryable<ApplicationUser> users, ITenantContext tenant)
    {
        var tenantId = tenant.TenantId;
        return users.Where(u => u.TenantId == tenantId);
    }
}
