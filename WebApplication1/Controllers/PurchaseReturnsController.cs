using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class PurchaseReturnsController(
    ApplicationDbContext db,
    IStockService stockService,
    IAccountingService accountingService,
    IActivityNotifier notifier) : Controller
{
    public async Task<IActionResult> Index()
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.PurchaseReturns.Include(r => r.PurchaseInvoice).ThenInclude(i => i!.Supplier).AsQueryable();

        if (warehouseIds is not null)
        {
            query = query.Where(r => warehouseIds.Contains(r.PurchaseInvoice!.WarehouseId));
        }

        return View(await query.Include(r => r.Items).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var purchaseReturn = await db.PurchaseReturns
            .Include(r => r.PurchaseInvoice).ThenInclude(i => i!.Supplier)
            .Include(r => r.Items).ThenInclude(i => i.PurchaseInvoiceItem).ThenInclude(i => i!.Product)
            .Include(r => r.Items).ThenInclude(i => i.PurchaseInvoiceItem).ThenInclude(i => i!.Batch)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (purchaseReturn is null) return NotFound();
        if (!User.IsWarehouseAllowed(purchaseReturn.PurchaseInvoice!.WarehouseId)) return Forbid();
        return View(purchaseReturn);
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Create(int purchaseInvoiceId)
    {
        var invoice = await db.PurchaseInvoices
            .Include(i => i.Items).ThenInclude(i => i.Product)
            .Include(i => i.Items).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(i => i.Id == purchaseInvoiceId);

        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();
        if (invoice.Status != DocumentStatus.Posted)
        {
            TempData["Error"] = "Only posted invoices can be returned against.";
            return RedirectToAction("Details", "PurchaseInvoices", new { id = purchaseInvoiceId });
        }

        return View(BuildCreateViewModel(invoice));
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseReturnCreateViewModel model)
    {
        var invoice = await db.PurchaseInvoices
            .Include(i => i.Items).ThenInclude(i => i.Product)
            .Include(i => i.Items).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(i => i.Id == model.PurchaseInvoiceId);

        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();
        if (invoice.Status != DocumentStatus.Posted)
        {
            TempData["Error"] = "Only posted invoices can be returned against.";
            return RedirectToAction("Details", "PurchaseInvoices", new { id = model.PurchaseInvoiceId });
        }

        var requestedLines = model.Items.Where(i => i.ReturnQuantity > 0).ToList();
        if (requestedLines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Enter a return quantity for at least one line.");
        }

        foreach (var line in requestedLines)
        {
            var item = invoice.Items.FirstOrDefault(i => i.Id == line.PurchaseInvoiceItemId);
            if (item?.Batch is null)
            {
                ModelState.AddModelError(string.Empty, "Invalid invoice line.");
                continue;
            }

            // Already-sold stock can't be returned — the ceiling is what's still on hand for
            // this batch right now, not the originally purchased quantity.
            if (line.ReturnQuantity > item.Batch.RemainingQuantity)
            {
                ModelState.AddModelError(string.Empty,
                    $"{item.Product?.Name}: cannot return {line.ReturnQuantity:0.##} — only {item.Batch.RemainingQuantity:0.##} of batch {item.Batch.BatchNumber} is still in stock (the rest has been sold).");
            }
        }

        if (!ModelState.IsValid)
        {
            var refreshed = BuildCreateViewModel(invoice);
            refreshed.Date = model.Date;
            refreshed.Notes = model.Notes;
            return View(refreshed);
        }

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var returnCount = await db.PurchaseReturns.CountAsync();
            var purchaseReturn = new PurchaseReturn
            {
                ReturnNumber = $"PRET-{returnCount + 1:D6}",
                PurchaseInvoiceId = invoice.Id,
                Date = model.Date,
                Notes = model.Notes,
                CreatedBy = User.Identity?.Name
            };

            foreach (var line in requestedLines)
            {
                var item = invoice.Items.First(i => i.Id == line.PurchaseInvoiceItemId);

                purchaseReturn.Items.Add(new PurchaseReturnItem
                {
                    PurchaseInvoiceItemId = item.Id,
                    Quantity = line.ReturnQuantity,
                    UnitCost = item.UnitPrice
                });

                item.Batch!.RemainingQuantity -= line.ReturnQuantity;

                await stockService.IssueStockAsync(item.ProductId, invoice.WarehouseId, line.ReturnQuantity, purchaseReturn.ReturnNumber,
                    batch: item.Batch, unitCost: item.UnitPrice, unitSalePrice: item.Batch.SalePrice, notes: "Purchase return");
            }

            db.PurchaseReturns.Add(purchaseReturn);
            await accountingService.PostPurchaseReturnAsync(purchaseReturn);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                foreach (var entry in db.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }
                continue;
            }

            await notifier.NotifyAsync(
                "Purchase Return Posted",
                $"{purchaseReturn.ReturnNumber} against {invoice.InvoiceNumber} ({purchaseReturn.TotalAmount:C})",
                "fas fa-rotate-left text-warning",
                [invoice.WarehouseId]);

            TempData["Success"] = $"Purchase return {purchaseReturn.ReturnNumber} posted.";
            return RedirectToAction(nameof(Details), new { id = purchaseReturn.Id });
        }
    }

    private static PurchaseReturnCreateViewModel BuildCreateViewModel(PurchaseInvoice invoice) => new()
    {
        PurchaseInvoiceId = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        Items = invoice.Items.Where(i => i.Batch is not null && i.Batch.RemainingQuantity > 0).Select(i => new PurchaseReturnLineInput
        {
            PurchaseInvoiceItemId = i.Id,
            ProductName = i.Product?.Name ?? string.Empty,
            BatchNumber = i.Batch?.BatchNumber,
            MaxReturnable = i.Batch!.RemainingQuantity,
            UnitCost = i.UnitPrice
        }).ToList()
    };
}
