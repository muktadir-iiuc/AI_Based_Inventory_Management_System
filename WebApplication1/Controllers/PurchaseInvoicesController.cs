using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class PurchaseInvoicesController(
    ApplicationDbContext db,
    IStockService stockService,
    IAccountingService accountingService,
    IActivityNotifier notifier) : Controller
{
    public async Task<IActionResult> Index(int? supplierId, int? warehouseId, int page = 1)
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Warehouse).AsQueryable();

        if (warehouseIds is not null)
        {
            query = query.Where(p => warehouseIds.Contains(p.WarehouseId));
        }
        else if (warehouseId.HasValue)
        {
            query = query.Where(p => p.WarehouseId == warehouseId);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(p => p.SupplierId == supplierId);
        }

        ViewData["SupplierId"] = new SelectList(await db.Suppliers.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", supplierId);
        ViewData["WarehouseId"] = new SelectList(await db.Warehouses.OrderBy(w => w.Name).ToListAsync(), "Id", "Name", warehouseId);
        ViewData["WarehouseScoped"] = warehouseIds is not null;

        return View(await PagedList<PurchaseInvoice>.CreateAsync(
            query.Include(p => p.Items).OrderByDescending(p => p.Date).ThenByDescending(p => p.Id), page));
    }

    public async Task<IActionResult> Details(int id)
    {
        var invoice = await db.PurchaseInvoices
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .Include(p => p.Payments)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();
        return View(invoice);
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        var warehouseIds = User.GetWarehouseIds();
        return View(new PurchaseInvoiceCreateViewModel
        {
            Items = [new PurchaseLineInput()],
            WarehouseId = warehouseIds is { Count: 1 } ids ? ids[0] : 0
        });
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseInvoiceCreateViewModel model)
    {
        model.Items = model.Items.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList();
        if (model.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one product line.");
        }

        if (model.WarehouseId <= 0 || !await db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId))
        {
            ModelState.AddModelError(nameof(model.WarehouseId), "Please select a warehouse.");
        }
        else if (!User.IsWarehouseAllowed(model.WarehouseId))
        {
            ModelState.AddModelError(nameof(model.WarehouseId), "You are not assigned to this warehouse.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var invoiceCount = await db.PurchaseInvoices.CountAsync();
        var invoice = new PurchaseInvoice
        {
            InvoiceNumber = $"PINV-{invoiceCount + 1:D6}",
            SupplierId = model.SupplierId,
            WarehouseId = model.WarehouseId,
            Date = model.Date,
            Notes = model.Notes,
            Status = DocumentStatus.Posted,
            CreatedBy = User.Identity?.Name
        };

        foreach (var item in model.Items)
        {
            invoice.Items.Add(new PurchaseInvoiceItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                SalePrice = item.SalePrice
            });
        }

        db.PurchaseInvoices.Add(invoice);

        // Each purchase line becomes its own batch — the authoritative source of this stock's
        // cost/sale price going forward. Product.CostPrice/SalePrice are left alone; they're
        // only ever a suggested default when creating the *next* purchase line.
        var batchCount = await db.ProductBatches.CountAsync();
        foreach (var item in invoice.Items)
        {
            batchCount++;
            var batch = new ProductBatch
            {
                ProductId = item.ProductId,
                WarehouseId = invoice.WarehouseId,
                PurchaseInvoiceItem = item,
                BatchNumber = $"BATCH-{batchCount:D6}",
                PurchaseDate = invoice.Date,
                PurchasePrice = item.UnitPrice,
                SalePrice = item.SalePrice,
                OriginalQuantity = item.Quantity,
                RemainingQuantity = item.Quantity
            };
            db.ProductBatches.Add(batch);

            await stockService.ReceiveStockAsync(item.ProductId, invoice.WarehouseId, item.Quantity, invoice.InvoiceNumber,
                batch: batch, unitCost: item.UnitPrice, unitSalePrice: item.SalePrice);
        }

        await accountingService.PostPurchaseInvoiceAsync(invoice);
        await db.SaveChangesAsync();

        var warehouseName = (await db.Warehouses.FindAsync(invoice.WarehouseId))?.Name;
        await notifier.NotifyAsync(
            "Purchase Invoice Posted",
            $"{invoice.InvoiceNumber} — {invoice.Items.Count} line(s) at {warehouseName} ({invoice.TotalAmount:C})",
            "fas fa-truck-loading text-info",
            [invoice.WarehouseId]);

        TempData["Success"] = $"Purchase invoice {invoice.InvoiceNumber} posted.";
        return RedirectToAction(nameof(Details), new { id = invoice.Id });
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var invoice = await db.PurchaseInvoices
            .Include(p => p.Items).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();

        if (invoice.Status == DocumentStatus.Cancelled)
        {
            TempData["Error"] = "This invoice is already cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var soldBatch = invoice.Items.Select(i => i.Batch).FirstOrDefault(b => b is not null && b.RemainingQuantity < b.OriginalQuantity);
        if (soldBatch is not null)
        {
            TempData["Error"] = $"Cannot cancel: batch {soldBatch.BatchNumber} already has {soldBatch.SoldQuantity:0.##} unit(s) sold.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await stockService.ReverseTransactionsForReferenceAsync(invoice.InvoiceNumber);
        await accountingService.ReverseJournalEntriesForReferenceAsync(invoice.InvoiceNumber, "Purchase invoice cancelled");
        invoice.Status = DocumentStatus.Cancelled;

        foreach (var item in invoice.Items)
        {
            if (item.Batch is not null)
            {
                item.Batch.RemainingQuantity = 0;
                item.Batch.IsActive = false;
            }
        }

        await db.SaveChangesAsync();

        await notifier.NotifyAsync(
            "Purchase Invoice Cancelled",
            $"{invoice.InvoiceNumber} was cancelled and reversed.",
            "fas fa-ban text-danger",
            [invoice.WarehouseId]);

        TempData["Success"] = $"Purchase invoice {invoice.InvoiceNumber} cancelled and reversed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateDropdownsAsync()
    {
        var warehouseIds = User.GetWarehouseIds();
        var warehouses = db.Warehouses.Where(w => w.IsActive).AsQueryable();
        if (warehouseIds is not null)
        {
            warehouses = warehouses.Where(w => warehouseIds.Contains(w.Id));
        }

        var singleWarehouseId = warehouseIds is { Count: 1 } ids ? ids[0] : (int?)null;

        ViewData["Suppliers"] = new SelectList(await db.Suppliers.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
        ViewData["Warehouses"] = new SelectList(await warehouses.OrderBy(w => w.Name).ToListAsync(), "Id", "Name", singleWarehouseId);
        ViewData["WarehouseScoped"] = singleWarehouseId.HasValue;

        // Most recent batch's sale price per product, as a starting suggestion for the new
        // purchase line's Sale Price field — not authoritative, the user can change it per batch.
        var latestBatchSalePrices = await db.ProductBatches
            .Where(b => b.IsActive)
            .GroupBy(b => b.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                SalePrice = g.OrderByDescending(b => b.PurchaseDate).ThenByDescending(b => b.Id).Select(b => b.SalePrice).First()
            })
            .ToDictionaryAsync(x => x.ProductId, x => x.SalePrice);

        var products = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Sku, p.Name, p.CostPrice, p.SalePrice, p.UnitOfMeasure!.Symbol }).ToListAsync();

        ViewData["Products"] = products.Select(p => new
        {
            p.Id,
            p.Sku,
            p.Name,
            p.CostPrice,
            SuggestedSalePrice = latestBatchSalePrices.GetValueOrDefault(p.Id, p.SalePrice),
            p.Symbol
        }).ToList();
    }
}
