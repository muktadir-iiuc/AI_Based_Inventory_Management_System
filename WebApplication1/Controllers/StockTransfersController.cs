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
using WebApplication1.Models.Saas;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

// Transfers go through the same request/approval flow as Stock Adjustments: a Sales or Purchase
// officer files a request (nothing moves), then a Manager/Admin approves it — the only moment
// stock and FIFO batches move — or rejects it. Admin/Manager are the approvers, so a transfer
// they create themselves is approved on the spot. Editing or cancelling a transfer that has
// already moved stock is likewise reserved for them. The whole menu is only for users who have
// been assigned the Stock Transfers permission (Users & Roles -> Edit); Admin/Manager always have it.
[Authorize(Policy = Permissions.StockTransfer)]
[RequireFeature(Features.StockTransfers)]
public class StockTransfersController(
    ApplicationDbContext db,
    IStockService stockService,
    IFifoAllocationService fifoService,
    IActivityNotifier notifier) : Controller
{
    private const int MaxApprovalAttempts = 3;

    private bool IsApprover => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Manager);

    public async Task<IActionResult> Index(StockTransferApprovalStatus? status)
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.StockTransfers.Include(t => t.FromWarehouse).Include(t => t.ToWarehouse).AsQueryable();

        if (warehouseIds is not null)
        {
            query = query.Where(t => warehouseIds.Contains(t.FromWarehouseId) || warehouseIds.Contains(t.ToWarehouseId));
        }

        var pendingQuery = query.Where(t => t.ApprovalStatus == StockTransferApprovalStatus.Pending);
        ViewData["PendingCount"] = await pendingQuery.CountAsync();
        ViewData["Status"] = status;
        ViewData["CanRequest"] = true;
        ViewData["IsApprover"] = IsApprover;

        if (status.HasValue)
        {
            query = query.Where(t => t.ApprovalStatus == status);
        }

        return View(await query.Include(t => t.Items.Where(i => i.IsCurrent))
            .Include(t => t.PurchaseInvoiceLinks).ThenInclude(l => l.PurchaseInvoice).ThenInclude(p => p!.Supplier)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Product)
            .Include(t => t.PurchaseInvoiceLinks).ThenInclude(l => l.PurchaseInvoice)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer is null) return NotFound();
        if (!User.IsWarehouseAllowed(transfer.FromWarehouseId) && !User.IsWarehouseAllowed(transfer.ToWarehouseId)) return Forbid();

        // What a reviewer needs to judge a pending request: FIFO-available stock at the source now.
        // Only shown to users assigned to the source warehouse (and Admin/Manager) — a destination-side
        // user must not see another warehouse's quantity.
        var productIds = transfer.Items.Select(i => i.ProductId).Distinct().ToList();
        ViewData["ShowSourceStock"] = User.IsWarehouseAllowed(transfer.FromWarehouseId);
        ViewData["Available"] = !User.IsWarehouseAllowed(transfer.FromWarehouseId) ? new Dictionary<int, decimal>() : await db.ProductBatches
            .Where(b => b.IsActive && b.WarehouseId == transfer.FromWarehouseId && productIds.Contains(b.ProductId))
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(b => b.RemainingQuantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty);
        ViewData["IsApprover"] = IsApprover;
        ViewData["IsRequester"] = string.Equals(transfer.CreatedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase);
        return View(transfer);
    }

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

        if (ModelState.IsValid)
        {
            await ValidateStockAsync(model);
        }

        model.PurchaseInvoiceIds = await AllowedPurchaseInvoiceIdsAsync(model.PurchaseInvoiceIds, []);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model.PurchaseInvoiceIds, model.SupplierId);
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
            // Not posted until a reviewer approves it — see StockTransfer.Status.
            Status = DocumentStatus.Cancelled,
            ApprovalStatus = StockTransferApprovalStatus.Pending,
            CreatedBy = User.Identity?.Name
        };

        foreach (var item in model.Items)
        {
            transfer.Items.Add(new StockTransferItem { ProductId = item.ProductId, Quantity = item.Quantity });
        }

        foreach (var invoiceId in model.PurchaseInvoiceIds)
        {
            transfer.PurchaseInvoiceLinks.Add(new StockTransferPurchaseInvoice { PurchaseInvoiceId = invoiceId });
        }

        db.StockTransfers.Add(transfer);
        await db.SaveChangesAsync();

        var fromName = (await db.Warehouses.FindAsync(transfer.FromWarehouseId))?.Name;
        var toName = (await db.Warehouses.FindAsync(transfer.ToWarehouseId))?.Name;

        if (IsApprover)
        {
            // The approver is the one asking, so there is nobody to wait for: run the same
            // approval as a reviewer would. If stock ran short in the meantime, drop the request
            // again rather than leave a pending one the user never meant to file.
            var result = await ApproveCoreAsync(transfer.Id, null);
            if (!result.Ok)
            {
                var orphan = await db.StockTransfers.FirstOrDefaultAsync(t => t.Id == transfer.Id);
                if (orphan is not null)
                {
                    db.StockTransfers.Remove(orphan);
                    await db.SaveChangesAsync();
                }
                ModelState.AddModelError(string.Empty, result.Error ?? "The transfer could not be posted.");
                await PopulateDropdownsAsync(model.PurchaseInvoiceIds, model.SupplierId);
                return View(model);
            }

            TempData["Success"] = $"Stock transfer {transfer.TransferNumber} posted.";
            return RedirectToAction(nameof(Details), new { id = transfer.Id });
        }

        await notifier.NotifyAsync(
            "Stock Transfer Requested",
            $"{transfer.TransferNumber} — {transfer.Items.Count} line(s) from {fromName} to {toName} awaiting approval.",
            "fas fa-truck-ramp-box text-warning",
            [transfer.FromWarehouseId, transfer.ToWarehouseId]);
        await NotifyTransferDocAsync(transfer, fromName, toName);

        TempData["Success"] = $"Stock transfer {transfer.TransferNumber} submitted for approval.";
        return RedirectToAction(nameof(Details), new { id = transfer.Id });
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? reviewNote, string? returnUrl)
    {
        var result = await ApproveCoreAsync(id, reviewNote);
        if (result.NotFound) return NotFound();

        if (result.Ok) TempData["Success"] = $"{result.Number} approved — stock has been moved.";
        else TempData["Error"] = result.Error;
        return RedirectBack(id, returnUrl);
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reviewNote, string? returnUrl)
    {
        var transfer = await db.StockTransfers.Include(t => t.Items.Where(i => i.IsCurrent)).FirstOrDefaultAsync(t => t.Id == id);
        if (transfer is null) return NotFound();

        if (transfer.ApprovalStatus != StockTransferApprovalStatus.Pending)
        {
            TempData["Error"] = $"{transfer.TransferNumber} has already been {transfer.ApprovalStatus.ToString().ToLowerInvariant()}.";
            return RedirectBack(id, returnUrl);
        }

        if (string.IsNullOrWhiteSpace(reviewNote))
        {
            TempData["Error"] = "Please enter a reason for rejecting the request.";
            return RedirectBack(id, returnUrl);
        }

        transfer.ApprovalStatus = StockTransferApprovalStatus.Rejected;
        transfer.ReviewedBy = User.Identity?.Name;
        transfer.ReviewedAt = DateTime.UtcNow;
        transfer.ReviewNote = reviewNote.Trim();

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = $"{transfer.TransferNumber} was changed by someone else — please review it again.";
            return RedirectBack(id, returnUrl);
        }

        await notifier.NotifyAsync(
            "Stock Transfer Rejected",
            $"{transfer.TransferNumber} was rejected: {transfer.ReviewNote}",
            "fas fa-circle-xmark text-danger",
            [transfer.FromWarehouseId, transfer.ToWarehouseId]);
        await NotifyTransferDocAsync(transfer);

        TempData["Success"] = $"{transfer.TransferNumber} rejected — no stock was moved.";
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id)
    {
        var transfer = await db.StockTransfers.Include(t => t.Items.Where(i => i.IsCurrent)).FirstOrDefaultAsync(t => t.Id == id);
        if (transfer is null) return NotFound();
        if (!string.Equals(transfer.CreatedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase)) return Forbid();

        if (transfer.ApprovalStatus != StockTransferApprovalStatus.Pending)
        {
            TempData["Error"] = $"{transfer.TransferNumber} has already been {transfer.ApprovalStatus.ToString().ToLowerInvariant()}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        transfer.ApprovalStatus = StockTransferApprovalStatus.Withdrawn;
        transfer.ReviewedBy = User.Identity?.Name;
        transfer.ReviewedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = $"{transfer.TransferNumber} was reviewed before it could be withdrawn.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await NotifyTransferDocAsync(transfer);
        TempData["Success"] = $"{transfer.TransferNumber} withdrawn.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Cancelling a transfer that has already moved stock is an approver action.
    [HttpPost]
    [Authorize(Roles = Roles.AdminManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var transfer = await db.StockTransfers.Include(t => t.Items.Where(i => i.IsCurrent)).FirstOrDefaultAsync(t => t.Id == id);
        if (transfer is null) return NotFound();
        if (!User.IsWarehouseAllowed(transfer.FromWarehouseId) && !User.IsWarehouseAllowed(transfer.ToWarehouseId)) return Forbid();

        if (transfer.ApprovalStatus != StockTransferApprovalStatus.Approved || transfer.Status != DocumentStatus.Posted)
        {
            TempData["Error"] = transfer.Status == DocumentStatus.Cancelled && transfer.ApprovalStatus == StockTransferApprovalStatus.Approved
                ? "This transfer is already cancelled."
                : "Only an approved, posted transfer can be cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var blocked = await ReverseBlockedReasonAsync(transfer, "cancel");
        if (blocked is not null)
        {
            TempData["Error"] = blocked;
            return RedirectToAction(nameof(Details), new { id });
        }

        await ReverseTransferBatchesAsync(transfer);
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

    [Authorize(Roles = Roles.AdminManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.Items.Where(i => i.IsCurrent))
            .Include(t => t.PurchaseInvoiceLinks)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (transfer is null) return NotFound();
        if (!User.IsWarehouseAllowed(transfer.FromWarehouseId) && !User.IsWarehouseAllowed(transfer.ToWarehouseId)) return Forbid();

        var blockedReason = await EditBlockedReasonAsync(transfer);
        if (blockedReason is not null)
        {
            TempData["Error"] = blockedReason;
            return RedirectToAction(nameof(Details), new { id });
        }

        var linkedInvoiceIds = transfer.PurchaseInvoiceLinks.Select(l => l.PurchaseInvoiceId).ToList();
        await PopulateDropdownsAsync(linkedInvoiceIds);
        var model = new StockTransferCreateViewModel
        {
            FromWarehouseId = transfer.FromWarehouseId,
            ToWarehouseId = transfer.ToWarehouseId,
            Date = transfer.Date,
            Notes = transfer.Notes,
            PurchaseInvoiceIds = linkedInvoiceIds,
            Items = transfer.Items.Select(i => new TransferLineInput { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
        };
        ViewData["TransferNumber"] = transfer.TransferNumber;
        return View(model);
    }

    // Editing keeps the same transfer number and row rather than cancel-and-recreate: the old
    // lines' effect (stock AND FIFO batches) is reversed exactly like Cancel, the old lines are
    // deactivated in place, then the new lines are moved fresh, same as approval. The reversal is
    // saved first — inside one database transaction with the re-move — so the FIFO allocation
    // for the new lines sees the batches the old lines gave back.
    [HttpPost]
    [Authorize(Roles = Roles.AdminManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StockTransferCreateViewModel model)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.Items.Where(i => i.IsCurrent))
            .Include(t => t.PurchaseInvoiceLinks)
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

        model.PurchaseInvoiceIds = await AllowedPurchaseInvoiceIdsAsync(
            model.PurchaseInvoiceIds,
            transfer.PurchaseInvoiceLinks.Select(l => l.PurchaseInvoiceId).ToList());

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model.PurchaseInvoiceIds, model.SupplierId);
            ViewData["TransferNumber"] = transfer.TransferNumber;
            return View(model);
        }

        var oldFromWarehouseId = transfer.FromWarehouseId;
        var oldToWarehouseId = transfer.ToWarehouseId;
        var oldProductIds = transfer.Items.Select(i => i.ProductId).ToList();
        string? failure = null;

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            try
            {
                foreach (var item in transfer.Items)
                {
                    item.IsCurrent = false;
                }
                await ReverseTransferBatchesAsync(transfer);
                await stockService.ReverseTransactionsForReferenceAsync(transfer.TransferNumber);
                await db.SaveChangesAsync();

                transfer.FromWarehouseId = model.FromWarehouseId;
                transfer.ToWarehouseId = model.ToWarehouseId;
                transfer.Date = model.Date;
                transfer.Notes = model.Notes;
                SyncPurchaseInvoiceLinks(transfer, model.PurchaseInvoiceIds);
                foreach (var item in model.Items)
                {
                    transfer.Items.Add(new StockTransferItem { ProductId = item.ProductId, Quantity = item.Quantity });
                }

                await MoveStockAsync(transfer);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (InsufficientStockException ex)
            {
                failure = $"Insufficient stock in the source warehouse. {ex.Message}";
            }
            catch (DbUpdateConcurrencyException)
            {
                failure = "The stock changed while saving. Please try again.";
            }

            if (failure is not null)
            {
                await tx.RollbackAsync();
            }
        }

        if (failure is not null)
        {
            db.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, failure);
            await PopulateDropdownsAsync(model.PurchaseInvoiceIds, model.SupplierId);
            ViewData["TransferNumber"] = transfer.TransferNumber;
            return View(model);
        }

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

    private sealed record ApproveResult(bool Ok, bool NotFound, string? Error, string? Number);

    // The one place a pending transfer becomes real: FIFO-allocates each line from the source,
    // gives the destination a same-cost batch for what arrived, and marks the request approved.
    // Nothing is saved unless every line can be covered; on a concurrency clash (another sale or
    // approval touched the same batches/row) it starts over from freshly read rows.
    private async Task<ApproveResult> ApproveCoreAsync(int id, string? reviewNote)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var transfer = await db.StockTransfers
                    .Include(t => t.FromWarehouse)
                    .Include(t => t.ToWarehouse)
                    .Include(t => t.Items.Where(i => i.IsCurrent))
                    .FirstOrDefaultAsync(t => t.Id == id);
                if (transfer is null) return new ApproveResult(false, true, null, null);

                if (transfer.ApprovalStatus != StockTransferApprovalStatus.Pending)
                {
                    return new ApproveResult(false, false,
                        $"{transfer.TransferNumber} has already been {transfer.ApprovalStatus.ToString().ToLowerInvariant()}.", transfer.TransferNumber);
                }

                await MoveStockAsync(transfer);

                transfer.Status = DocumentStatus.Posted;
                transfer.ApprovalStatus = StockTransferApprovalStatus.Approved;
                transfer.ReviewedBy = User.Identity?.Name;
                transfer.ReviewedAt = DateTime.UtcNow;
                transfer.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();

                await db.SaveChangesAsync();

                await notifier.NotifyAsync(
                    "Stock Transfer Posted",
                    $"{transfer.TransferNumber} — {transfer.Items.Count} line(s) moved from {transfer.FromWarehouse?.Name} to {transfer.ToWarehouse?.Name}.",
                    "fas fa-truck-ramp-box text-success",
                    [transfer.FromWarehouseId, transfer.ToWarehouseId]);
                await NotifyStockChangeAsync(transfer.Items.Select(i => i.ProductId), [transfer.FromWarehouseId, transfer.ToWarehouseId]);
                await NotifyTransferDocAsync(transfer, transfer.FromWarehouse?.Name, transfer.ToWarehouse?.Name);

                return new ApproveResult(true, false, null, transfer.TransferNumber);
            }
            catch (InsufficientStockException ex)
            {
                // Stock was sold or moved since the request was filed. Nothing was saved; the
                // request stays pending so the reviewer can reject it.
                db.ChangeTracker.Clear();
                return new ApproveResult(false, false,
                    $"Cannot approve: {ex.Message} The request is still pending — reject it if it is no longer valid.", null);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                if (attempt >= MaxApprovalAttempts)
                {
                    return new ApproveResult(false, false, "The stock changed while approving. Please try again.", null);
                }
            }
        }
    }

    // Moves the transfer's current lines: FIFO-allocates the source batches (oldest first), issues
    // them at the source, and receives a clone at the destination with the same purchase date and
    // prices — so the destination can sell it FIFO at the original cost, and its age is preserved.
    private async Task MoveStockAsync(StockTransfer transfer)
    {
        var batchCount = await db.ProductBatches.CountAsync();
        foreach (var item in transfer.Items.Where(i => i.IsCurrent))
        {
            var allocations = await fifoService.AllocateAsync(item.ProductId, transfer.FromWarehouseId, item.Quantity);
            foreach (var allocation in allocations)
            {
                var source = allocation.Batch;
                var clone = new ProductBatch
                {
                    ProductId = item.ProductId,
                    WarehouseId = transfer.ToWarehouseId,
                    BatchNumber = $"BATCH-{++batchCount:D6}",
                    PurchaseDate = source.PurchaseDate,
                    PurchasePrice = source.PurchasePrice,
                    SalePrice = source.SalePrice,
                    OriginalQuantity = allocation.Quantity,
                    RemainingQuantity = allocation.Quantity,
                    ExpiryDate = source.ExpiryDate
                };
                db.ProductBatches.Add(clone);

                await stockService.IssueStockAsync(item.ProductId, transfer.FromWarehouseId, allocation.Quantity, transfer.TransferNumber,
                    batch: source, unitCost: source.PurchasePrice, unitSalePrice: source.SalePrice, notes: "Transfer out");
                await stockService.ReceiveStockAsync(item.ProductId, transfer.ToWarehouseId, allocation.Quantity, transfer.TransferNumber,
                    batch: clone, unitCost: source.PurchasePrice, unitSalePrice: source.SalePrice, notes: "Transfer in");
            }
        }
    }

    // Undoes the batch side of a batch-backed transfer: batches taken from at the source are
    // topped back up, and the destination clones give their quantity back. Transfers made before
    // batches moved have no batch-linked transactions and are simply skipped. The caller checks
    // ReverseBlockedReasonAsync first and then reverses the aggregate stock as before.
    private async Task ReverseTransferBatchesAsync(StockTransfer transfer)
    {
        var transactions = await db.StockTransactions
            .Include(t => t.Batch)
            .Where(t => t.Reference == transfer.TransferNumber && !t.IsReversed && t.BatchId != null)
            .ToListAsync();

        foreach (var t in transactions)
        {
            t.Batch!.RemainingQuantity += t.Type == StockTransactionType.Out ? t.Quantity : -t.Quantity;
        }
    }

    // Null when reversing is allowed; otherwise the reason to show the user. Reversing means
    // pulling the quantity back out of the destination — blocked if some of it has already moved
    // on from there (sold, or transferred onward), whether judged by warehouse total or by the
    // specific batch that was created for it.
    private async Task<string?> ReverseBlockedReasonAsync(StockTransfer transfer, string verb)
    {
        var productIds = transfer.Items.Select(i => i.ProductId).Distinct().ToList();
        var destStocks = await db.ProductWarehouseStocks
            .Where(s => s.WarehouseId == transfer.ToWarehouseId && productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

        foreach (var item in transfer.Items)
        {
            if (destStocks.GetValueOrDefault(item.ProductId, 0) < item.Quantity)
            {
                var productName = await db.Products.Where(p => p.Id == item.ProductId).Select(p => p.Name).FirstOrDefaultAsync();
                return $"Cannot {verb}: {productName} has already moved out of the destination warehouse. Create a new transfer instead to correct this.";
            }
        }

        var consumed = await db.StockTransactions
            .Where(t => t.Reference == transfer.TransferNumber && !t.IsReversed && t.Type == StockTransactionType.In
                        && t.BatchId != null && t.Batch!.RemainingQuantity < t.Quantity)
            .Select(t => t.Product!.Name)
            .FirstOrDefaultAsync();
        if (consumed is not null)
        {
            return $"Cannot {verb}: some of the {consumed} received by the destination has already been sold or moved on. Create a new transfer instead to correct this.";
        }

        return null;
    }

    private async Task<string?> EditBlockedReasonAsync(StockTransfer transfer)
    {
        if (transfer.ApprovalStatus != StockTransferApprovalStatus.Approved || transfer.Status != DocumentStatus.Posted)
        {
            return "Only a posted transfer can be edited.";
        }

        return await ReverseBlockedReasonAsync(transfer, "edit");
    }

    // FIFO-available stock at the source is what approval will draw on, so that's what a request
    // is checked against (summed per product, so two lines for one product can't jointly overdraw).
    private async Task ValidateStockAsync(StockTransferCreateViewModel model)
    {
        var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        foreach (var group in model.Items.GroupBy(i => i.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var product))
            {
                continue;
            }

            try
            {
                await fifoService.PreviewAsync(group.Key, model.FromWarehouseId, group.Sum(i => i.Quantity));
            }
            catch (InsufficientStockException ex)
            {
                // Don't reveal another warehouse's quantity to a user who isn't assigned to it.
                ModelState.AddModelError(string.Empty, User.IsWarehouseAllowed(model.FromWarehouseId)
                    ? $"Insufficient stock for {product.Name} in the source warehouse: available {ex.Available:0.##}, requested {ex.Requested:0.##}."
                    : $"The source warehouse doesn't have enough {product.Name} for the requested {ex.Requested:0.##}.");
            }
        }
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

        // Split per warehouse by the notifier: the source's users never receive the destination's
        // quantity (or vice versa); only Admin/Manager get every warehouse and the total.
        await notifier.NotifyStockChangeAsync(products.Select(p => new StockChangeProduct(
            p.Id, p.CurrentStock, stocks.Where(s => s.ProductId == p.Id).ToDictionary(s => s.WarehouseId, s => s.Quantity))));
    }

    // fromName/toName are only needed by the client when it has to insert a brand-new row (a
    // freshly requested/posted transfer it doesn't have yet) — later status changes only touch a
    // row already on the page, so they can omit them.
    private async Task NotifyTransferDocAsync(StockTransfer transfer, string? fromName = null, string? toName = null)
    {
        var supplierNames = await db.StockTransferPurchaseInvoices
            .Where(l => l.StockTransferId == transfer.Id)
            .Select(l => l.PurchaseInvoice!.Supplier!.Name)
            .Distinct().ToListAsync();
        await notifier.NotifyDataChangeAsync("StockTransferDoc", new
        {
            id = transfer.Id,
            transferNumber = transfer.TransferNumber,
            fromWarehouseId = transfer.FromWarehouseId,
            fromWarehouseName = fromName,
            toWarehouseId = transfer.ToWarehouseId,
            toWarehouseName = toName,
            date = transfer.Date.ToString("yyyy-MM-dd"),
            dateText = transfer.Date.ToDateTimeText(transfer.CreatedAt),
            status = transfer.DisplayStatus,
            supplierName = string.Join(", ", supplierNames),
            notes = transfer.Notes
        }, [transfer.FromWarehouseId, transfer.ToWarehouseId]);
    }

    // Keeps only purchase invoice ids that exist and that this user could have picked (posted and
    // within their warehouses), plus ones already tagged on the transfer being edited.
    private async Task<List<int>> AllowedPurchaseInvoiceIdsAsync(List<int>? requested, List<int> alreadyLinked)
    {
        var ids = (requested ?? []).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return ids;

        var warehouseIds = User.GetWarehouseIds();
        return await db.PurchaseInvoices
            .Where(p => ids.Contains(p.Id)
                && (alreadyLinked.Contains(p.Id)
                    || (p.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(p.WarehouseId)))))
            .Select(p => p.Id)
            .ToListAsync();
    }

    private static void SyncPurchaseInvoiceLinks(StockTransfer transfer, List<int> invoiceIds)
    {
        foreach (var link in transfer.PurchaseInvoiceLinks.Where(l => !invoiceIds.Contains(l.PurchaseInvoiceId)).ToList())
        {
            transfer.PurchaseInvoiceLinks.Remove(link);
        }
        foreach (var invoiceId in invoiceIds.Where(id => transfer.PurchaseInvoiceLinks.All(l => l.PurchaseInvoiceId != id)))
        {
            transfer.PurchaseInvoiceLinks.Add(new StockTransferPurchaseInvoice { PurchaseInvoiceId = invoiceId });
        }
    }

    // Both From and To Warehouse dropdowns always list every warehouse — including inactive ones,
    // since a transfer is often exactly how remaining stock gets moved OUT of a warehouse that's
    // been deactivated — and regardless of the user's own assignment. This is deliberately unscoped
    // and unfiltered here, unlike every other warehouse-facing dropdown in the app. Stock shown in
    // the picker is FIFO-available (active batch) quantity, the figure a request is checked against.
    // Posted purchase invoices this user may see (same warehouse scoping as the Purchase Invoices
    // list), plus any already tagged on the transfer.
    private IQueryable<PurchaseInvoice> VisibleInvoices(List<int> selectedIds)
    {
        var warehouseIds = User.GetWarehouseIds();
        return db.PurchaseInvoices.Where(p => selectedIds.Contains(p.Id)
            || (p.Status == DocumentStatus.Posted
                && (warehouseIds == null || warehouseIds.Contains(p.WarehouseId))));
    }

    private async Task<List<InvoiceOption>> InvoiceOptionsAsync(int supplierId, List<int> selectedIds)
    {
        var rows = await VisibleInvoices(selectedIds)
            .Where(p => p.SupplierId == supplierId)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new { p.Id, p.InvoiceNumber, p.Date, Warehouse = p.Warehouse!.Name })
            .ToListAsync();
        return rows.Select(p => new InvoiceOption(p.Id, $"{p.InvoiceNumber} — {p.Date:dd MMM yyyy} ({p.Warehouse})")).ToList();
    }

    private sealed record InvoiceOption(int Id, string Text);

    // Feeds the cascading Purchase Invoices dropdown on Create/Edit.
    [HttpGet]
    public async Task<IActionResult> InvoicesBySupplier(int supplierId) =>
        Json(await InvoiceOptionsAsync(supplierId, []));

    private async Task PopulateDropdownsAsync(IReadOnlyCollection<int>? selectedInvoiceIds = null, int? supplierId = null)
    {
        var warehouses = await db.Warehouses.OrderBy(w => w.Name).ToListAsync();
        ViewData["Warehouses"] = new SelectList(warehouses, "Id", "Name");

        // Purchase invoices offered as reference tags: posted ones the user may see (same warehouse
        // scoping as the Purchase Invoices list), plus any already tagged on this transfer even if
        // since cancelled, so an Edit never silently drops an existing tag. No amounts are shown.
        var selectedIds = selectedInvoiceIds?.ToList() ?? [];
        var visible = VisibleInvoices(selectedIds);

        // Supplier dropdown drives the invoice dropdown (cascading). Fall back to the supplier of an
        // already-tagged invoice so Edit and failed postbacks reopen with both filled in.
        supplierId ??= selectedIds.Count == 0 ? null
            : await db.PurchaseInvoices.Where(p => selectedIds.Contains(p.Id)).OrderBy(p => p.Id).Select(p => (int?)p.SupplierId).FirstOrDefaultAsync();
        ViewData["SupplierId"] = supplierId;
        var suppliers = await visible.Select(p => new { p.SupplierId, p.Supplier!.Name }).Distinct().OrderBy(s => s.Name).ToListAsync();
        ViewData["Suppliers"] = new SelectList(suppliers, "SupplierId", "Name", supplierId);

        var invoices = supplierId is null ? new List<InvoiceOption>() : await InvoiceOptionsAsync(supplierId.Value, selectedIds);
        ViewData["PurchaseInvoices"] = new MultiSelectList(invoices, "Id", "Text", selectedIds);

        // Any warehouse can be picked, but a restricted user only receives stock figures for their
        // own warehouse(s); the page shows "—" (not a number) for any other source warehouse.
        var userWarehouseIds = User.GetWarehouseIds();
        ViewData["StockVisibleWarehouseIds"] = userWarehouseIds;
        var batchTotals = await db.ProductBatches
            .Where(b => b.IsActive && (userWarehouseIds == null || userWarehouseIds.Contains(b.WarehouseId)))
            .GroupBy(b => new { b.ProductId, b.WarehouseId })
            .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Quantity = g.Sum(b => b.RemainingQuantity) })
            .ToListAsync();
        var byProduct = batchTotals.ToLookup(b => b.ProductId);

        var products = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Brand, p.Size, Category = p.Category!.Name, p.UnitOfMeasure!.Symbol })
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
            Stocks = byProduct[p.Id].Select(s => new { s.WarehouseId, s.Quantity })
        }).ToList();
    }
}
