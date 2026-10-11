using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;

namespace WebApplication1.Authorization;

public class PermissionAuthorizationHandler(ApplicationDbContext db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.IsInRole(Roles.Admin) || context.User.IsInRole(Roles.Manager))
        {
            context.Succeed(requirement);
            return;
        }

        // Everyone else: only pages ticked for them individually (Administration -> Page Permissions,
        // or "Menu Access" on the user's Edit page). The old role-level RolePermissions table is no
        // longer consulted — the AddUserPermissions migration turned its rows into per-user grants.
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var granted = userId is not null && await db.UserPermissions
            .AnyAsync(p => p.PermissionKey == requirement.PermissionKey && p.UserId == userId);

        if (granted)
        {
            context.Succeed(requirement);
        }
    }
}
