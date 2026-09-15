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
    public async Task<IActionResult> Index()
    {
        return View(await db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync());
    }

    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Create()
    {
        ViewData["NextCode"] = await GenerateWarehouseCodeAsync();
        return View(new Warehouse());
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Warehouse model)
    {
        ModelState.Remove(nameof(Warehouse.Code)); // auto-generated below, not user input

        if (!ModelState.IsValid)
        {
            ViewData["NextCode"] = await GenerateWarehouseCodeAsync();
            return View(model);
        }

        model.Code = await GenerateWarehouseCodeAsync();
        model.CreatedBy = User.Identity?.Name;
        db.Warehouses.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Warehouse created with code {model.Code}.";
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

    private async Task<string> GenerateWarehouseCodeAsync()
    {
        var existingCodes = await db.Warehouses.Select(w => w.Code).ToListAsync();

        var nextNumber = existingCodes
            .Where(c => c.StartsWith("WH-") && c[3..].All(char.IsDigit))
            .Select(c => int.Parse(c[3..]))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"WH-{nextNumber:D2}";
    }
}
