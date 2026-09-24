using System.ComponentModel.DataAnnotations;
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

public class SuppliersController(ApplicationDbContext db, IAccountingService accountingService, IWebHostEnvironment env, ICompanySettingsService companySettings, IPartyPaymentService partyPayments) : Controller
{
    public async Task<IActionResult> Index(string? search)
    {
        var query = db.Suppliers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Name.Contains(search));
        }
        ViewData["Search"] = search;
        return View(await query.OrderBy(s => s.Name).ToListAsync());
    }

    // The Purchase Invoices list on Details must only show invoices from the caller's assigned
    // warehouse(s) - otherwise a warehouse-restricted user could see purchase amounts/documents
    // from warehouses they aren't assigned to. Same class of leak as CustomersController.Details.
    public async Task<IActionResult> Details(int id)
    {
        var warehouseIds = User.GetWarehouseIds();
        ViewData["WarehouseScoped"] = warehouseIds is not null;

        var supplier = await db.Suppliers
            .Include(s => s.PurchaseInvoices.Where(p => warehouseIds == null || warehouseIds.Contains(p.WarehouseId)))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound();
        return View(supplier);
    }

    // Full running-balance statement for one supplier - the mirror of CustomersController.Ledger.
    // The sign convention flips: a purchase invoice CREDITS the supplier's account (we owe more)
    // while payments made and purchase returns DEBIT it, so the closing balance is what we still
    // owe. Warehouse scoping follows the same rule as Details: every document is filtered by the
    // warehouse of the invoice it belongs to.
    private async Task<PartyLedgerViewModel?> BuildLedgerAsync(int? partyId, DateTime? from, DateTime? to)
    {
        var warehouseIds = User.GetWarehouseIds();

        var vm = new PartyLedgerViewModel
        {
            PartyType = PartyLedgerType.Supplier,
            PartyId = partyId,
            From = from,
            To = to,
            WarehouseScoped = warehouseIds is not null,
            Parties = await db.Suppliers
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name })
                .ToListAsync()
        };

        if (partyId is null or 0)
        {
            vm.Summary = await BuildPayablesSummaryAsync(warehouseIds);
            return vm;
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == partyId);
        if (supplier is null) return null;

        vm.PartyName = supplier.Name;
        vm.PartyPhone = supplier.Phone;
        vm.PartyAddress = supplier.Address;

        var rows = new List<PartyLedgerRow>();

        // Party-level, not warehouse-level, so shown regardless of the caller's warehouse
        // scope. Sorts ahead of any same-day document (TypeRank -1). A positive opening
        // balance is owed to the supplier, so it credits their account like an invoice does.
        if (supplier.OpeningBalance != 0)
        {
            rows.Add(new PartyLedgerRow
            {
                Date = supplier.OpeningBalanceDate ?? supplier.CreatedAt.Date,
                DocumentType = "Opening Balance",
                DocumentNumber = "—",
                Description = supplier.OpeningBalance > 0 ? "Balance brought forward" : "Advance brought forward",
                Credit = supplier.OpeningBalance > 0 ? supplier.OpeningBalance : 0,
                Debit = supplier.OpeningBalance < 0 ? -supplier.OpeningBalance : 0,
                TypeRank = -1
            });
        }

        var invoices = await db.PurchaseInvoices
            .Where(p => p.SupplierId == supplier.Id && p.Status == DocumentStatus.Posted)
            .Where(p => warehouseIds == null || warehouseIds.Contains(p.WarehouseId))
            .Include(p => p.Items)
            .Include(p => p.Warehouse)
            .ToListAsync();

        rows.AddRange(invoices.Select(inv => new PartyLedgerRow
        {
            Date = inv.Date,
            DocumentType = "Purchase Invoice",
            DocumentNumber = inv.InvoiceNumber,
            Description = inv.Warehouse?.Name,
            Credit = inv.TotalAmount,
            LinkController = "PurchaseInvoices",
            LinkId = inv.Id,
            TypeRank = 0
        }));

        var returns = await db.PurchaseReturns
            .Where(r => r.PurchaseInvoice!.SupplierId == supplier.Id && r.PurchaseInvoice.Status == DocumentStatus.Posted)
            .Where(r => warehouseIds == null || warehouseIds.Contains(r.PurchaseInvoice!.WarehouseId))
            .Include(r => r.Items)
            .Include(r => r.PurchaseInvoice)
            .ToListAsync();

        rows.AddRange(returns.Select(ret => new PartyLedgerRow
        {
            Date = ret.Date,
            DocumentType = "Purchase Return",
            DocumentNumber = ret.ReturnNumber,
            Description = $"Against {ret.PurchaseInvoice?.InvoiceNumber}",
            Debit = ret.TotalAmount,
            LinkController = "PurchaseReturns",
            LinkId = ret.Id,
            TypeRank = 1
        }));

        // Payments have no Details page of their own (Accounting only ever lists them), so
        // these rows carry no link - projected to a flat shape rather than loaded as entities.
        // Payments against the opening balance have no invoice (and so no warehouse); like the
        // opening balance itself they're shown to every caller.
        var payments = await db.Payments
            .Where(p => (p.PurchaseInvoiceId != null && p.PurchaseInvoice!.SupplierId == supplier.Id
                         && p.PurchaseInvoice.Status == DocumentStatus.Posted
                         && (warehouseIds == null || warehouseIds.Contains(p.PurchaseInvoice!.WarehouseId)))
                        || (p.PurchaseInvoiceId == null && p.SupplierId == supplier.Id))
            .Select(p => new { p.Date, p.PaymentNumber, p.Amount, InvoiceNumber = p.PurchaseInvoice != null ? p.PurchaseInvoice.InvoiceNumber : null })
            .ToListAsync();

        rows.AddRange(payments.Select(p => new PartyLedgerRow
        {
            Date = p.Date,
            DocumentType = "Payment Made",
            DocumentNumber = p.PaymentNumber,
            Description = p.InvoiceNumber is null ? "Against opening balance" : $"Against {p.InvoiceNumber}",
            Debit = p.Amount,
            TypeRank = 2
        }));

        vm.Build(rows);
        return vm;
    }

    public async Task<IActionResult> Ledger(int? partyId, DateTime? from, DateTime? to)
    {
        var vm = await BuildLedgerAsync(partyId, from, to);
        return vm is null ? NotFound() : View("PartyLedger", vm);
    }

    // Record a payment straight from the ledger: not tied to an invoice, but to what this party
    // owes in total. The amount is applied oldest-first (see IPartyPaymentService) and can't
    // exceed the current due the ledger shows for this user's warehouse scope.
    [HttpPost]
    [Authorize(Roles = Roles.AccountingManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePayment(int partyId, decimal amount, DateTime? date, string? notes, string? returnUrl)
    {
        var result = await partyPayments.RecordAsync(PartyLedgerType.Supplier, partyId, amount, date ?? DateTime.UtcNow.Date,
            notes, User.GetWarehouseIds(), User.Identity?.Name);

        if (result.Ok)
        {
            TempData["Success"] = $"Payment {(result.PaymentNumbers.Count == 1 ? " " : "s ")}{string.Join(", ", result.PaymentNumbers)} recorded — applied to: {string.Join("; ", result.Applied)}.";
        }
        else
        {
            TempData["Error"] = result.Error;
        }

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Ledger), new { partyId });
    }

    // The same ledger (same rows, same running balance, same warehouse scoping) as an A4 PDF in
    // the Sales Invoice report's style, opened inline so it can be printed or saved from the viewer.
    public async Task<IActionResult> LedgerPdf(int partyId, DateTime? from, DateTime? to)
    {
        var vm = await BuildLedgerAsync(partyId, from, to);
        if (vm is null || vm.PartyId is not > 0) return NotFound();

        var company = await companySettings.GetAsync();
        var pdf = PartyLedgerReportBuilder.Render(vm, company, env.ContentRootPath);
        return File(pdf, "application/pdf");
    }

    // The supplier's current balance for the Purchase Invoice Create page — exactly the
    // Supplier Ledger's closing balance for this user (same warehouse scoping), computed by the
    // same summary builder so the two figures can never drift apart. Positive = we owe them.
    [HttpGet]
    // From the invoice Edit page, excludeInvoiceId leaves that invoice (lines, payments, returns)
    // out of the balance so it reads as "due before this invoice"; settled is what the invoice
    // itself has already paid/returned, which the page nets off its edited total.
    public async Task<IActionResult> Due(int id, int? excludeInvoiceId = null)
    {
        var warehouseIds = User.GetWarehouseIds();
        var row = (await BuildPayablesSummaryAsync(warehouseIds, id, excludeInvoiceId)).FirstOrDefault();
        if (row is null) return Json(new { ok = false });

        decimal settled = 0;
        if (excludeInvoiceId != null)
        {
            var own = db.PurchaseInvoices.Where(i => i.Id == excludeInvoiceId && i.SupplierId == id && i.Status == DocumentStatus.Posted
                                                     && (warehouseIds == null || warehouseIds.Contains(i.WarehouseId)));
            settled = await db.Payments.Where(p => own.Any(i => i.Id == p.PurchaseInvoiceId)).SumAsync(p => (decimal?)p.Amount) ?? 0;
            settled += await db.PurchaseReturnItems.Where(r => own.Any(i => i.Id == r.PurchaseReturn!.PurchaseInvoiceId)).SumAsync(r => (decimal?)(r.Quantity * r.UnitCost)) ?? 0;
        }

        // Everything paid to this supplier to date: invoice payments (in the caller's
        // warehouse scope, like the balance) plus payments against the opening balance.
        var totalPaid = await db.Payments
            .Where(p => (p.PurchaseInvoiceId != null && p.PurchaseInvoice!.SupplierId == id && p.PurchaseInvoice.Status == DocumentStatus.Posted
                         && (warehouseIds == null || warehouseIds.Contains(p.PurchaseInvoice.WarehouseId)))
                        || (p.PurchaseInvoiceId == null && p.SupplierId == id))
            .SumAsync(p => (decimal?)p.Amount) ?? 0;
        return Json(new { ok = true, id = row.Id, name = row.Name, balance = row.Balance, settled, totalPaid });
    }

    // Closing payable per supplier for the ledger landing page: invoiced - paid - returned,
    // as three grouped queries rather than loading every invoice graph.
    private async Task<List<PartyLedgerSummaryRow>> BuildPayablesSummaryAsync(List<int>? warehouseIds, int? supplierId = null, int? excludeInvoiceId = null)
    {
        var invoiced = await db.PurchaseInvoiceItems
            .Where(i => i.IsCurrent && i.PurchaseInvoice!.Status == DocumentStatus.Posted)
            .Where(i => excludeInvoiceId == null || i.PurchaseInvoiceId != excludeInvoiceId)
            .Where(i => supplierId == null || i.PurchaseInvoice!.SupplierId == supplierId)
            .Where(i => warehouseIds == null || warehouseIds.Contains(i.PurchaseInvoice!.WarehouseId))
            .GroupBy(i => i.PurchaseInvoice!.SupplierId)
            .Select(g => new { SupplierId = g.Key, Total = g.Sum(i => i.Quantity * i.UnitPrice) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Total);

        var paid = await db.Payments
            .Where(p => p.PurchaseInvoiceId != null && p.PurchaseInvoice!.Status == DocumentStatus.Posted)
            .Where(p => excludeInvoiceId == null || p.PurchaseInvoiceId != excludeInvoiceId)
            .Where(p => supplierId == null || p.PurchaseInvoice!.SupplierId == supplierId)
            .Where(p => warehouseIds == null || warehouseIds.Contains(p.PurchaseInvoice!.WarehouseId))
            .GroupBy(p => p.PurchaseInvoice!.SupplierId)
            .Select(g => new { SupplierId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Total);

        var returned = await db.PurchaseReturnItems
            .Where(i => i.PurchaseReturn!.PurchaseInvoice!.Status == DocumentStatus.Posted)
            .Where(i => excludeInvoiceId == null || i.PurchaseReturn!.PurchaseInvoiceId != excludeInvoiceId)
            .Where(i => supplierId == null || i.PurchaseReturn!.PurchaseInvoice!.SupplierId == supplierId)
            .Where(i => warehouseIds == null || warehouseIds.Contains(i.PurchaseReturn!.PurchaseInvoice!.WarehouseId))
            .GroupBy(i => i.PurchaseReturn!.PurchaseInvoice!.SupplierId)
            .Select(g => new { SupplierId = g.Key, Total = g.Sum(i => i.Quantity * i.UnitCost) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Total);

        // Opening balances and the payments against them aren't warehouse-scoped — see Ledger.
        var openingPaid = await db.Payments
            .Where(p => p.PurchaseInvoiceId == null && p.SupplierId != null)
            .Where(p => supplierId == null || p.SupplierId == supplierId)
            .GroupBy(p => p.SupplierId!.Value)
            .Select(g => new { SupplierId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Total);

        var suppliers = await db.Suppliers
            .Where(x => supplierId == null || x.Id == supplierId)
            .OrderBy(s => s.Name)
            .Select(s => new { Row = new PartyLedgerSummaryRow { Id = s.Id, Name = s.Name, Phone = s.Phone }, s.OpeningBalance })
            .ToListAsync();
        var openingBalances = suppliers.ToDictionary(s => s.Row.Id, s => s.OpeningBalance);
        var rows = suppliers.Select(s => s.Row).ToList();

        foreach (var row in rows)
        {
            row.Balance = openingBalances[row.Id]
                          - openingPaid.GetValueOrDefault(row.Id)
                          + invoiced.GetValueOrDefault(row.Id)
                          - paid.GetValueOrDefault(row.Id)
                          - returned.GetValueOrDefault(row.Id);
        }

        return rows;
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public IActionResult Create() => View(new Supplier());

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Supplier model)
    {
        // Same amount-plus-direction handling as CustomersController.Create.
        if (model.OpeningBalance < 0)
        {
            ModelState.AddModelError(nameof(Supplier.OpeningBalance), "Enter the opening balance as a positive amount, and choose whether it's a due or an advance.");
        }
        if (model.OpeningBalance > 0 && model.OpeningBalanceDate?.Date > DateTime.UtcNow.Date.AddDays(1)) // a day of slack: UTC can still be "yesterday" locally
        {
            ModelState.AddModelError(nameof(Supplier.OpeningBalanceDate), "The opening balance date can't be in the future.");
        }
        if (!ModelState.IsValid) return View(model);

        if (model.OpeningBalance == 0)
        {
            model.OpeningBalanceDate = null;
        }
        else
        {
            model.OpeningBalanceDate = (model.OpeningBalanceDate ?? DateTime.UtcNow).Date;
            if (model.OpeningBalanceIsAdvance)
            {
                model.OpeningBalance = -model.OpeningBalance;
            }
        }

        model.CreatedBy = User.Identity?.Name;

        // Two saves — the opening-balance journal entry's reference needs the supplier's Id —
        // kept atomic so a supplier never exists with an unposted opening balance.
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Suppliers.Add(model);
        await db.SaveChangesAsync();
        if (model.OpeningBalance != 0)
        {
            await accountingService.PostSupplierOpeningBalanceAsync(model);
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();

        TempData["Success"] = model.OpeningBalance == 0
            ? "Supplier created."
            : $"Supplier created with an opening {(model.OpeningBalance > 0 ? "due" : "advance")} of {Math.Abs(model.OpeningBalance):N2}.";
        return RedirectToAction(nameof(Index));
    }

    // Lets the Purchase Invoice Create page add a missing supplier inline, without losing
    // whatever line items the user has already entered by navigating away to the full
    // Suppliers/Create page. Same role gate as that page (Create/Edit/Delete above).
    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreate([FromBody] SupplierQuickCreateRequest? request)
    {
        if (request is null || !ModelState.IsValid)
        {
            var error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Json(new { ok = false, error = string.IsNullOrWhiteSpace(error) ? "Invalid supplier details." : error });
        }

        var name = request.Name.Trim();
        if (await db.Suppliers.AnyAsync(s => s.Name == name))
        {
            return Json(new { ok = false, error = "A supplier with this name already exists." });
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !new EmailAddressAttribute().IsValid(request.Email))
        {
            return Json(new { ok = false, error = "Enter a valid email address, or leave it blank." });
        }

        var supplier = new Supplier
        {
            Name = name,
            ContactPerson = string.IsNullOrWhiteSpace(request.ContactPerson) ? null : request.ContactPerson.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            CreatedBy = User.Identity?.Name
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return Json(new { ok = true, id = supplier.Id, name = supplier.Name });
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var supplier = await db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound();
        return View(supplier);
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Supplier model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var supplier = await db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound();

        supplier.Name = model.Name;
        supplier.ContactPerson = model.ContactPerson;
        supplier.Phone = model.Phone;
        supplier.Email = model.Email;
        supplier.Address = model.Address;
        supplier.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Supplier updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await db.Suppliers.Include(s => s.PurchaseInvoices).Include(s => s.OpeningBalancePayments).FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound();

        if (supplier.PurchaseInvoices.Count != 0)
        {
            TempData["Error"] = "Cannot delete a supplier that has purchase invoices.";
            return RedirectToAction(nameof(Index));
        }
        if (supplier.OpeningBalancePayments.Count != 0)
        {
            TempData["Error"] = "Cannot delete a supplier that has payments against their opening balance.";
            return RedirectToAction(nameof(Index));
        }

        // Otherwise the opening balance would stay in Accounts Payable with no supplier.
        if (supplier.OpeningBalance != 0)
        {
            await accountingService.ReverseJournalEntriesForReferenceAsync(
                AccountingService.SupplierOpeningBalanceReference(supplier.Id), $"supplier {supplier.Name} deleted");
        }

        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync();
        TempData["Success"] = "Supplier deleted.";
        return RedirectToAction(nameof(Index));
    }
}
