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

public class StockTransfersController(ApplicationDbContext db, IStockService stockService, IActivityNotifier notifier) : Controller
{
    public async Task<IActionResult> Index()
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.StockTransfers.Include(t => t.FromWarehouse).Include(t => t.ToWarehouse).AsQueryable();

        if (warehouseIds is not null)
        {
            query = query.Where(t => warehouseIds.Contains(t.FromWarehouseId) || warehouseIds.Contains(t.ToWarehouseId));
        }

        return View(await query.Include(t => t.Items.Where(i => i.IsCurrent)).OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer is null) return NotFound();
        if (!User.IsWarehouseAllowed(transfer.FromWarehouseId) && !User.IsWarehouseAllowed(transfer.ToWarehouseId)) return Forbid();
        return View(transfer);
    }

    [Authorize(Policy = Permissions.StockTransfer)]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        var warehouseIds = User.GetWarehouseIds();
        return View(new StockTransferCreateViewModel
        {
            Items = [new TransferLineInput()],
            FromWarehouseId = warehouseIds is { Count: 1 } ids ? ids[0] : 0
        });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.StockTransfer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StockTransferCreateViewModel model)
    {
        model.Items = model.Items.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList();
        if (model.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one product line.");
        }

        if (model.FromWarehouseId == model.ToWarehouseId)
        {
            ModelState.AddModelError(string.Empty, "The source and destination warehouse must be different.");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == model.FromWarehouseId))
        {
            ModelState.AddModelError(nameof(model.FromWarehouseId), "Please select a source warehouse.");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == model.ToWarehouseId))
        {
            ModelState.AddModelError(nameof(model.ToWarehouseId), "Please select a destination warehouse.");
        }

        var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var fromStocks = await db.ProductWarehouseStocks
            .Where(s => s.WarehouseId == model.FromWarehouseId && productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

        foreach (var item in model.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            var available = fromStocks.GetValueOrDefault(item.ProductId, 0);
            if (item.Quantity > available)
            {
                ModelState.AddModelError(string.Empty,
                    $"Insufficient stock for {product.Name} in the source warehouse: available {available:0.##}, requested {item.Quantity:0.##}.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var count = await db.StockTransfers.CountAsync();
        var transfer = new StockTransfer
        {
            TransferNumber = $"XFER-{count + 1:D6}",
            FromWarehouseId = model.FromWarehouseId,
            ToWarehouseId = model.ToWarehouseId,
            Date = model.Date,
            Notes = model.Notes,
            Status = DocumentStatus.Posted,
            CreatedBy = User.Identity?.Name
        };

        foreach (var item in model.Items)
        {
            transfer.Items.Add(new StockTransferItem { ProductId = item.ProductId, Quantity = item.Quantity });
        }

        db.StockTransfers.Add(transfer);

        foreach (var item in transfer.Items)
        {
            await stockService.IssueStockAsync(item.ProductId, transfer.FromWarehouseId, item.Quantity, transfer.TransferNumber, notes: "Transfer out");
            await stockService.ReceiveStockAsync(item.ProductId, transfer.ToWarehouseId, item.Quantity, transfer.TransferNumber, notes: "Transfer in");
        }

        await db.SaveChangesAsync();

        var fromName = (await db.Warehouses.FindAsync(transfer.FromWarehouseId))?.Name;
        var toName = (await db.Warehouses.FindAsync(transfer.ToWarehouseId))?.Name;
        await notifier.NotifyAsync(
            "Stock Transfer Posted",
            $"{transfer.TransferNumber} — {transfer.Items.Count} line(s) moved from {fromName} to {toName}.",
            "fas fa-truck-ramp-box text-warning",
            [transfer.FromWarehouseId, transfer.ToWarehouseId]);
        await NotifyStockChangeAsync(transfer.Items.Select(i => i.ProductId), [transfer.FromWarehouseId, transfer.ToWarehouseId]);
        await NotifyTransferDocAsync(transfer, fromName, toName);

        TempData["Success"] = $"Stock transfer {transfer.TransferNumber} posted.";
        return RedirectToAction(nameof(Details), new { id = transfer.Id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.StockTransfer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var transfer = await db.StockTransfers.Include(t => t.Items.Where(i => i.IsCurrent)).FirstOrDefaultAsync(t => t.Id == id);
        if (transfer is null) return NotFound();
        if (!User.IsWarehouseAllowed(transfer.FromWarehouseId) && !User.IsWarehouseAllowed(transfer.ToWarehouseId)) return Forbid();

        if (transfer.Status == DocumentStatus.Cancelled)
        {
            TempData["Error"] = "This transfer is already cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await stockService.ReverseTransactionsForReferenceAsync(transfer.TransferNumber);
        transfer.Status = DocumentStatus.Cancelled;

        await db.SaveChangesAsync();

        await notifier.NotifyAsync(
            "Stock Transfer Cancelled",
            $"{transfer.TransferNumber} was cancelled and reversed.",
            "fas fa-ban text-danger",
            [transfer.FromWarehouseId, transfer.ToWarehouseId]);
        await NotifyStockChangeAsync(transfer.Items.Select(i => i.ProductId), [transfer.FromWarehouseId, transfer.ToWarehouseId]);
        await NotifyTransferDocAsync(transfer);

        TempData["Success"] = $"Stock transfer {transfer.TransferNumber} cancelled and reversed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Permissions.StockTransfer)]
    public async Task<IActionResult> Edit(int id)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.Items.Where(i => i.IsCurrent))
            .FirstOrDefaultAsync(t => t.Id == id);
        if (transfer is null) return NotFound();
        if (!User.IsWarehouseAllowed(transfer.FromWarehouseId) && !User.IsWarehouseAllowed(transfer.ToWarehouseId)) return Forbid();

        var blockedReason = await EditBlockedReasonAsync(transfer);
        if (blockedReason is not null)
        {
            TempData["Error"] = blockedReason;
            return RedirectToAction(nameof(Details), new { id });
        }

        await PopulateDropdownsAsync();
        var model = new StockTransferCreateViewModel
        {
            FromWarehouseId = transfer.FromWarehouseId,
            ToWarehouseId = transfer.ToWarehouseId,
            Date = transfer.Date,
            Notes = transfer.Notes,
            Items = transfer.Items.Select(i => new TransferLineInput { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
        };
        ViewData["TransferNumber"] = transfer.TransferNumber;
        return View(model);
    }

    // Editing keeps the same transfer number and row rather than cancel-and-recreate: the old
    // lines' stock effect is reversed exactly like Cancel, the old lines are deactivated in
    // place, then the new lines are issued/received fresh, same as Create. StockService's
    // reversal helper tracks which rows it has already reversed (IsReversed), so this can
    // safely happen more than once on the same transfer number without double-reversing.
    [HttpPost]
    [Authorize(Policy = Permissions.StockTransfer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StockTransferCreateViewModel model)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.Items.Where(i => i.IsCurrent))
            .FirstOrDefaultAsync(t => t.Id == id);
        if (transfer is null) return NotFound();
        if (!User.IsWarehouseAllowed(transfer.FromWarehouseId) && !User.IsWarehouseAllowed(transfer.ToWarehouseId)) return Forbid();

        var blockedReason = await EditBlockedReasonAsync(transfer);
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

        if (model.FromWarehouseId == model.ToWarehouseId)
        {
            ModelState.AddModelError(string.Empty, "The source and destination warehouse must be different.");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == model.FromWarehouseId))
        {
            ModelState.AddModelError(nameof(model.FromWarehouseId), "Please select a source warehouse.");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == model.ToWarehouseId))
        {
            ModelState.AddModelError(nameof(model.ToWarehouseId), "Please select a destination warehouse.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            ViewData["TransferNumber"] = transfer.TransferNumber;
            return View(model);
        }

        // Reverse this transfer's own stock effect first — the availability check just below
        // sees the restored quantity immediately (EF resolves the same tracked
        // ProductWarehouseStock instances by identity rather than re-reading stale DB values).
        var oldFromWarehouseId = transfer.FromWarehouseId;
        var oldToWarehouseId = transfer.ToWarehouseId;
        var oldProductIds = transfer.Items.Select(i => i.ProductId).ToList();
        foreach (var item in transfer.Items)
        {
            item.IsCurrent = false;
        }
        await stockService.ReverseTransactionsForReferenceAsync(transfer.TransferNumber);

        var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var fromStocks = await db.ProductWarehouseStocks
            .Where(s => s.WarehouseId == model.FromWarehouseId && productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

        foreach (var item in model.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            var available = fromStocks.GetValueOrDefault(item.ProductId, 0);
            if (item.Quantity > available)
            {
                ModelState.AddModelError(string.Empty,
                    $"Insufficient stock for {product.Name} in the source warehouse: available {available:0.##}, requested {item.Quantity:0.##}.");
            }
        }

        if (!ModelState.IsValid)
        {
            // Nothing has been saved yet — not calling SaveChangesAsync leaves the database
            // untouched, so simply re-showing the form is enough to discard the in-memory undo.
            await PopulateDropdownsAsync();
            ViewData["TransferNumber"] = transfer.TransferNumber;
            return View(model);
        }

        transfer.FromWarehouseId = model.FromWarehouseId;
        transfer.ToWarehouseId = model.ToWarehouseId;
        transfer.Date = model.Date;
        transfer.Notes = model.Notes;

        foreach (var item in model.Items)
        {
            transfer.Items.Add(new StockTransferItem { ProductId = item.ProductId, Quantity = item.Quantity });
        }

        foreach (var newItem in transfer.Items.Where(i => i.IsCurrent))
        {
            await stockService.IssueStockAsync(newItem.ProductId, transfer.FromWarehouseId, newItem.Quantity, transfer.TransferNumber, notes: "Transfer out");
            await stockService.ReceiveStockAsync(newItem.ProductId, transfer.ToWarehouseId, newItem.Quantity, transfer.TransferNumber, notes: "Transfer in");
        }

        await db.SaveChangesAsync();

        var fromName = (await db.Warehouses.FindAsync(transfer.FromWarehouseId))?.Name;
        var toName = (await db.Warehouses.FindAsync(transfer.ToWarehouseId))?.Name;
        await notifier.NotifyAsync(
            "Stock Transfer Edited",
            $"{transfer.TransferNumber} was edited — {transfer.Items.Count(i => i.IsCurrent)} line(s) moved from {fromName} to {toName}.",
            "fas fa-pen text-info",
            [transfer.FromWarehouseId, transfer.ToWarehouseId]);

        var touchedProductIds = oldProductIds.Concat(transfer.Items.Where(i => i.IsCurrent).Select(i => i.ProductId));
        var touchedWarehouseIds = new[] { oldFromWarehouseId, oldToWarehouseId, transfer.FromWarehouseId, transfer.ToWarehouseId };
        await NotifyStockChangeAsync(touchedProductIds, touchedWarehouseIds);
        await NotifyTransferDocAsync(transfer, fromName, toName);

        TempData["Success"] = $"Stock transfer {transfer.TransferNumber} updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Null when editing is allowed; otherwise the reason to show the user. Shared by both the
    // Edit GET (so a blocked transfer never even reaches the form) and POST (re-checked in case
    // something changed between GET and submit). Reversing this transfer means pulling its
    // quantity back out of the destination warehouse — blocked if that stock (or some of it)
    // has already moved on from there (sold, or transferred onward again).
    private async Task<string?> EditBlockedReasonAsync(StockTransfer transfer)
    {
        if (transfer.Status != DocumentStatus.Posted)
        {
            return "Only a posted transfer can be edited.";
        }

        var productIds = transfer.Items.Select(i => i.ProductId).Distinct().ToList();
        var destStocks = await db.ProductWarehouseStocks
            .Where(s => s.WarehouseId == transfer.ToWarehouseId && productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

        foreach (var item in transfer.Items)
        {
            if (destStocks.GetValueOrDefault(item.ProductId, 0) < item.Quantity)
            {
                var productName = await db.Products.Where(p => p.Id == item.ProductId).Select(p => p.Name).FirstOrDefaultAsync();
                return $"Cannot edit: {productName} has already moved out of the destination warehouse. Create a new transfer instead to correct this.";
            }
        }

        return null;
    }

    // Broadcasts the authoritative post-save stock figures for every affected product so open
    // pages (Products Index/Details) can patch their own displayed quantities instead of relying
    // on the recipient's page being reloaded. Sends absolute new values, not deltas — deltas would
    // require the client to already know the pre-change quantity, which it may not if it only
    // just opened the page or if it's not tracking every warehouse shown here.
    private async Task NotifyStockChangeAsync(IEnumerable<int> productIds, IEnumerable<int> warehouseIds)
    {
        var productIdList = productIds.Distinct().ToList();
        var warehouseIdList = warehouseIds.Distinct().ToList();

        var products = await db.Products
            .Where(p => productIdList.Contains(p.Id))
            .Select(p => new { p.Id, p.CurrentStock })
            .ToListAsync();
        var stocks = await db.ProductWarehouseStocks
            .Where(s => productIdList.Contains(s.ProductId) && warehouseIdList.Contains(s.WarehouseId))
            .ToListAsync();

        var payloadProducts = products.Select(p => new
        {
            productId = p.Id,
            currentStock = p.CurrentStock,
            warehouseStocks = stocks.Where(s => s.ProductId == p.Id).ToDictionary(s => s.WarehouseId.ToString(), s => s.Quantity)
        });

        await notifier.NotifyDataChangeAsync("StockChange", new { products = payloadProducts }, warehouseIdList);
    }

    // fromName/toName are only needed by the client when it has to insert a brand-new row (a
    // freshly posted transfer it doesn't have yet) — Cancel/Edit only ever touch a row already on
    // the page, so they can omit them.
    private Task NotifyTransferDocAsync(StockTransfer transfer, string? fromName = null, string? toName = null)
    {
        return notifier.NotifyDataChangeAsync("StockTransferDoc", new
        {
            id = transfer.Id,
            transferNumber = transfer.TransferNumber,
            fromWarehouseId = transfer.FromWarehouseId,
            fromWarehouseName = fromName,
            toWarehouseId = transfer.ToWarehouseId,
            toWarehouseName = toName,
            date = transfer.Date.ToString("yyyy-MM-dd"),
            status = transfer.Status.ToString(),
            lineCount = transfer.Items.Count(i => i.IsCurrent)
        }, [transfer.FromWarehouseId, transfer.ToWarehouseId]);
    }

    // Both From and To Warehouse dropdowns always list every warehouse — including inactive ones,
    // since a transfer is often exactly how remaining stock gets moved OUT of a warehouse that's
    // been deactivated — and regardless of the user's own assignment. This is deliberately unscoped
    // and unfiltered here, unlike every other warehouse-facing dropdown in the app.
    private async Task PopulateDropdownsAsync()
    {
        var warehouses = await db.Warehouses.OrderBy(w => w.Name).ToListAsync();
        ViewData["Warehouses"] = new SelectList(warehouses, "Id", "Name");

        // Show stock for the scoped/source warehouse in the picker; for unscoped users this
        // reflects whichever warehouse they currently have selected as "From" on the client.
        ViewData["Products"] = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                p.UnitOfMeasure!.Symbol,
                Stocks = p.WarehouseStocks.Select(s => new { s.WarehouseId, s.Quantity })
            }).ToListAsync();
    }
}
