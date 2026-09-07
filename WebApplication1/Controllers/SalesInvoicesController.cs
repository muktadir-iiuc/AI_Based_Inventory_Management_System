using AspNetCore.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class SalesInvoicesController(
    ApplicationDbContext db,
    IStockService stockService,
    IAccountingService accountingService,
    IActivityNotifier notifier,
    ICompanySettingsService companySettings,
    IWebHostEnvironment env) : Controller
{
    public async Task<IActionResult> Index(int? customerId, int? warehouseId)
    {
        var scopedWarehouseId = User.GetWarehouseId();
        var query = db.SalesInvoices.Include(s => s.Customer).Include(s => s.Warehouse).AsQueryable();

        if (scopedWarehouseId.HasValue)
        {
            query = query.Where(s => s.WarehouseId == scopedWarehouseId);
        }
        else if (warehouseId.HasValue)
        {
            query = query.Where(s => s.WarehouseId == warehouseId);
        }

        if (customerId.HasValue)
        {
            query = query.Where(s => s.CustomerId == customerId);
        }

        ViewData["CustomerId"] = new SelectList(await db.Customers.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", customerId);
        ViewData["WarehouseId"] = new SelectList(await db.Warehouses.OrderBy(w => w.Name).ToListAsync(), "Id", "Name", warehouseId);
        ViewData["WarehouseScoped"] = scopedWarehouseId.HasValue;

        return View(await query.Include(s => s.Items).OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var invoice = await db.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (invoice is null) return NotFound();
        return View(invoice);
    }

    [Authorize(Roles = Roles.SalesManagers)]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        var scopedWarehouseId = User.GetWarehouseId();
        return View(new SalesInvoiceCreateViewModel
        {
            Items = [new InvoiceLineInput()],
            WarehouseId = scopedWarehouseId ?? 0
        });
    }

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SalesInvoiceCreateViewModel model)
    {
        model.Items = model.Items.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList();
        if (model.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one product line.");
        }

        // A warehouse-scoped user can only sell from their own warehouse, regardless of what was submitted.
        var scopedWarehouseId = User.GetWarehouseId();
        if (scopedWarehouseId.HasValue)
        {
            model.WarehouseId = scopedWarehouseId.Value;
        }

        if (model.WarehouseId <= 0 || !await db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId))
        {
            ModelState.AddModelError(nameof(model.WarehouseId), "Please select a warehouse.");
        }

        var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        if (model.WarehouseId > 0)
        {
            var warehouseStocks = await db.ProductWarehouseStocks
                .Where(s => s.WarehouseId == model.WarehouseId && productIds.Contains(s.ProductId))
                .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

            foreach (var item in model.Items)
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                {
                    continue;
                }

                var availableInWarehouse = warehouseStocks.GetValueOrDefault(item.ProductId, 0);
                if (item.Quantity > availableInWarehouse)
                {
                    ModelState.AddModelError(string.Empty,
                        $"Insufficient stock for {product.Name} in the selected warehouse: available {availableInWarehouse:0.##}, requested {item.Quantity:0.##}.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var invoiceCount = await db.SalesInvoices.CountAsync();
        var invoice = new SalesInvoice
        {
            InvoiceNumber = $"SINV-{invoiceCount + 1:D6}",
            CustomerId = model.CustomerId,
            WarehouseId = model.WarehouseId,
            Date = model.Date,
            Notes = model.Notes,
            Status = DocumentStatus.Posted,
            CreatedBy = User.Identity?.Name
        };

        foreach (var item in model.Items)
        {
            invoice.Items.Add(new SalesInvoiceItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                UnitCost = products[item.ProductId].CostPrice
            });
        }

        db.SalesInvoices.Add(invoice);

        foreach (var item in invoice.Items)
        {
            await stockService.IssueStockAsync(item.ProductId, invoice.WarehouseId, item.Quantity, invoice.InvoiceNumber);
        }

        await accountingService.PostSalesInvoiceAsync(invoice);
        await db.SaveChangesAsync();

        var warehouseName = (await db.Warehouses.FindAsync(invoice.WarehouseId))?.Name;
        await notifier.NotifyAsync(
            "Sales Invoice Posted",
            $"{invoice.InvoiceNumber} — {invoice.Items.Count} line(s) at {warehouseName} ({invoice.TotalAmount:C})",
            "fas fa-cash-register text-success",
            [invoice.WarehouseId]);

        TempData["Success"] = $"Sales invoice {invoice.InvoiceNumber} posted.";
        return RedirectToAction(nameof(Details), new { id = invoice.Id });
    }

    public async Task<IActionResult> PrintPdf(int id)
    {
        var invoice = await db.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.UnitOfMeasure)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (invoice is null) return NotFound();

        var lines = invoice.Items.Select((item, index) => new SalesInvoiceReportLine
        {
            SL = (index + 1).ToString(),
            ProductName = item.Product?.Name ?? string.Empty,
            Sku = item.Product?.Sku ?? string.Empty,
            Quantity = $"{item.Quantity:0.##} {item.Product?.UnitOfMeasure?.Symbol}".Trim(),
            UnitPrice = item.UnitPrice.ToString("N2"),
            LineTotal = item.LineTotal.ToString("N2")
        }).ToList();

        var paidAmount = invoice.Payments.Sum(p => p.Amount);
        var dueAmount = invoice.TotalAmount - paidAmount;

        var company = await companySettings.GetAsync();

        var header = new SalesInvoiceReportHeader
        {
            CompanyName = company.CompanyName,
            CompanyAddress = company.Address ?? string.Empty,
            CompanyPhone = string.IsNullOrWhiteSpace(company.Phone) ? string.Empty : $"Phone: {company.Phone}",
            CompanyLogo = company.LogoImage ?? SalesInvoiceReportHeader.TransparentPixel,
            CompanyLogoMimeType = company.LogoImage is null ? "image/png" : company.LogoMimeType ?? "image/png",
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.Date.ToString("MMM dd, yyyy"),
            Status = invoice.Status.ToString(),
            CustomerName = invoice.Customer?.Name ?? string.Empty,
            CustomerAddress = invoice.Customer?.Address ?? string.Empty,
            CustomerPhone = invoice.Customer?.Phone ?? string.Empty,
            WarehouseName = invoice.Warehouse?.Name ?? string.Empty,
            Notes = invoice.Notes ?? string.Empty,
            TotalAmount = invoice.TotalAmount.ToString("N2"),
            PaidAmount = paidAmount.ToString("N2"),
            DueAmount = dueAmount.ToString("N2")
        };

        var reportPath = Path.Combine(env.ContentRootPath, "Reports", "SalesInvoiceReport.rdlc");
        var report = new LocalReport(reportPath);
        report.AddDataSource("InvoiceHeader", new List<SalesInvoiceReportHeader> { header });
        report.AddDataSource("InvoiceItems", lines);

        var result = report.Execute(RenderType.Pdf, 1, null, string.Empty);
        return File(result.MainStream, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }

    public async Task<IActionResult> PrintThermal(int id)
    {
        var invoice = await db.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.UnitOfMeasure)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (invoice is null) return NotFound();

        var itemLines = invoice.Items.Select((item, index) => new SalesInvoiceReportLine
        {
            SL = (index + 1).ToString(),
            ProductName = item.Product?.Name ?? string.Empty,
            Sku = item.Product?.Sku ?? string.Empty,
            Quantity = $"{item.Quantity:0.##} {item.Product?.UnitOfMeasure?.Symbol}".Trim(),
            UnitPrice = item.UnitPrice.ToString("N2"),
            LineTotal = item.LineTotal.ToString("N2")
        }).ToList();

        var paidAmount = invoice.Payments.Sum(p => p.Amount);
        var dueAmount = invoice.TotalAmount - paidAmount;

        var company = await companySettings.GetAsync();

        var header = new SalesInvoiceReportHeader
        {
            CompanyName = company.CompanyName,
            CompanyAddress = company.Address ?? string.Empty,
            CompanyPhone = string.IsNullOrWhiteSpace(company.Phone) ? string.Empty : $"Phone: {company.Phone}",
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.Date.ToString("MMM dd, yyyy"),
            CustomerName = invoice.Customer?.Name ?? string.Empty,
            CustomerAddress = invoice.Customer?.Address ?? string.Empty,
            CustomerPhone = invoice.Customer?.Phone ?? string.Empty,
            WarehouseName = invoice.Warehouse?.Name ?? string.Empty,
            ServedBy = string.IsNullOrWhiteSpace(invoice.CreatedBy) ? "N/A" : invoice.CreatedBy,
            Notes = invoice.Notes ?? string.Empty,
            TotalAmount = invoice.TotalAmount.ToString("N2"),
            PaidAmount = paidAmount.ToString("N2"),
            DueAmount = dueAmount.ToString("N2")
        };

        var detailLines = ThermalReceiptFormatter.BuildDetailLines(header, itemLines);

        // The RDLC's fixed-position sections (company header, invoice meta, totals,
        // footer) have known heights, but the receipt/detail-lines block in between
        // grows with the invoice, so every section after it is positioned here via
        // template placeholders rather than fixed coordinates in the .rdlc.
        const double dynamicSectionTop = 1.78;
        const double dynamicRowHeightDesign = 0.16; // cosmetic only, for the Tablix's own declared <Height>
        const double dynamicRowAllowance = 0.34; // visual layout only — see pageHeight comment for the real per-row cost
        const double totalsHeight = 0.66; // Total + Paid + Balance Due rows, fixed count
        const double bottomMargin = 0.08;
        const double maxPageHeightIn = 60;

        var dynamicHeight = detailLines.Count * dynamicRowHeightDesign;
        var dynamicEnd = dynamicSectionTop + detailLines.Count * dynamicRowAllowance;
        var divider3Top = dynamicEnd + 0.04;
        var totalsTop = dynamicEnd + 0.10;
        var totalsEnd = totalsTop + totalsHeight;
        var divider4Top = totalsEnd + 0.04;
        var footerTop = divider4Top + 0.08;
        var footerNoteTop = footerTop + 0.22;
        var footerEnd = footerNoteTop + 0.16;
        var bodyHeight = footerEnd;

        // AspNetCore.Reporting's PDF pagination doesn't honor each item's declared
        // <Top>/<Height> for page-break decisions — it appears to walk the body's
        // report items in document order, accumulating each one's *actual* required
        // height (consistently more than its declared height), and once that running
        // total exceeds the page's printable height everything remaining spills to a
        // second page. Empirically fit against two calibration points (4 detail lines
        // -> ~5.2in required, 30 detail lines -> ~17.95in required): required(n) =
        // 3.24in + 0.49in/line. The constants below add a ~15% safety margin on top.
        var pageHeight = Math.Min(3.6 + detailLines.Count * 0.55 + bottomMargin + 0.2, maxPageHeightIn);

        var templatePath = Path.Combine(env.ContentRootPath, "Reports", "SalesInvoiceThermalReceipt.rdlc");
        var rdlc = await System.IO.File.ReadAllTextAsync(templatePath);
        rdlc = rdlc
            .Replace("__DYNAMICHEIGHT__", dynamicHeight.ToString("0.00"))
            .Replace("__DIVIDER3_TOP__", divider3Top.ToString("0.00"))
            .Replace("__TOTALS_TOP__", totalsTop.ToString("0.00"))
            .Replace("__FOOTER_NOTE_TOP__", footerNoteTop.ToString("0.00"))
            .Replace("__FOOTER_TOP__", footerTop.ToString("0.00"))
            .Replace("__DIVIDER4_TOP__", divider4Top.ToString("0.00"))
            .Replace("__BODYHEIGHT__", bodyHeight.ToString("0.00"))
            .Replace("__PAGEHEIGHT__", pageHeight.ToString("0.00"));

        var tempPath = Path.Combine(Path.GetTempPath(), $"thermal-receipt-{Guid.NewGuid():N}.rdlc");
        try
        {
            await System.IO.File.WriteAllTextAsync(tempPath, rdlc);

            var report = new LocalReport(tempPath);
            report.AddDataSource("InvoiceHeader", new List<SalesInvoiceReportHeader> { header });
            report.AddDataSource("ReceiptLines", detailLines);

            var result = report.Execute(RenderType.Pdf, 1, null, string.Empty);
            return File(result.MainStream, "application/pdf", $"{invoice.InvoiceNumber}-receipt.pdf");
        }
        finally
        {
            System.IO.File.Delete(tempPath);
        }
    }

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var invoice = await db.SalesInvoices.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
        if (invoice is null) return NotFound();

        if (invoice.Status == DocumentStatus.Cancelled)
        {
            TempData["Error"] = "This invoice is already cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await stockService.ReverseTransactionsForReferenceAsync(invoice.InvoiceNumber);
        await accountingService.ReverseJournalEntriesForReferenceAsync(invoice.InvoiceNumber, "Sales invoice cancelled");
        invoice.Status = DocumentStatus.Cancelled;

        await db.SaveChangesAsync();

        await notifier.NotifyAsync(
            "Sales Invoice Cancelled",
            $"{invoice.InvoiceNumber} was cancelled and reversed.",
            "fas fa-ban text-danger",
            [invoice.WarehouseId]);

        TempData["Success"] = $"Sales invoice {invoice.InvoiceNumber} cancelled and reversed.";
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

        ViewData["Customers"] = new SelectList(await db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewData["Warehouses"] = new SelectList(await warehouses.OrderBy(w => w.Name).ToListAsync(), "Id", "Name", scopedWarehouseId);
        ViewData["WarehouseScoped"] = scopedWarehouseId.HasValue;

        // When the user is scoped to one warehouse, show that warehouse's actual stock in the picker;
        // otherwise fall back to the global total as a guide (the server re-validates against the chosen warehouse on submit).
        if (scopedWarehouseId.HasValue)
        {
            ViewData["Products"] = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Sku,
                    p.Name,
                    p.SalePrice,
                    CurrentStock = p.WarehouseStocks.Where(s => s.WarehouseId == scopedWarehouseId).Select(s => s.Quantity).FirstOrDefault(),
                    p.UnitOfMeasure!.Symbol
                }).ToListAsync();
        }
        else
        {
            ViewData["Products"] = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Sku, p.Name, p.SalePrice, p.CurrentStock, p.UnitOfMeasure!.Symbol }).ToListAsync();
        }
    }
}
