using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.ViewModels;

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
        ModelState.Remove(nameof(Product.Sku)); // auto-generated below, not user input

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        model.Sku = await GenerateSkuAsync(model.CategoryId);
        model.CreatedBy = User.Identity?.Name;
        db.Products.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Product created with SKU {model.Sku}.";
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
        ModelState.Remove(nameof(Product.Sku)); // SKU is fixed at creation and not editable

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();

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

    // Generates SKUs like "ELE-001": a 3-letter category prefix plus a per-prefix
    // running sequence, following the same shape as the seeded demo catalog.
    private async Task<string> GenerateSkuAsync(int categoryId)
    {
        var categoryName = await db.Categories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync();
        var prefix = BuildSkuPrefix(categoryName);

        var existingSuffixes = await db.Products
            .Where(p => p.Sku.StartsWith(prefix + "-"))
            .Select(p => p.Sku)
            .ToListAsync();

        var nextNumber = existingSuffixes
            .Select(sku => sku[(prefix.Length + 1)..])
            .Where(suffix => suffix.Length > 0 && suffix.All(char.IsDigit))
            .Select(int.Parse)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{prefix}-{nextNumber:D3}";
    }

    private static string BuildSkuPrefix(string? categoryName)
    {
        var letters = new string((categoryName ?? string.Empty).Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return letters.Length >= 3 ? letters[..3] : letters.PadRight(3, 'X');
    }
}
