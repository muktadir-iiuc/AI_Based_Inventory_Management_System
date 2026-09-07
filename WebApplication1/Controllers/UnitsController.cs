using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Controllers;

public class UnitsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await db.UnitOfMeasures.OrderBy(u => u.Name).ToListAsync());
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public IActionResult Create() => View(new UnitOfMeasure());

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UnitOfMeasure model)
    {
        if (!ModelState.IsValid) return View(model);

        model.CreatedBy = User.Identity?.Name;
        db.UnitOfMeasures.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "Unit created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var unit = await db.UnitOfMeasures.FindAsync(id);
        if (unit is null) return NotFound();
        return View(unit);
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UnitOfMeasure model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var unit = await db.UnitOfMeasures.FindAsync(id);
        if (unit is null) return NotFound();

        unit.Name = model.Name;
        unit.Symbol = model.Symbol;
        unit.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Unit updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var unit = await db.UnitOfMeasures.Include(u => u.Products).FirstOrDefaultAsync(u => u.Id == id);
        if (unit is null) return NotFound();

        if (unit.Products.Count != 0)
        {
            TempData["Error"] = "Cannot delete a unit that still has products assigned to it.";
            return RedirectToAction(nameof(Index));
        }

        db.UnitOfMeasures.Remove(unit);
        await db.SaveChangesAsync();
        TempData["Success"] = "Unit deleted.";
        return RedirectToAction(nameof(Index));
    }
}
