using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

public class CategoriesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search)
    {
        var query = db.Categories.AsQueryable().Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Name.Contains(search));
        }
        ViewData["Search"] = search;
        return View(await query.Include(c => c.Products).OrderBy(c => c.Name).ToListAsync());
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public IActionResult Create() => View(new Category());

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.CreatedBy = User.Identity?.Name;
        db.Categories.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "Category created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null) return NotFound();
        return View(category);
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Category model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var category = await db.Categories.FindAsync(id);
        if (category is null) return NotFound();

        category.Name = model.Name;
        category.Description = model.Description;
        category.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Category updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await db.Categories.Include(c => c.Products).FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return NotFound();

        if (category.Products.Count != 0)
        {
            TempData["Error"] = "Cannot delete a category that still has products assigned to it.";
            return RedirectToAction(nameof(Index));
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        TempData["Success"] = "Category deleted.";
        return RedirectToAction(nameof(Index));
    }
}
