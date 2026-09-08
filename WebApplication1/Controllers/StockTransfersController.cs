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

        return View(await query.Include(t => t.Items).OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Items).ThenInclude(i => i.Product)
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
        else if (!User.IsWarehouseAllowed(model.FromWarehouseId))
        {
            ModelState.AddModelError(nameof(model.FromWarehouseId), "You are not assigned to this warehouse.");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == model.ToWarehouseId))
        {
            ModelState.AddModelError(nameof(model.ToWarehouseId), "Please select a destination warehouse.");
        }
        else if (!User.IsWarehouseAllowed(model.ToWarehouseId))
        {
            ModelState.AddModelError(nameof(model.ToWarehouseId), "You are not assigned to this warehouse.");
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

        TempData["Success"] = $"Stock transfer {transfer.TransferNumber} posted.";
        return RedirectToAction(nameof(Details), new { id = transfer.Id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.StockTransfer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var transfer = await db.StockTransfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
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

        TempData["Success"] = $"Stock transfer {transfer.TransferNumber} cancelled and reversed.";
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

        ViewData["Warehouses"] = new SelectList(await warehouses.OrderBy(w => w.Name).ToListAsync(), "Id", "Name");
        ViewData["WarehouseScoped"] = singleWarehouseId.HasValue;
        ViewData["ScopedWarehouseId"] = singleWarehouseId;

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
