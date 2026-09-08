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

    public async Task<IActionResult> Index(int page = 1)
    {
        var pagedUsers = await PagedList<ApplicationUser>.CreateAsync(
            userManager.Users.Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse).OrderBy(u => u.Email), page);

        var items = new List<UserListItem>();
        foreach (var user in pagedUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            items.Add(new UserListItem
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                IsActive = user.IsActive,
                Roles = roles.ToList(),
                WarehouseNames = user.UserWarehouses.Select(uw => uw.Warehouse!.Name).OrderBy(n => n).ToList()
            });
        }

        return View(new PagedList<UserListItem>(items, pagedUsers.Paging.TotalCount, pagedUsers.Paging.PageNumber, pagedUsers.Paging.PageSize));
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
        model.WarehouseIds = model.WarehouseIds.Distinct().ToList();
        ValidateRolesAndWarehouses(model.Roles, model.WarehouseIds);

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
            IsActive = true
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

        if (!IsUnrestricted(model.Roles))
        {
            db.UserWarehouses.AddRange(model.WarehouseIds.Select(id => new UserWarehouse { UserId = user.Id, WarehouseId = id }));
            await db.SaveChangesAsync();
        }

        TempData["Success"] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var user = await userManager.Users.Include(u => u.UserWarehouses).FirstOrDefaultAsync(u => u.Id == id);
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
            WarehouseIds = user.UserWarehouses.Select(uw => uw.WarehouseId).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserEditViewModel model)
    {
        if (id != model.Id) return NotFound();

        model.Roles = model.Roles.Distinct().ToList();
        model.WarehouseIds = model.WarehouseIds.Distinct().ToList();
        ValidateRolesAndWarehouses(model.Roles, model.WarehouseIds);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var user = await userManager.Users.Include(u => u.UserWarehouses).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.FullName = model.FullName;
        user.IsActive = model.IsActive;
        user.Email = model.Email;
        user.UserName = model.Email;
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

        List<int> desiredWarehouseIds = IsUnrestricted(model.Roles) ? [] : model.WarehouseIds;
        var currentWarehouseIds = user.UserWarehouses.Select(uw => uw.WarehouseId).ToList();
        var warehousesToRemove = user.UserWarehouses.Where(uw => !desiredWarehouseIds.Contains(uw.WarehouseId)).ToList();
        var warehousesToAdd = desiredWarehouseIds.Except(currentWarehouseIds)
            .Select(wid => new UserWarehouse { UserId = user.Id, WarehouseId = wid });

        db.UserWarehouses.RemoveRange(warehousesToRemove);
        db.UserWarehouses.AddRange(warehousesToAdd);
        await db.SaveChangesAsync();

        TempData["Success"] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsUnrestricted(List<string> roles) => roles.Any(UnrestrictedRoles.Contains);

    private void ValidateRolesAndWarehouses(List<string> roles, List<int> warehouseIds)
    {
        if (roles.Count == 0)
        {
            ModelState.AddModelError(nameof(UserCreateViewModel.Roles), "Select at least one role.");
            return;
        }

        if (!IsUnrestricted(roles) && warehouseIds.Count == 0)
        {
            ModelState.AddModelError(nameof(UserCreateViewModel.WarehouseIds), "This role combination must be assigned to at least one warehouse.");
        }
    }

    private async Task PopulateDropdownsAsync()
    {
        ViewData["Roles"] = new SelectList(roleManager.Roles.OrderBy(r => r.Name).ToList(), "Name", "Name");
        ViewData["Warehouses"] = new SelectList(await db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync(), "Id", "Name");
        ViewData["UnrestrictedRoles"] = UnrestrictedRoles;
    }
}
