using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Services.Tenancy;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

// Which user sees which page (Stock Transfers, Stock Adjustments, ... — see Permissions.All).
// One row per user, one checkbox per page. Admin and Manager are not listed: they always see
// every page. The same grants can also be ticked under "Menu Access" on a user's Edit page.
[Authorize(Roles = Roles.AdminManagers)]
public class PermissionsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ITenantContext tenant) : Controller
{
    public async Task<IActionResult> Index()
    {
        var users = await GrantableUsersAsync();
        var userIds = users.Select(u => u.Id).ToList();
        var granted = await db.UserPermissions
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => p.UserId + "|" + p.PermissionKey)
            .ToListAsync();

        return View(new PagePermissionsViewModel
        {
            Users = users,
            Permissions = Permissions.All,
            Granted = granted.ToHashSet()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(List<string>? grants)
    {
        // Only the users shown on the page are touched; anything posted for another user
        // (Admin/Manager, inactive, unknown) or an unknown page key is ignored.
        var users = (await GrantableUsersAsync()).ToDictionary(u => u.Id);
        var userIds = users.Keys.ToHashSet();
        var validKeys = Permissions.All.Select(p => p.Key).ToHashSet();

        // A page the user's roles can't have (Purchase Invoices for a Sales Officer/Viewer) is dropped.
        var desired = (grants ?? [])
            .Select(g => g.Split('|', 2))
            .Where(parts => parts.Length == 2 && userIds.Contains(parts[0]) && validKeys.Contains(parts[1])
                && Permissions.IsGrantable(parts[1], users[parts[0]].Roles))
            .Select(parts => (UserId: parts[0], Key: parts[1]))
            .ToHashSet();

        var current = await db.UserPermissions.Where(p => userIds.Contains(p.UserId)).ToListAsync();
        db.UserPermissions.RemoveRange(current.Where(p => !desired.Contains((p.UserId, p.PermissionKey))));
        db.UserPermissions.AddRange(desired
            .Where(d => !current.Any(p => p.UserId == d.UserId && p.PermissionKey == d.Key))
            .Select(d => new UserPermission { UserId = d.UserId, PermissionKey = d.Key }));

        await db.SaveChangesAsync();
        TempData["Success"] = "Page access updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<PagePermissionUserRow>> GrantableUsersAsync()
    {
        var users = await userManager.TenantUsers(tenant)
            .Where(u => u.IsActive)
            .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        var rows = new List<PagePermissionUserRow>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (roles.Contains(Roles.Admin) || roles.Contains(Roles.Manager))
            {
                continue;
            }

            rows.Add(new PagePermissionUserRow
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Roles = roles.OrderBy(r => r).ToList(),
                Warehouses = user.UserWarehouses.Select(uw => uw.Warehouse!.Name).OrderBy(n => n).ToList()
            });
        }

        return rows;
    }
}
