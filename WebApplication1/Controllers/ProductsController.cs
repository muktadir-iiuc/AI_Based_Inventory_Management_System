using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class ProductsController(
    ApplicationDbContext db,
    IProductPriceService priceService,
    IActivityNotifier notifier,
    UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index(string? search, int? categoryId)
    {
        var query = db.Products.Include(p => p.Category).Include(p => p.UnitOfMeasure).AsQueryable().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
        }
        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        ViewData["Search"] = search;
        ViewData["CategoryId"] = new SelectList(await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", categoryId);

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
        ViewData["LastPriceChange"] = await priceService.GetLatestPriceChangeAsync(id);
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

        var userId = userManager.GetUserId(User)!;
        var priceChange = priceService.ApplySalePriceChange(product, model.SalePrice, userId);

        product.Name = model.Name;
        product.Description = model.Description;
        product.CategoryId = model.CategoryId;
        product.UnitOfMeasureId = model.UnitOfMeasureId;
        product.CostPrice = model.CostPrice;
        product.ReorderLevel = model.ReorderLevel;
        product.IsActive = model.IsActive;
        await db.SaveChangesAsync();

        if (priceChange is not null)
        {
            var changedByName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Unknown";
            var icon = priceChange.PriceIncreased ? "fas fa-arrow-trend-up text-success" : "fas fa-arrow-trend-down text-danger";
            await notifier.NotifyAsync("Sale Price Updated", BuildPriceChangeToastMessage(product, priceChange, changedByName), icon, []);
            await priceService.NotifyManagersOfPriceChangeAsync(product, priceChange, changedByName);
        }

        TempData["Success"] = "Product updated.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> PendingPriceUpdates()
    {
        var pending = await priceService.GetPendingMonthlyReviewQuery()
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        ViewData["MonthLabel"] = DateTime.UtcNow.ToString("MMMM yyyy");
        return View(pending);
    }

    public async Task<IActionResult> PriceHistory(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();

        ViewData["Product"] = product;
        return View(await priceService.GetPriceHistoryAsync(id));
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

    // Rendered as-is inside the live toast and the notification bell dropdown (both trust
    // server-built HTML for their `message` field — see _Layout.cshtml's handleNotification).
    // Product/user text is HTML-encoded since Name/Sku/FullName are free-text fields; colors use
    // opacity/semantic classes rather than fixed shades so it reads correctly on the toast's dark
    // background and the dropdown's light one from the same markup.
    private static string BuildPriceChangeToastMessage(Product product, ProductPriceHistory change, string changedByName)
    {
        var badgeClass = change.PriceIncreased ? "text-bg-success" : "text-bg-danger";
        var priceColorClass = change.PriceIncreased ? "text-success" : "text-danger";
        var badgeText = change.PercentChange is { } percent
            ? $"{(change.PriceIncreased ? "▲" : "▼")} {percent:0.##}%"
            : change.PriceIncreased ? "Increased" : "Decreased";

        var name = WebUtility.HtmlEncode(product.Name);
        var sku = WebUtility.HtmlEncode(product.Sku);
        var changedBy = WebUtility.HtmlEncode(changedByName);

        return $"""
            <div class="d-flex align-items-center gap-2 mb-1">
                <span class="fw-semibold">{name}</span>
                <span class="opacity-75 small">({sku})</span>
                <span class="badge {badgeClass} ms-auto">{badgeText}</span>
            </div>
            <div>
                <span class="text-decoration-line-through opacity-75">{change.OldPrice:C}</span>
                <i class="fas fa-arrow-right-long mx-1 opacity-75"></i>
                <span class="fw-bold {priceColorClass}">{change.NewPrice:C}</span>
            </div>
            <div class="opacity-75 small mt-1">by {changedBy}</div>
            <a href="/Products/Details/{product.Id}" class="small fw-semibold text-decoration-underline d-inline-block mt-1" style="color:inherit;">
                View product <i class="fas fa-arrow-up-right-from-square ms-1"></i>
            </a>
            """;
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
