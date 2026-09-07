using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;

namespace WebApplication1.Controllers;

[Authorize(Roles = Roles.Admin)]
public class PermissionsController(ApplicationDbContext db) : Controller
{
    // Admin is excluded: it implicitly has every permission and is never shown as grantable.
    private static readonly string[] GrantableRoles = Roles.All.Where(r => r != Roles.Admin).ToArray();

    public async Task<IActionResult> Index()
    {
        ViewData["Roles"] = GrantableRoles;
        ViewData["Permissions"] = Models.Identity.Permissions.All;

        var granted = await db.RolePermissions
            .Select(p => p.RoleName + "|" + p.PermissionKey)
            .ToListAsync();

        return View(granted.ToHashSet());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(List<string>? grants)
    {
        var validKeys = Models.Identity.Permissions.All.Select(p => p.Key).ToHashSet();
        var current = await db.RolePermissions.ToListAsync();
        db.RolePermissions.RemoveRange(current);

        foreach (var grant in (grants ?? []).Distinct())
        {
            var parts = grant.Split('|', 2);
            if (parts.Length != 2 || !GrantableRoles.Contains(parts[0]) || !validKeys.Contains(parts[1]))
            {
                continue;
            }

            db.RolePermissions.Add(new RolePermission { RoleName = parts[0], PermissionKey = parts[1] });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "Permissions updated.";
        return RedirectToAction(nameof(Index));
    }
}
