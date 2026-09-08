using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class SalesReturnsController(
    ApplicationDbContext db,
    IStockService stockService,
    IAccountingService accountingService,
    IActivityNotifier notifier) : Controller
{
    public async Task<IActionResult> Index(int page = 1)
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.SalesReturns.Include(r => r.SalesInvoice).ThenInclude(i => i!.Customer).AsQueryable();

        if (warehouseIds is not null)
        {
            query = query.Where(r => warehouseIds.Contains(r.SalesInvoice!.WarehouseId));
        }

        return View(await PagedList<SalesReturn>.CreateAsync(
            query.Include(r => r.Items).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id), page));
    }

    public async Task<IActionResult> Details(int id)
    {
        var salesReturn = await db.SalesReturns
            .Include(r => r.SalesInvoice).ThenInclude(i => i!.Customer)
            .Include(r => r.Items).ThenInclude(i => i.SalesInvoiceItem).ThenInclude(i => i!.Product)
            .Include(r => r.Items).ThenInclude(i => i.SalesInvoiceItem).ThenInclude(i => i!.Batch)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (salesReturn is null) return NotFound();
        if (!User.IsWarehouseAllowed(salesReturn.SalesInvoice!.WarehouseId)) return Forbid();
        return View(salesReturn);
    }

    [Authorize(Roles = Roles.SalesManagers)]
    public async Task<IActionResult> Create(int salesInvoiceId)
    {
        var invoice = await db.SalesInvoices
            .Include(i => i.Items).ThenInclude(i => i.Product)
            .Include(i => i.Items).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(i => i.Id == salesInvoiceId);

        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();
        if (invoice.Status != DocumentStatus.Posted)
        {
            TempData["Error"] = "Only posted invoices can be returned against.";
            return RedirectToAction("Details", "SalesInvoices", new { id = salesInvoiceId });
        }

        var model = await BuildCreateViewModelAsync(invoice);
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SalesReturnCreateViewModel model)
    {
        var invoice = await db.SalesInvoices
            .Include(i => i.Items).ThenInclude(i => i.Product)
            .Include(i => i.Items).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(i => i.Id == model.SalesInvoiceId);

        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();
        if (invoice.Status != DocumentStatus.Posted)
        {
            TempData["Error"] = "Only posted invoices can be returned against.";
            return RedirectToAction("Details", "SalesInvoices", new { id = model.SalesInvoiceId });
        }

        var alreadyReturned = await GetAlreadyReturnedQuantitiesAsync(invoice.Id);
        var requestedLines = model.Items.Where(i => i.ReturnQuantity > 0).ToList();
        if (requestedLines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Enter a return quantity for at least one line.");
        }

        foreach (var line in requestedLines)
        {
            var item = invoice.Items.FirstOrDefault(i => i.Id == line.SalesInvoiceItemId);
            if (item is null)
            {
                ModelState.AddModelError(string.Empty, "Invalid invoice line.");
                continue;
            }

            var maxReturnable = item.Quantity - alreadyReturned.GetValueOrDefault(item.Id, 0);
            if (line.ReturnQuantity > maxReturnable)
            {
                ModelState.AddModelError(string.Empty,
                    $"{item.Product?.Name}: cannot return {line.ReturnQuantity:0.##} — only {maxReturnable:0.##} remains returnable on this line.");
            }
        }

        if (!ModelState.IsValid)
        {
            var refreshed = await BuildCreateViewModelAsync(invoice);
            refreshed.Date = model.Date;
            refreshed.Notes = model.Notes;
            return View(refreshed);
        }

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var returnCount = await db.SalesReturns.CountAsync();
            var salesReturn = new SalesReturn
            {
                ReturnNumber = $"SRET-{returnCount + 1:D6}",
                SalesInvoiceId = invoice.Id,
                Date = model.Date,
                Notes = model.Notes,
                CreatedBy = User.Identity?.Name
            };

            foreach (var line in requestedLines)
            {
                var item = invoice.Items.First(i => i.Id == line.SalesInvoiceItemId);

                salesReturn.Items.Add(new SalesReturnItem
                {
                    SalesInvoiceItemId = item.Id,
                    Quantity = line.ReturnQuantity,
                    UnitPrice = item.UnitPrice,
                    UnitCost = item.UnitCost
                });

                if (item.Batch is not null)
                {
                    item.Batch.RemainingQuantity += line.ReturnQuantity;
                }

                await stockService.ReceiveStockAsync(item.ProductId, invoice.WarehouseId, line.ReturnQuantity, salesReturn.ReturnNumber,
                    batch: item.Batch, unitCost: item.UnitCost, unitSalePrice: item.UnitPrice, notes: "Sales return");
            }

            db.SalesReturns.Add(salesReturn);
            await accountingService.PostSalesReturnAsync(salesReturn);

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
                "Sales Return Posted",
                $"{salesReturn.ReturnNumber} against {invoice.InvoiceNumber} ({salesReturn.TotalAmount:C})",
                "fas fa-rotate-left text-warning",
                [invoice.WarehouseId]);

            TempData["Success"] = $"Sales return {salesReturn.ReturnNumber} posted.";
            return RedirectToAction(nameof(Details), new { id = salesReturn.Id });
        }
    }

    private async Task<Dictionary<int, decimal>> GetAlreadyReturnedQuantitiesAsync(int salesInvoiceId)
    {
        return await db.SalesReturnItems
            .Where(i => i.SalesInvoiceItem!.SalesInvoiceId == salesInvoiceId)
            .GroupBy(i => i.SalesInvoiceItemId)
            .Select(g => new { SalesInvoiceItemId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.SalesInvoiceItemId, x => x.Quantity);
    }

    private async Task<SalesReturnCreateViewModel> BuildCreateViewModelAsync(SalesInvoice invoice)
    {
        var alreadyReturned = await GetAlreadyReturnedQuantitiesAsync(invoice.Id);

        return new SalesReturnCreateViewModel
        {
            SalesInvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            Items = invoice.Items.Select(i => new SalesReturnLineInput
            {
                SalesInvoiceItemId = i.Id,
                ProductName = i.Product?.Name ?? string.Empty,
                BatchNumber = i.Batch?.BatchNumber,
                MaxReturnable = i.Quantity - alreadyReturned.GetValueOrDefault(i.Id, 0),
                UnitPrice = i.UnitPrice,
                UnitCost = i.UnitCost
            }).Where(l => l.MaxReturnable > 0).ToList()
        };
    }
}
