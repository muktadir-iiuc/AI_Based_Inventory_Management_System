using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
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
        var scopedWarehouseId = User.GetWarehouseId();
        var query = db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Warehouse).AsQueryable();

        if (scopedWarehouseId.HasValue)
        {
            query = query.Where(p => p.WarehouseId == scopedWarehouseId);
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
        ViewData["WarehouseScoped"] = scopedWarehouseId.HasValue;

        return View(await query.Include(p => p.Items).OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToListAsync());
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
        return View(invoice);
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        var scopedWarehouseId = User.GetWarehouseId();
        return View(new PurchaseInvoiceCreateViewModel
        {
            Items = [new InvoiceLineInput()],
            WarehouseId = scopedWarehouseId ?? 0
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

        // A warehouse-scoped user can only post to their own warehouse, regardless of what was submitted.
        var scopedWarehouseId = User.GetWarehouseId();
        if (scopedWarehouseId.HasValue)
        {
            model.WarehouseId = scopedWarehouseId.Value;
        }

        if (model.WarehouseId <= 0 || !await db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId))
        {
            ModelState.AddModelError(nameof(model.WarehouseId), "Please select a warehouse.");
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
                UnitPrice = item.UnitPrice
            });
        }

        db.PurchaseInvoices.Add(invoice);

        foreach (var item in invoice.Items)
        {
            await stockService.ReceiveStockAsync(item.ProductId, invoice.WarehouseId, item.Quantity, invoice.InvoiceNumber);

            var product = await db.Products.FindAsync(item.ProductId);
            if (product is not null)
            {
                product.CostPrice = item.UnitPrice; // track latest purchase cost
            }
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
        var invoice = await db.PurchaseInvoices.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
        if (invoice is null) return NotFound();

        if (invoice.Status == DocumentStatus.Cancelled)
        {
            TempData["Error"] = "This invoice is already cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await stockService.ReverseTransactionsForReferenceAsync(invoice.InvoiceNumber);
        await accountingService.ReverseJournalEntriesForReferenceAsync(invoice.InvoiceNumber, "Purchase invoice cancelled");
        invoice.Status = DocumentStatus.Cancelled;

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
        var scopedWarehouseId = User.GetWarehouseId();
        var warehouses = db.Warehouses.Where(w => w.IsActive).AsQueryable();
        if (scopedWarehouseId.HasValue)
        {
            warehouses = warehouses.Where(w => w.Id == scopedWarehouseId);
        }

        ViewData["Suppliers"] = new SelectList(await db.Suppliers.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
        ViewData["Warehouses"] = new SelectList(await warehouses.OrderBy(w => w.Name).ToListAsync(), "Id", "Name", scopedWarehouseId);
        ViewData["WarehouseScoped"] = scopedWarehouseId.HasValue;
        ViewData["Products"] = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Sku, p.Name, p.CostPrice, p.UnitOfMeasure!.Symbol }).ToListAsync();
    }
}
