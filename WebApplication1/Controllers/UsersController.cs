using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

[Authorize(Roles = Roles.Admin)]
public class UsersController(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ApplicationDbContext db) : Controller
{
    private static readonly string[] UnrestrictedRoles = [Roles.Admin, Roles.Manager];

    public async Task<IActionResult> Index()
    {
        var users = userManager.Users.Include(u => u.Warehouse).OrderBy(u => u.Email).ToList();
        var items = new List<UserListItem>();

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            items.Add(new UserListItem
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                IsActive = user.IsActive,
                Roles = roles.ToList(),
                WarehouseName = user.Warehouse?.Name
            });
        }

        return View(items);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        return View(new UserCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        model.Roles = model.Roles.Distinct().ToList();
        ValidateRolesAndWarehouse(model.Roles, model.WarehouseId);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            EmailConfirmed = true,
            IsActive = true,
            WarehouseId = IsUnrestricted(model.Roles) ? null : model.WarehouseId
        };

        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            await PopulateDropdownsAsync();
            return View(model);
        }

        await userManager.AddToRolesAsync(user, model.Roles);
        TempData["Success"] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var roles = await userManager.GetRolesAsync(user);
        await PopulateDropdownsAsync();
        return View(new UserEditViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            IsActive = user.IsActive,
            Roles = roles.ToList(),
            WarehouseId = user.WarehouseId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserEditViewModel model)
    {
        if (id != model.Id) return NotFound();

        model.Roles = model.Roles.Distinct().ToList();
        ValidateRolesAndWarehouse(model.Roles, model.WarehouseId);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.FullName = model.FullName;
        user.IsActive = model.IsActive;
        user.Email = model.Email;
        user.UserName = model.Email;
        user.WarehouseId = IsUnrestricted(model.Roles) ? null : model.WarehouseId;
        await userManager.UpdateAsync(user);

        var currentRoles = await userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Except(model.Roles).ToList();
        var toAdd = model.Roles.Except(currentRoles).ToList();

        if (toRemove.Count != 0)
        {
            await userManager.RemoveFromRolesAsync(user, toRemove);
        }
        if (toAdd.Count != 0)
        {
            await userManager.AddToRolesAsync(user, toAdd);
        }

        TempData["Success"] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsUnrestricted(List<string> roles) => roles.Any(UnrestrictedRoles.Contains);

    private void ValidateRolesAndWarehouse(List<string> roles, int? warehouseId)
    {
        if (roles.Count == 0)
        {
            ModelState.AddModelError(nameof(UserCreateViewModel.Roles), "Select at least one role.");
            return;
        }

        if (!IsUnrestricted(roles) && warehouseId is null)
        {
            ModelState.AddModelError(nameof(UserCreateViewModel.WarehouseId), "This role combination must be assigned to a warehouse.");
        }
    }

    private async Task PopulateDropdownsAsync()
    {
        ViewData["Roles"] = new SelectList(roleManager.Roles.OrderBy(r => r.Name).ToList(), "Name", "Name");
        ViewData["Warehouses"] = new SelectList(await db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync(), "Id", "Name");
        ViewData["UnrestrictedRoles"] = UnrestrictedRoles;
    }
}
