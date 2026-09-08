using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

public class WarehousesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(int page = 1)
    {
        return View(await PagedList<Warehouse>.CreateAsync(db.Warehouses.OrderBy(w => w.Name), page));
    }

    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public IActionResult Create() => View(new Warehouse());

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Warehouse model)
    {
        if (await db.Warehouses.AnyAsync(w => w.Code == model.Code))
        {
            ModelState.AddModelError(nameof(Warehouse.Code), "This warehouse code is already in use.");
        }
        if (!ModelState.IsValid) return View(model);

        model.CreatedBy = User.Identity?.Name;
        db.Warehouses.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "Warehouse created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Edit(int id)
    {
        var warehouse = await db.Warehouses.FindAsync(id);
        if (warehouse is null) return NotFound();
        return View(warehouse);
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Warehouse model)
    {
        if (id != model.Id) return NotFound();

        if (await db.Warehouses.AnyAsync(w => w.Code == model.Code && w.Id != id))
        {
            ModelState.AddModelError(nameof(Warehouse.Code), "This warehouse code is already in use.");
        }
        if (!ModelState.IsValid) return View(model);

        var warehouse = await db.Warehouses.FindAsync(id);
        if (warehouse is null) return NotFound();

        warehouse.Code = model.Code;
        warehouse.Name = model.Name;
        warehouse.Location = model.Location;
        warehouse.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Warehouse updated.";
        return RedirectToAction(nameof(Index));
    }
}
