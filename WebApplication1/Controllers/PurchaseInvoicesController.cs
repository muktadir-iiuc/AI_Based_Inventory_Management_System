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
    public async Task<IActionResult> Index(int? supplierId, int? warehouseId)
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

        return View(await query.Include(p => p.Items.Where(i => i.IsCurrent)).OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var invoice = await db.PurchaseInvoices
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Product)
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
            .Include(p => p.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();

        if (invoice.Status == DocumentStatus.Cancelled)
        {
            TempData["Error"] = "This invoice is already cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var soldBatch = FindSoldBatch(invoice);
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

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var invoice = await db.PurchaseInvoices
            .Include(p => p.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();

        var blockedReason = EditBlockedReason(invoice);
        if (blockedReason is not null)
        {
            TempData["Error"] = blockedReason;
            return RedirectToAction(nameof(Details), new { id });
        }

        await PopulateDropdownsAsync();
        var model = new PurchaseInvoiceCreateViewModel
        {
            SupplierId = invoice.SupplierId,
            WarehouseId = invoice.WarehouseId,
            Date = invoice.Date,
            Notes = invoice.Notes,
            Items = invoice.Items.Select(i => new PurchaseLineInput
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                SalePrice = i.SalePrice
            }).ToList()
        };
        ViewData["InvoiceNumber"] = invoice.InvoiceNumber;
        return View(model);
    }

    // Editing keeps the same invoice number and row rather than cancel-and-recreate: the old
    // lines' stock/accounting effect is reversed exactly like Cancel, the old lines and their
    // batches are deactivated in place (never deleted — ProductBatch and StockTransaction both
    // hold Restrict FKs back to them, and it preserves the audit trail), then the new lines are
    // posted fresh, same as Create. Both StockService and AccountingService's reversal helpers
    // track which rows they've already reversed (IsReversed), so this can safely happen more
    // than once on the same invoice number without ever double-reversing history.
    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PurchaseInvoiceCreateViewModel model)
    {
        var invoice = await db.PurchaseInvoices
            .Include(p => p.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();

        var blockedReason = EditBlockedReason(invoice);
        if (blockedReason is not null)
        {
            TempData["Error"] = blockedReason;
            return RedirectToAction(nameof(Details), new { id });
        }

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
            ViewData["InvoiceNumber"] = invoice.InvoiceNumber;
            return View(model);
        }

        foreach (var item in invoice.Items)
        {
            if (item.Batch is not null)
            {
                item.Batch.RemainingQuantity = 0;
                item.Batch.IsActive = false;
            }
            item.IsCurrent = false;
        }

        await stockService.ReverseTransactionsForReferenceAsync(invoice.InvoiceNumber);
        await accountingService.ReverseJournalEntriesForReferenceAsync(invoice.InvoiceNumber, "Purchase invoice edited");

        invoice.SupplierId = model.SupplierId;
        invoice.WarehouseId = model.WarehouseId;
        invoice.Date = model.Date;
        invoice.Notes = model.Notes;

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

        var batchCount = await db.ProductBatches.CountAsync();
        foreach (var newItem in invoice.Items.Where(i => i.IsCurrent))
        {
            batchCount++;
            var batch = new ProductBatch
            {
                ProductId = newItem.ProductId,
                WarehouseId = invoice.WarehouseId,
                PurchaseInvoiceItem = newItem,
                BatchNumber = $"BATCH-{batchCount:D6}",
                PurchaseDate = invoice.Date,
                PurchasePrice = newItem.UnitPrice,
                SalePrice = newItem.SalePrice,
                OriginalQuantity = newItem.Quantity,
                RemainingQuantity = newItem.Quantity
            };
            db.ProductBatches.Add(batch);

            await stockService.ReceiveStockAsync(newItem.ProductId, invoice.WarehouseId, newItem.Quantity, invoice.InvoiceNumber,
                batch: batch, unitCost: newItem.UnitPrice, unitSalePrice: newItem.SalePrice);
        }

        await accountingService.PostPurchaseInvoiceAsync(invoice);
        await db.SaveChangesAsync();

        var warehouseName = (await db.Warehouses.FindAsync(invoice.WarehouseId))?.Name;
        await notifier.NotifyAsync(
            "Purchase Invoice Edited",
            $"{invoice.InvoiceNumber} was edited — {invoice.Items.Count(i => i.IsCurrent)} line(s) at {warehouseName} ({invoice.TotalAmount:C})",
            "fas fa-pen text-info",
            [invoice.WarehouseId]);

        TempData["Success"] = $"Purchase invoice {invoice.InvoiceNumber} updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static ProductBatch? FindSoldBatch(PurchaseInvoice invoice) =>
        invoice.Items.Select(i => i.Batch).FirstOrDefault(b => b is not null && b.RemainingQuantity < b.OriginalQuantity);

    // Null when editing is allowed; otherwise the reason to show the user. Shared by both the
    // Edit GET (so a blocked invoice never even reaches the form) and POST (re-checked in case
    // something changed between GET and submit).
    private static string? EditBlockedReason(PurchaseInvoice invoice)
    {
        if (invoice.Status != DocumentStatus.Posted)
        {
            return "Only a posted invoice can be edited.";
        }

        var soldBatch = FindSoldBatch(invoice);
        if (soldBatch is not null)
        {
            return $"Cannot edit: batch {soldBatch.BatchNumber} already has {soldBatch.SoldQuantity:0.##} unit(s) sold. Use a Purchase Return instead to correct this invoice.";
        }

        return null;
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
            .Select(p => new { p.Id, p.Sku, p.Name, p.CostPrice, p.SalePrice, p.UnitOfMeasure!.Symbol, p.CategoryId, Category = p.Category!.Name }).ToListAsync();

        ViewData["Products"] = products.Select(p => new
        {
            p.Id,
            p.Sku,
            p.Name,
            p.CostPrice,
            SuggestedSalePrice = latestBatchSalePrices.GetValueOrDefault(p.Id, p.SalePrice),
            p.Symbol,
            p.CategoryId,
            p.Category
        }).ToList();

        // Line items pick a category first, then a product filtered to that category — see
        // Views/PurchaseInvoices/Create.cshtml.
        ViewData["Categories"] = await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
    }
}
