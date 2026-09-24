using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

// Stock corrections go through a request/approval flow: a Sales or Purchase officer files a
// request (nothing changes), then a Manager/Admin approves it — which is the only moment stock,
// FIFO batches and the ledger move — or rejects it, which changes nothing.
public class StockAdjustmentsController(
    ApplicationDbContext db,
    IStockService stockService,
    IFifoAllocationService fifoService,
    IAccountingService accountingService,
    IActivityNotifier notifier) : Controller
{
    private const string RequesterRoles = $"{Roles.SalesOfficer},{Roles.PurchaseOfficer}";
    private const int MaxApprovalAttempts = 3;

    [Authorize(Roles = Roles.InventoryManagers)]
    public async Task<IActionResult> Index(StockAdjustmentStatus? status)
    {
        var query = ScopedQuery().Include(a => a.Warehouse).Include(a => a.Items).AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status);
        }

        ViewData["Status"] = status;
        ViewData["CanReview"] = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager);
        ViewData["PendingCount"] = await ScopedQuery().CountAsync(a => a.Status == StockAdjustmentStatus.Pending);

        return View(await query.OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id).ToListAsync());
    }

    [Authorize(Roles = Roles.InventoryManagers)]
    public async Task<IActionResult> Details(int id)
    {
        var adjustment = await db.StockAdjustments
            .Include(a => a.Warehouse)
            .Include(a => a.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.UnitOfMeasure)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (adjustment is null) return NotFound();
        if (!User.IsWarehouseAllowed(adjustment.WarehouseId)) return Forbid();

        // What a reviewer needs to judge a decrease: the FIFO-available quantity right now.
        var productIds = adjustment.Items.Select(i => i.ProductId).Distinct().ToList();
        ViewData["Available"] = await db.ProductBatches
            .Where(b => b.IsActive && b.WarehouseId == adjustment.WarehouseId && productIds.Contains(b.ProductId))
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(b => b.RemainingQuantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty);
        ViewData["CanReview"] = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager);
        ViewData["IsRequester"] = string.Equals(adjustment.RequestedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase);

        return View(adjustment);
    }

    [Authorize(Roles = RequesterRoles)]
    public async Task<IActionResult> Create()
    {
        await PopulateAsync();
        var warehouseIds = User.GetWarehouseIds();
        return View(new StockAdjustmentCreateViewModel
        {
            WarehouseId = warehouseIds is { Count: 1 } ids ? ids[0] : 0,
            Items = [new StockAdjustmentLineInput()]
        });
    }

    [HttpPost]
    [Authorize(Roles = RequesterRoles)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StockAdjustmentCreateViewModel model)
    {
        model.Items = model.Items.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList();
        if (model.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one product line.");
        }

        if (!User.IsWarehouseAllowed(model.WarehouseId)
            || !await db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId && w.IsActive))
        {
            ModelState.AddModelError(nameof(model.WarehouseId), "Please select one of your warehouses.");
        }

        if (model.Reason == StockAdjustmentReason.Other && string.IsNullOrWhiteSpace(model.Notes))
        {
            ModelState.AddModelError(nameof(model.Notes), "Please describe the reason in the notes.");
        }

        var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => p.IsActive && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        foreach (var item in model.Items)
        {
            if (!products.ContainsKey(item.ProductId))
            {
                ModelState.AddModelError(string.Empty, "One of the selected products is no longer available.");
                break;
            }

            if (item.Direction == StockAdjustmentDirection.Increase && (item.UnitCost is null || item.UnitCost <= 0))
            {
                ModelState.AddModelError(string.Empty, $"Enter the unit cost for the units being added to {products[item.ProductId].Name}.");
            }
        }

        // Early feedback only — the FIFO check is repeated at approval, when it actually counts.
        if (ModelState.IsValid)
        {
            foreach (var group in model.Items.Where(i => i.Direction == StockAdjustmentDirection.Decrease).GroupBy(i => i.ProductId))
            {
                try
                {
                    await fifoService.PreviewAsync(group.Key, model.WarehouseId, group.Sum(i => i.Quantity));
                }
                catch (InsufficientStockException ex)
                {
                    ModelState.AddModelError(string.Empty, $"{products[group.Key].Name}: {ex.Message}");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync();
            return View(model);
        }

        var count = await db.StockAdjustments.CountAsync();
        var adjustment = new StockAdjustment
        {
            AdjustmentNumber = $"ADJ-{count + 1:D6}",
            WarehouseId = model.WarehouseId,
            Reason = model.Reason!.Value,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            Status = StockAdjustmentStatus.Pending,
            RequestedBy = User.Identity?.Name,
            RequestedAt = DateTime.UtcNow
        };

        foreach (var item in model.Items)
        {
            var increase = item.Direction == StockAdjustmentDirection.Increase;
            adjustment.Items.Add(new StockAdjustmentItem
            {
                ProductId = item.ProductId,
                Direction = item.Direction,
                Quantity = item.Quantity,
                UnitCost = increase ? item.UnitCost : null,
                SalePrice = increase ? item.SalePrice : null
            });
        }

        db.StockAdjustments.Add(adjustment);
        await db.SaveChangesAsync();

        var warehouseName = (await db.Warehouses.FindAsync(adjustment.WarehouseId))?.Name;
        await notifier.NotifyAsync(
            "Stock Adjustment Requested",
            $"{adjustment.AdjustmentNumber} — {adjustment.Items.Count} line(s) in {warehouseName} awaiting approval.",
            "fas fa-scale-balanced text-warning",
            [adjustment.WarehouseId]);

        TempData["Success"] = $"Stock adjustment {adjustment.AdjustmentNumber} submitted for approval.";
        return RedirectToAction(nameof(Details), new { id = adjustment.Id });
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? reviewNote, string? returnUrl)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var adjustment = await db.StockAdjustments
                    .Include(a => a.Warehouse)
                    .Include(a => a.Items).ThenInclude(i => i.Product)
                    .FirstOrDefaultAsync(a => a.Id == id);
                if (adjustment is null) return NotFound();

                if (adjustment.Status != StockAdjustmentStatus.Pending)
                {
                    TempData["Error"] = $"{adjustment.AdjustmentNumber} has already been {adjustment.Status.ToString().ToLowerInvariant()}.";
                    return RedirectBack(id, returnUrl);
                }

                decimal increaseCost = 0, decreaseCost = 0;
                var batchCount = await db.ProductBatches.CountAsync();
                var now = DateTime.UtcNow;

                foreach (var item in adjustment.Items)
                {
                    if (item.Direction == StockAdjustmentDirection.Decrease)
                    {
                        // FIFO, exactly like a sale: oldest batches go first, at their own cost.
                        var allocations = await fifoService.AllocateAsync(item.ProductId, adjustment.WarehouseId, item.Quantity);
                        item.AppliedCostTotal = 0;
                        foreach (var allocation in allocations)
                        {
                            var batch = allocation.Batch;
                            await stockService.IssueStockAsync(item.ProductId, adjustment.WarehouseId, allocation.Quantity,
                                adjustment.AdjustmentNumber, batch: batch, unitCost: batch.PurchasePrice, unitSalePrice: batch.SalePrice,
                                notes: $"Stock adjustment (decrease) — {adjustment.Reason.GetDisplayName()}");
                            item.AppliedCostTotal += allocation.Quantity * batch.PurchasePrice;
                        }
                        decreaseCost += item.AppliedCostTotal;
                    }
                    else
                    {
                        var unitCost = item.UnitCost ?? 0;
                        var batch = new ProductBatch
                        {
                            ProductId = item.ProductId,
                            WarehouseId = adjustment.WarehouseId,
                            BatchNumber = $"BATCH-{++batchCount:D6}",
                            PurchaseDate = now,
                            PurchasePrice = unitCost,
                            SalePrice = item.SalePrice ?? item.Product?.SalePrice ?? 0,
                            OriginalQuantity = item.Quantity,
                            RemainingQuantity = item.Quantity
                        };
                        db.ProductBatches.Add(batch);

                        await stockService.ReceiveStockAsync(item.ProductId, adjustment.WarehouseId, item.Quantity,
                            adjustment.AdjustmentNumber, batch: batch, unitCost: unitCost, unitSalePrice: batch.SalePrice,
                            notes: $"Stock adjustment (increase) — {adjustment.Reason.GetDisplayName()}");
                        item.AppliedCostTotal = item.Quantity * unitCost;
                        increaseCost += item.AppliedCostTotal;
                    }
                }

                if (increaseCost > 0 || decreaseCost > 0)
                {
                    await accountingService.PostStockAdjustmentAsync(adjustment, increaseCost, decreaseCost);
                }

                adjustment.Status = StockAdjustmentStatus.Approved;
                adjustment.ReviewedBy = User.Identity?.Name;
                adjustment.ReviewedAt = now;
                adjustment.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();

                await db.SaveChangesAsync();

                await notifier.NotifyAsync(
                    "Stock Adjustment Approved",
                    $"{adjustment.AdjustmentNumber} was approved — stock in {adjustment.Warehouse?.Name} has been updated.",
                    "fas fa-circle-check text-success",
                    [adjustment.WarehouseId]);
                await NotifyStockChangeAsync(adjustment.Items.Select(i => i.ProductId), adjustment.WarehouseId);

                TempData["Success"] = $"{adjustment.AdjustmentNumber} approved — stock and accounts updated.";
                return RedirectBack(id, returnUrl);
            }
            catch (InsufficientStockException ex)
            {
                // Stock was sold or moved since the request was filed. Nothing has been saved;
                // the request stays pending so the reviewer can reject it (or ask for a new one).
                db.ChangeTracker.Clear();
                TempData["Error"] = $"Cannot approve: {ex.Message} The request is still pending — reject it if it is no longer valid.";
                return RedirectBack(id, returnUrl);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another sale/adjustment touched the same batch (or another reviewer acted first).
                // Start over from freshly read rows so the FIFO allocation and status are current.
                db.ChangeTracker.Clear();
                if (attempt >= MaxApprovalAttempts)
                {
                    TempData["Error"] = "The stock changed while approving. Please try again.";
                    return RedirectBack(id, returnUrl);
                }
            }
        }
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reviewNote, string? returnUrl)
    {
        var adjustment = await db.StockAdjustments.Include(a => a.Warehouse).FirstOrDefaultAsync(a => a.Id == id);
        if (adjustment is null) return NotFound();

        if (adjustment.Status != StockAdjustmentStatus.Pending)
        {
            TempData["Error"] = $"{adjustment.AdjustmentNumber} has already been {adjustment.Status.ToString().ToLowerInvariant()}.";
            return RedirectBack(id, returnUrl);
        }

        if (string.IsNullOrWhiteSpace(reviewNote))
        {
            TempData["Error"] = "Please enter a reason for rejecting the request.";
            return RedirectBack(id, returnUrl);
        }

        adjustment.Status = StockAdjustmentStatus.Rejected;
        adjustment.ReviewedBy = User.Identity?.Name;
        adjustment.ReviewedAt = DateTime.UtcNow;
        adjustment.ReviewNote = reviewNote.Trim();

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = $"{adjustment.AdjustmentNumber} was changed by someone else — please review it again.";
            return RedirectBack(id, returnUrl);
        }

        await notifier.NotifyAsync(
            "Stock Adjustment Rejected",
            $"{adjustment.AdjustmentNumber} was rejected: {adjustment.ReviewNote}",
            "fas fa-circle-xmark text-danger",
            [adjustment.WarehouseId]);

        TempData["Success"] = $"{adjustment.AdjustmentNumber} rejected — no stock was changed.";
        return RedirectBack(id, returnUrl);
    }

    // Approve/Reject can be triggered from the list page as well as the Details page; the list
    // sends where to come back to. Only local URLs are honoured (no open redirect).
    private IActionResult RedirectBack(int id, string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Details), new { id });

    // The requester can withdraw their own request while it is still pending.
    [HttpPost]
    [Authorize(Roles = RequesterRoles)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var adjustment = await db.StockAdjustments.FirstOrDefaultAsync(a => a.Id == id);
        if (adjustment is null) return NotFound();
        if (!User.IsWarehouseAllowed(adjustment.WarehouseId)) return Forbid();
        if (!string.Equals(adjustment.RequestedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase)) return Forbid();

        if (adjustment.Status != StockAdjustmentStatus.Pending)
        {
            TempData["Error"] = $"{adjustment.AdjustmentNumber} has already been {adjustment.Status.ToString().ToLowerInvariant()}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        adjustment.Status = StockAdjustmentStatus.Cancelled;
        adjustment.ReviewedBy = User.Identity?.Name;
        adjustment.ReviewedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = $"{adjustment.AdjustmentNumber} was reviewed before it could be cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = $"{adjustment.AdjustmentNumber} cancelled.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Requests visible to this user: everything for Admin/Manager, else their own warehouses.
    private IQueryable<StockAdjustment> ScopedQuery()
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.StockAdjustments.AsQueryable();
        return warehouseIds is null ? query : query.Where(a => warehouseIds.Contains(a.WarehouseId));
    }

    private async Task PopulateAsync()
    {
        var warehouseIds = User.GetWarehouseIds();
        var warehouseQuery = db.Warehouses.Where(w => w.IsActive);
        if (warehouseIds is not null)
        {
            warehouseQuery = warehouseQuery.Where(w => warehouseIds.Contains(w.Id));
        }
        ViewData["Warehouses"] = new SelectList(await warehouseQuery.OrderBy(w => w.Name).ToListAsync(), "Id", "Name");

        // FIFO-available stock per warehouse (sum of active batches) — the number a decrease is
        // actually checked against — plus default prices to pre-fill an increase.
        var batchTotals = await db.ProductBatches
            .Where(b => b.IsActive)
            .GroupBy(b => new { b.ProductId, b.WarehouseId })
            .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Quantity = g.Sum(b => b.RemainingQuantity) })
            .ToListAsync();
        var byProduct = batchTotals.ToLookup(b => b.ProductId);

        var products = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Brand, p.Size, Category = p.Category!.Name, Symbol = p.UnitOfMeasure!.Symbol, p.CostPrice, p.SalePrice })
            .ToListAsync();
        ViewData["Products"] = products.Select(p => new
        {
            p.Id,
            p.Sku,
            p.Name,
            p.Brand,
            p.Size,
            p.Category,
            p.Symbol,
            p.CostPrice,
            p.SalePrice,
            Stocks = byProduct[p.Id].Select(s => new { s.WarehouseId, s.Quantity })
        }).ToList();
    }

    private async Task NotifyStockChangeAsync(IEnumerable<int> productIds, int warehouseId)
    {
        var productIdList = productIds.Distinct().ToList();
        var products = await db.Products
            .Where(p => productIdList.Contains(p.Id))
            .Select(p => new { p.Id, p.CurrentStock })
            .ToListAsync();
        var stocks = await db.ProductWarehouseStocks
            .Where(s => productIdList.Contains(s.ProductId) && s.WarehouseId == warehouseId)
            .ToListAsync();

        var payloadProducts = products.Select(p => new
        {
            productId = p.Id,
            currentStock = p.CurrentStock,
            warehouseStocks = stocks.Where(s => s.ProductId == p.Id).ToDictionary(s => s.WarehouseId.ToString(), s => s.Quantity)
        });

        await notifier.NotifyDataChangeAsync("StockChange", new { products = payloadProducts }, [warehouseId]);
    }
}
