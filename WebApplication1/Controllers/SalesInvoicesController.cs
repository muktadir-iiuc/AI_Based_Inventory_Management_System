using AspNetCore.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class SalesInvoicesController(
    ApplicationDbContext db,
    IStockService stockService,
    IFifoAllocationService fifoService,
    IAccountingService accountingService,
    IActivityNotifier notifier,
    ICompanySettingsService companySettings,
    IWebHostEnvironment env) : Controller
{
    public async Task<IActionResult> Index(int? customerId, int? warehouseId)
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.SalesInvoices.Include(s => s.Customer).Include(s => s.Warehouse).AsQueryable();

        if (warehouseIds is not null)
        {
            query = query.Where(s => warehouseIds.Contains(s.WarehouseId));
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
        ViewData["WarehouseScoped"] = warehouseIds is not null;

        return View(await query.Include(s => s.Items).OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var invoice = await db.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Include(s => s.Items).ThenInclude(i => i.Batch)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();
        return View(invoice);
    }

    [Authorize(Roles = Roles.SalesManagers)]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        return View(new SalesInvoiceCreateViewModel
        {
            Items = [new SalesLineInput()],
            WarehouseId = (int)(ViewData["DefaultWarehouseId"] ?? 0)
        });
    }

    // Read-only: computes (but does not apply) the FIFO batch breakdown for a requested
    // quantity, so the Create page can show the salesperson exactly which batches/prices a
    // line will draw from before they submit. The server always decides FIFO order — this
    // endpoint never lets the client pick a batch, it only previews what the server would do.
    [HttpGet]
    [Authorize(Roles = Roles.SalesManagers)]
    public async Task<IActionResult> PreviewAllocation(int productId, int warehouseId, decimal quantity)
    {
        if (productId <= 0 || warehouseId <= 0 || quantity <= 0 || !User.IsWarehouseAllowed(warehouseId))
        {
            return Json(new { ok = false });
        }

        try
        {
            var allocations = await fifoService.PreviewAsync(productId, warehouseId, quantity);
            return Json(new
            {
                ok = true,
                lines = allocations.Select(a => new
                {
                    a.Batch.BatchNumber,
                    Available = a.Batch.RemainingQuantity,
                    Allocated = a.Quantity,
                    PurchasePrice = a.Batch.PurchasePrice,
                    SalePrice = a.Batch.SalePrice
                }),
                unitPrice = allocations.Count > 0 ? allocations[0].Batch.SalePrice : 0,
                lineTotal = allocations.Sum(a => a.Quantity * a.Batch.SalePrice)
            });
        }
        catch (InsufficientStockException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
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

        if (model.WarehouseId <= 0 || !await db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId))
        {
            ModelState.AddModelError(nameof(model.WarehouseId), "Please select a warehouse.");
        }
        else if (!User.IsWarehouseAllowed(model.WarehouseId))
        {
            ModelState.AddModelError(nameof(model.WarehouseId), "You are not assigned to this warehouse.");
        }

        var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        if (model.WarehouseId > 0 && ModelState.IsValid)
        {
            // Read-only check up front: reject the whole submission together (no partial sale)
            // if any line can't be fully covered by currently available batches. Prices/batches
            // are determined for real — and re-validated — inside the save-and-retry loop below,
            // since stock can change between this check and the actual allocation.
            foreach (var item in model.Items)
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                {
                    continue;
                }

                try
                {
                    await fifoService.PreviewAsync(item.ProductId, model.WarehouseId, item.Quantity);
                }
                catch (InsufficientStockException ex)
                {
                    ModelState.AddModelError(string.Empty, $"{product.Name}: {ex.Message}");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        // FIFO allocation mutates ProductBatch.RemainingQuantity on tracked entities; two
        // concurrent sales racing for the same batch make the loser's SaveChangesAsync throw
        // DbUpdateConcurrencyException (ProductBatch.RowVersion). Retry against freshly-loaded
        // batches rather than fail the sale outright — see FifoAllocationService remarks.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
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
                List<BatchAllocation> allocations;
                try
                {
                    allocations = await fifoService.AllocateAsync(item.ProductId, model.WarehouseId, item.Quantity);
                }
                catch (InsufficientStockException ex)
                {
                    ModelState.AddModelError(string.Empty, $"{products[item.ProductId].Name}: {ex.Message}");
                    continue;
                }

                // A manually-entered price on the line applies to every batch this line draws
                // from (the salesperson sets one price for the quantity they're selling); with
                // no override, each allocation keeps falling back to its own batch's SalePrice.
                foreach (var allocation in allocations)
                {
                    var unitPrice = item.UnitPrice is > 0 ? item.UnitPrice.Value : allocation.Batch.SalePrice;

                    invoice.Items.Add(new SalesInvoiceItem
                    {
                        ProductId = item.ProductId,
                        Quantity = allocation.Quantity,
                        UnitPrice = unitPrice,
                        UnitCost = allocation.Batch.PurchasePrice,
                        Batch = allocation.Batch
                    });

                    await stockService.IssueStockAsync(item.ProductId, model.WarehouseId, allocation.Quantity, invoice.InvoiceNumber,
                        batch: allocation.Batch, unitCost: allocation.Batch.PurchasePrice, unitSalePrice: unitPrice);
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(model);
            }

            db.SalesInvoices.Add(invoice);
            await accountingService.PostSalesInvoiceAsync(invoice);

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

            var warehouseName = (await db.Warehouses.FindAsync(invoice.WarehouseId))?.Name;
            await notifier.NotifyAsync(
                "Sales Invoice Posted",
                $"{invoice.InvoiceNumber} — {invoice.Items.Count} line(s) at {warehouseName} ({invoice.TotalAmount:C})",
                "fas fa-cash-register text-success",
                [invoice.WarehouseId]);

            TempData["Success"] = $"Sales invoice {invoice.InvoiceNumber} posted.";
            return RedirectToAction(nameof(Details), new { id = invoice.Id });
        }
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
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();

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
        var invoice = await db.SalesInvoices.Where(s => s.Id == id)
            .Select(s => new { s.InvoiceNumber, s.WarehouseId }).FirstOrDefaultAsync();
        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();

        ViewData["Title"] = $"Receipt Preview — {invoice.InvoiceNumber}";
        return View(new ThermalReceiptPreviewViewModel(id, invoice.InvoiceNumber));
    }

    public async Task<IActionResult> ThermalReceiptPdf(int id)
    {
        var pdf = await GenerateThermalReceiptPdfAsync(id);
        if (pdf is null) return NotFound();

        return File(pdf.Value.Bytes, "application/pdf");
    }

    public async Task<IActionResult> ThermalReceiptDownload(int id)
    {
        var pdf = await GenerateThermalReceiptPdfAsync(id);
        if (pdf is null) return NotFound();

        return File(pdf.Value.Bytes, "application/pdf", $"{pdf.Value.InvoiceNumber}-receipt.pdf");
    }

    private async Task<(byte[] Bytes, string InvoiceNumber)?> GenerateThermalReceiptPdfAsync(int id)
    {
        var invoice = await db.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.UnitOfMeasure)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (invoice is null) return null;
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return null;

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

        var customerOutstandingDue = await db.SalesInvoices
            .Where(s => s.CustomerId == invoice.CustomerId && s.Status != DocumentStatus.Cancelled)
            .Select(s => s.Items.Sum(i => i.Quantity * i.UnitPrice) - s.Payments.Sum(p => p.Amount))
            .SumAsync();

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
            DueAmount = dueAmount.ToString("N2"),
            CustomerOutstandingDue = customerOutstandingDue.ToString("N2")
        };

        var detailLines = ThermalReceiptFormatter.BuildDetailLines(header, itemLines);

        // The RDLC's fixed-position sections (company header, invoice meta, totals,
        // footer) have known heights, but the receipt/detail-lines block in between
        // grows with the invoice, so every section after it is positioned here via
        // template placeholders rather than fixed coordinates in the .rdlc.
        const double dynamicSectionTop = 1.78;
        // The detail Tablix's row Textbox has CanGrow=false (see the .rdlc), so each row
        // visually renders at exactly this declared height — verified by extracting each
        // line's actual PDF coordinates from generated receipts, which showed a rock-steady
        // 0.16in between every consecutive row. This constant is the Tablix's own declared
        // <Height> (__DYNAMICHEIGHT__).
        const double dynamicRowHeightDesign = 0.16;
        const double totalsHeight = 0.9; // Total + Paid + Balance Due + Customer Outstanding rows, fixed count
        const double maxPageHeightIn = 60;

        var dynamicHeight = detailLines.Count * dynamicRowHeightDesign;
        // Everything after the Tablix (divider/totals/footer) is placed at an absolute <Top>,
        // but AspNetCore.Reporting's actual rendered position for it does NOT move smoothly or
        // 1:1 with that declared value — confirmed by generating real receipts (10- and 16-line
        // invoices) at several different per-line multipliers and reading each line's true PDF
        // coordinates back out: small changes here produced wildly inconsistent swings (a value
        // that closed the gap at one multiplier flipped into several inches of text/totals
        // *overlap* at a nearby one), so this isn't a stable line someone can just solve for.
        // 0.044 is the largest value found, of the ones tried, that reliably leaves a small
        // *positive* gap (not overlap) for both invoices tested — biased deliberately toward
        // "a bit of leftover blank space" over any risk of overlapping text, since the latter is
        // a worse defect. If this still prints with a noticeable gap, nudge this down slightly
        // and reprint — do not extrapolate/interpolate a "precise" value from only 1-2 points,
        // per the instability described above; change it in small steps and reprint each time.
        const double dynamicRowPositioningCost = 0.044;
        var dynamicEnd = dynamicSectionTop + detailLines.Count * dynamicRowPositioningCost;
        var divider3Top = dynamicEnd + 0.04;
        var totalsTop = dynamicEnd + 0.10;
        var customerDueDividerTop = totalsTop + 0.66; // Total + Paid + Balance Due rows end here
        var totalsEnd = totalsTop + totalsHeight;
        var divider4Top = totalsEnd + 0.04;
        var footerTop = divider4Top + 0.08;
        var footerNoteTop = footerTop + 0.22;
        var footerEnd = footerNoteTop + 0.16;
        // The Body's own declared height should still reflect the Tablix's true (visual) size
        // rather than the deliberately-not-advanced dynamicEnd above, so it's never declared
        // smaller than the content actually rendered inside it.
        var bodyHeight = Math.Max(footerEnd, dynamicSectionTop + dynamicHeight + (footerEnd - dynamicEnd));

        // AspNetCore.Reporting's PDF pagination doesn't honor each item's declared
        // <Top>/<Height> for page-break decisions — it appears to walk the body's
        // report items in document order, accumulating each one's *actual* required
        // height (consistently more than its declared height), and once that running
        // total exceeds the page's printable height everything remaining spills to a
        // second page. Empirically fit against two calibration points (4 detail lines
        // -> ~5.2in required, 30 detail lines -> ~17.95in required): required(n) =
        // 3.24in + 0.49in/line. On thermal (continuous-roll) printers the PDF's page
        // height is the paper length that gets fed and cut, so any slack here prints
        // as a literal blank gap below the receipt — a flat, modest safety margin on
        // top of the calibrated minimum keeps that gap small and constant instead of
        // growing with the number of line items.
        var pageHeight = Math.Min(3.24 + detailLines.Count * 0.49 + 0.5, maxPageHeightIn);

        var templatePath = Path.Combine(env.ContentRootPath, "Reports", "SalesInvoiceThermalReceipt.rdlc");
        var rdlc = await System.IO.File.ReadAllTextAsync(templatePath);
        rdlc = rdlc
            .Replace("__DYNAMICHEIGHT__", dynamicHeight.ToString("0.00"))
            .Replace("__DIVIDER3_TOP__", divider3Top.ToString("0.00"))
            .Replace("__TOTALS_TOP__", totalsTop.ToString("0.00"))
            .Replace("__CUSTOMER_DUE_DIVIDER_TOP__", customerDueDividerTop.ToString("0.00"))
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
            return (result.MainStream, invoice.InvoiceNumber);
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
        var invoice = await db.SalesInvoices
            .Include(s => s.Items).ThenInclude(i => i.Batch)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (invoice is null) return NotFound();
        if (!User.IsWarehouseAllowed(invoice.WarehouseId)) return Forbid();

        if (invoice.Status == DocumentStatus.Cancelled)
        {
            TempData["Error"] = "This invoice is already cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Restore each line's quantity to the specific batch it was drawn from (not just the
        // warehouse aggregate), so a batch this sale depleted becomes available again for FIFO.
        foreach (var item in invoice.Items)
        {
            if (item.Batch is not null)
            {
                item.Batch.RemainingQuantity += item.Quantity;
            }
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
        var warehouseIds = User.GetWarehouseIds();
        var warehouses = db.Warehouses.Where(w => w.IsActive).AsQueryable();
        if (warehouseIds is not null)
        {
            warehouses = warehouses.Where(w => warehouseIds.Contains(w.Id));
        }

        var warehouseList = await warehouses.OrderBy(w => w.Name).ToListAsync();

        // A readonly, locked warehouse field only makes sense when the user has exactly one
        // assigned warehouse; with several (or none — unrestricted), they still pick from a
        // dropdown. Default it to the first option rather than leaving it blank: unit price
        // now depends on the batch(es) FIFO would draw from at the *selected* warehouse, so an
        // empty selection means no price can be computed until the user picks one — defaulting
        // to a real warehouse means pricing works immediately without that extra step.
        var singleWarehouseId = warehouseIds is { Count: 1 } ids ? ids[0] : (int?)null;
        var defaultWarehouseId = singleWarehouseId ?? warehouseList.FirstOrDefault()?.Id;

        ViewData["Customers"] = new SelectList(await db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewData["Warehouses"] = new SelectList(warehouseList, "Id", "Name", defaultWarehouseId);
        ViewData["WarehouseScoped"] = singleWarehouseId.HasValue;
        ViewData["DefaultWarehouseId"] = defaultWarehouseId;

        // When the user is scoped to one warehouse, show that warehouse's actual stock in the picker;
        // otherwise fall back to the global total as a guide (the server re-validates against the chosen warehouse on submit).
        // Unit price is no longer shown here at all — it depends on which batch(es) FIFO draws
        // from for the entered quantity, computed live via PreviewAllocation as the user types.
        if (singleWarehouseId.HasValue)
        {
            ViewData["Products"] = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Sku,
                    p.Name,
                    CurrentStock = p.WarehouseStocks.Where(s => s.WarehouseId == singleWarehouseId).Select(s => s.Quantity).FirstOrDefault(),
                    p.UnitOfMeasure!.Symbol,
                    Category = p.Category!.Name
                }).ToListAsync();
        }
        else
        {
            ViewData["Products"] = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Sku, p.Name, p.CurrentStock, p.UnitOfMeasure!.Symbol, Category = p.Category!.Name }).ToListAsync();
        }
    }
}
