using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Controllers;

public class ProductsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search, int? categoryId)
    {
        var query = db.Products.Include(p => p.Category).Include(p => p.UnitOfMeasure).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
        }
        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        ViewData["Search"] = search;
        ViewData["CategoryId"] = new SelectList(await db.Categories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", categoryId);

        return View(await query.OrderBy(p => p.Name).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await db.Products
            .Include(p => p.Category)
            .Include(p => p.UnitOfMeasure)
            .Include(p => p.StockTransactions.OrderByDescending(t => t.Date).Take(20)).ThenInclude(t => t.Warehouse)
            .Include(p => p.WarehouseStocks.Where(s => s.Quantity != 0)).ThenInclude(s => s.Warehouse)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null) return NotFound();
        return View(product);
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        return View(new Product());
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product model)
    {
        if (await db.Products.AnyAsync(p => p.Sku == model.Sku))
        {
            ModelState.AddModelError(nameof(Product.Sku), "This SKU is already in use.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        model.CreatedBy = User.Identity?.Name;
        db.Products.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "Product created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();
        await PopulateDropdownsAsync();
        return View(product);
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product model)
    {
        if (id != model.Id) return NotFound();

        if (await db.Products.AnyAsync(p => p.Sku == model.Sku && p.Id != id))
        {
            ModelState.AddModelError(nameof(Product.Sku), "This SKU is already in use.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();

        product.Sku = model.Sku;
        product.Name = model.Name;
        product.Description = model.Description;
        product.CategoryId = model.CategoryId;
        product.UnitOfMeasureId = model.UnitOfMeasureId;
        product.CostPrice = model.CostPrice;
        product.SalePrice = model.SalePrice;
        product.ReorderLevel = model.ReorderLevel;
        product.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Product updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();

        var hasMovement = await db.StockTransactions.AnyAsync(t => t.ProductId == id);
        if (hasMovement)
        {
            TempData["Error"] = "This product has stock movement history and cannot be deleted. Mark it inactive instead.";
            return RedirectToAction(nameof(Index));
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync();
        TempData["Success"] = "Product deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdownsAsync()
    {
        ViewData["Categories"] = new SelectList(await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewData["Units"] = new SelectList(await db.UnitOfMeasures.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync(), "Id", "Name");
    }
}
