using System.ComponentModel.DataAnnotations;
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

public class CustomersController(ApplicationDbContext db, IAccountingService accountingService, IWebHostEnvironment env, ICompanySettingsService companySettings, IPartyPaymentService partyPayments) : Controller
{
    // Customer.OutstandingDue sums whatever SalesInvoices collection is loaded on the entity
    // (Items, Payments and Returns all required — see Customer.OutstandingDue), so a
    // warehouse-restricted user must only ever have their own warehouse(s)' invoices loaded
    // here - otherwise the "Outstanding Due" figure (and the invoice list on Details) would leak
    // amounts/documents from warehouses they aren't assigned to. Same class of leak as the
    // Products stock fix - see ProductsController.Index.
    public async Task<IActionResult> Index(string? search)
    {
        var warehouseIds = User.GetWarehouseIds();
        ViewData["WarehouseScoped"] = warehouseIds is not null;

        var query = db.Customers
            .Include(c => c.SalesInvoices.Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))).ThenInclude(s => s.Items)
            .Include(c => c.SalesInvoices.Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))).ThenInclude(s => s.Payments)
            .Include(c => c.SalesInvoices.Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))).ThenInclude(s => s.Returns).ThenInclude(r => r.Items)
            .Include(c => c.OpeningBalancePayments)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Name.Contains(search));
        }
        ViewData["Search"] = search;
        return View(await query.OrderBy(c => c.Name).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var warehouseIds = User.GetWarehouseIds();
        ViewData["WarehouseScoped"] = warehouseIds is not null;

        var customer = await db.Customers
            .Include(c => c.SalesInvoices.Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))).ThenInclude(s => s.Items)
            .Include(c => c.SalesInvoices.Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))).ThenInclude(s => s.Payments)
            .Include(c => c.SalesInvoices.Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))).ThenInclude(s => s.Returns).ThenInclude(r => r.Items)
            .Include(c => c.OpeningBalancePayments)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();
        return View(customer);
    }

    // Full running-balance statement for one customer: invoices debit the account, payments
    // received and sales returns credit it, so the closing balance is the true receivable -
    // same invoiced-minus-paid-minus-returned formula as Customer.OutstandingDue, just broken
    // out into dated rows instead of a single total. Warehouse scoping follows the same rule
    // as Index/Details: every document is filtered by the warehouse of the invoice it belongs
    // to, so a restricted user never sees amounts from warehouses they aren't assigned to.
    private async Task<PartyLedgerViewModel?> BuildLedgerAsync(int? partyId, DateTime? from, DateTime? to)
    {
        var warehouseIds = User.GetWarehouseIds();

        var vm = new PartyLedgerViewModel
        {
            PartyType = PartyLedgerType.Customer,
            PartyId = partyId,
            From = from,
            To = to,
            WarehouseScoped = warehouseIds is not null,
            Parties = await db.Customers
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToListAsync()
        };

        if (partyId is null or 0)
        {
            vm.Summary = await BuildReceivablesSummaryAsync(warehouseIds);
            return vm;
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == partyId);
        if (customer is null) return null;

        vm.PartyName = customer.Name;
        vm.PartyPhone = customer.Phone;
        vm.PartyAddress = customer.Address;

        var rows = new List<PartyLedgerRow>();

        // Party-level, not warehouse-level, so shown regardless of the caller's warehouse
        // scope. Sorts ahead of any same-day document (TypeRank -1).
        if (customer.OpeningBalance != 0)
        {
            rows.Add(new PartyLedgerRow
            {
                Date = customer.OpeningBalanceDate ?? customer.CreatedAt.Date,
                DocumentType = "Opening Balance",
                DocumentNumber = "—",
                Description = customer.OpeningBalance > 0 ? "Balance brought forward" : "Advance brought forward",
                Debit = customer.OpeningBalance > 0 ? customer.OpeningBalance : 0,
                Credit = customer.OpeningBalance < 0 ? -customer.OpeningBalance : 0,
                TypeRank = -1
            });
        }

        var invoices = await db.SalesInvoices
            .Where(s => s.CustomerId == customer.Id && s.Status == DocumentStatus.Posted)
            .Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))
            .Include(s => s.Items)
            .Include(s => s.Warehouse)
            .ToListAsync();

        rows.AddRange(invoices.Select(inv => new PartyLedgerRow
        {
            Date = inv.Date,
            DocumentType = "Sales Invoice",
            DocumentNumber = inv.InvoiceNumber,
            Description = inv.Warehouse?.Name,
            Debit = inv.TotalAmount,
            LinkController = "SalesInvoices",
            LinkId = inv.Id,
            TypeRank = 0
        }));

        var returns = await db.SalesReturns
            .Where(r => r.SalesInvoice!.CustomerId == customer.Id && r.SalesInvoice.Status == DocumentStatus.Posted)
            .Where(r => warehouseIds == null || warehouseIds.Contains(r.SalesInvoice!.WarehouseId))
            .Include(r => r.Items)
            .Include(r => r.SalesInvoice)
            .ToListAsync();

        rows.AddRange(returns.Select(ret => new PartyLedgerRow
        {
            Date = ret.Date,
            DocumentType = "Sales Return",
            DocumentNumber = ret.ReturnNumber,
            Description = $"Against {ret.SalesInvoice?.InvoiceNumber}",
            Credit = ret.TotalAmount,
            LinkController = "SalesReturns",
            LinkId = ret.Id,
            TypeRank = 1
        }));

        // Payments have no Details page of their own (Accounting only ever lists them), so
        // these rows carry no link - projected to a flat shape rather than loaded as entities.
        // Payments against the opening balance have no invoice (and so no warehouse); like the
        // opening balance itself they're shown to every caller.
        var payments = await db.Payments
            .Where(p => (p.SalesInvoiceId != null && p.SalesInvoice!.CustomerId == customer.Id
                         && p.SalesInvoice.Status == DocumentStatus.Posted
                         && (warehouseIds == null || warehouseIds.Contains(p.SalesInvoice!.WarehouseId)))
                        || (p.SalesInvoiceId == null && p.CustomerId == customer.Id))
            .Select(p => new { p.Date, p.PaymentNumber, p.Amount, InvoiceNumber = p.SalesInvoice != null ? p.SalesInvoice.InvoiceNumber : null })
            .ToListAsync();

        rows.AddRange(payments.Select(p => new PartyLedgerRow
        {
            Date = p.Date,
            DocumentType = "Payment Received",
            DocumentNumber = p.PaymentNumber,
            Description = p.InvoiceNumber is null ? "Against opening balance" : $"Against {p.InvoiceNumber}",
            Credit = p.Amount,
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
    public async Task<IActionResult> ReceivePayment(int partyId, decimal amount, DateTime? date, string? notes, string? returnUrl)
    {
        var result = await partyPayments.RecordAsync(PartyLedgerType.Customer, partyId, amount, date ?? DateTime.UtcNow.Date,
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

    // The customer's current balance for the Sales Invoice Create page — exactly the Customer
    // Ledger's closing balance for this user (same warehouse scoping), computed by the same
    // summary builder so the two figures can never drift apart. Positive = owed to us.
    [HttpGet]
    // From the invoice Edit page, excludeInvoiceId leaves that invoice (lines, payments, returns)
    // out of the balance so it reads as "due before this invoice"; settled is what the invoice
    // itself has already received/returned, which the page nets off its edited total.
    public async Task<IActionResult> Due(int id, int? excludeInvoiceId = null)
    {
        var warehouseIds = User.GetWarehouseIds();
        var row = (await BuildReceivablesSummaryAsync(warehouseIds, id, excludeInvoiceId)).FirstOrDefault();
        if (row is null) return Json(new { ok = false });

        decimal settled = 0;
        if (excludeInvoiceId != null)
        {
            var own = db.SalesInvoices.Where(i => i.Id == excludeInvoiceId && i.CustomerId == id && i.Status == DocumentStatus.Posted
                                                  && (warehouseIds == null || warehouseIds.Contains(i.WarehouseId)));
            settled = await db.Payments.Where(p => own.Any(i => i.Id == p.SalesInvoiceId)).SumAsync(p => (decimal?)p.Amount) ?? 0;
            settled += await db.SalesReturnItems.Where(r => own.Any(i => i.Id == r.SalesReturn!.SalesInvoiceId)).SumAsync(r => (decimal?)(r.Quantity * r.UnitPrice - r.DiscountShare)) ?? 0;
        }

        // Everything received from this customer to date: invoice payments (in the caller's
        // warehouse scope, like the balance) plus payments against the opening balance.
        var totalPaid = await db.Payments
            .Where(p => (p.SalesInvoiceId != null && p.SalesInvoice!.CustomerId == id && p.SalesInvoice.Status == DocumentStatus.Posted
                         && (warehouseIds == null || warehouseIds.Contains(p.SalesInvoice.WarehouseId)))
                        || (p.SalesInvoiceId == null && p.CustomerId == id))
            .SumAsync(p => (decimal?)p.Amount) ?? 0;
        return Json(new { ok = true, id = row.Id, name = row.Name, balance = row.Balance, settled, totalPaid });
    }

    // Closing receivable per customer for the ledger landing page. Three grouped queries
    // instead of loading every invoice graph, netted the same way as the ledger itself
    // (invoiced - paid - returned).
    private async Task<List<PartyLedgerSummaryRow>> BuildReceivablesSummaryAsync(List<int>? warehouseIds, int? customerId = null, int? excludeInvoiceId = null)
    {
        var invoiced = await db.SalesInvoiceItems
            .Where(i => i.IsCurrent && i.SalesInvoice!.Status == DocumentStatus.Posted)
            .Where(i => excludeInvoiceId == null || i.SalesInvoiceId != excludeInvoiceId)
            .Where(i => customerId == null || i.SalesInvoice!.CustomerId == customerId)
            .Where(i => warehouseIds == null || warehouseIds.Contains(i.SalesInvoice!.WarehouseId))
            .GroupBy(i => i.SalesInvoice!.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(i => i.Quantity * i.UnitPrice - i.DiscountShare) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        var paid = await db.Payments
            .Where(p => p.SalesInvoiceId != null && p.SalesInvoice!.Status == DocumentStatus.Posted)
            .Where(p => excludeInvoiceId == null || p.SalesInvoiceId != excludeInvoiceId)
            .Where(p => customerId == null || p.SalesInvoice!.CustomerId == customerId)
            .Where(p => warehouseIds == null || warehouseIds.Contains(p.SalesInvoice!.WarehouseId))
            .GroupBy(p => p.SalesInvoice!.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        var returned = await db.SalesReturnItems
            .Where(i => i.SalesReturn!.SalesInvoice!.Status == DocumentStatus.Posted)
            .Where(i => excludeInvoiceId == null || i.SalesReturn!.SalesInvoiceId != excludeInvoiceId)
            .Where(i => customerId == null || i.SalesReturn!.SalesInvoice!.CustomerId == customerId)
            .Where(i => warehouseIds == null || warehouseIds.Contains(i.SalesReturn!.SalesInvoice!.WarehouseId))
            .GroupBy(i => i.SalesReturn!.SalesInvoice!.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(i => i.Quantity * i.UnitPrice - i.DiscountShare) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        // Opening balances and the payments against them aren't warehouse-scoped — see Ledger.
        var openingPaid = await db.Payments
            .Where(p => p.SalesInvoiceId == null && p.CustomerId != null)
            .Where(p => customerId == null || p.CustomerId == customerId)
            .GroupBy(p => p.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        var customers = await db.Customers
            .Where(x => customerId == null || x.Id == customerId)
            .OrderBy(c => c.Name)
            .Select(c => new { Row = new PartyLedgerSummaryRow { Id = c.Id, Name = c.Name, Phone = c.Phone }, c.OpeningBalance })
            .ToListAsync();
        var openingBalances = customers.ToDictionary(c => c.Row.Id, c => c.OpeningBalance);
        var rows = customers.Select(c => c.Row).ToList();

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

    [Authorize(Roles = Roles.SalesManagers)]
    public IActionResult Create() => View(new Customer());

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer model)
    {
        // The form posts a non-negative amount plus an "is advance" flag; the sign is applied
        // only after validation, so a redisplayed form still shows what the user typed.
        if (model.OpeningBalance < 0)
        {
            ModelState.AddModelError(nameof(Customer.OpeningBalance), "Enter the opening balance as a positive amount, and choose whether it's a due or an advance.");
        }
        if (model.OpeningBalance > 0 && model.OpeningBalanceDate?.Date > DateTime.UtcNow.Date.AddDays(1)) // a day of slack: UTC can still be "yesterday" locally
        {
            ModelState.AddModelError(nameof(Customer.OpeningBalanceDate), "The opening balance date can't be in the future.");
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

        // Two saves — the opening-balance journal entry's reference needs the customer's Id —
        // kept atomic so a customer never exists with an unposted opening balance.
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Customers.Add(model);
        await db.SaveChangesAsync();
        if (model.OpeningBalance != 0)
        {
            await accountingService.PostCustomerOpeningBalanceAsync(model);
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();

        TempData["Success"] = model.OpeningBalance == 0
            ? "Customer created."
            : $"Customer created with an opening {(model.OpeningBalance > 0 ? "due" : "advance")} of {Math.Abs(model.OpeningBalance):N2}.";
        return RedirectToAction(nameof(Index));
    }

    // Lets the Sales Invoice Create page add a missing customer inline, without losing
    // whatever line items the user has already entered by navigating away to the full
    // Customers/Create page. Same role gate as that page (Create/Edit/Delete above).
    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreate([FromBody] CustomerQuickCreateRequest? request)
    {
        if (request is null || !ModelState.IsValid)
        {
            var error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Json(new { ok = false, error = string.IsNullOrWhiteSpace(error) ? "Invalid customer details." : error });
        }

        var name = request.Name.Trim();
        if (await db.Customers.AnyAsync(c => c.Name == name))
        {
            return Json(new { ok = false, error = "A customer with this name already exists." });
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !new EmailAddressAttribute().IsValid(request.Email))
        {
            return Json(new { ok = false, error = "Enter a valid email address, or leave it blank." });
        }

        var customer = new Customer
        {
            Name = name,
            ContactPerson = string.IsNullOrWhiteSpace(request.ContactPerson) ? null : request.ContactPerson.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            CreatedBy = User.Identity?.Name
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return Json(new { ok = true, id = customer.Id, name = customer.Name });
    }

    [Authorize(Roles = Roles.SalesManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return NotFound();
        return View(customer);
    }

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return NotFound();

        customer.Name = model.Name;
        customer.ContactPerson = model.ContactPerson;
        customer.Phone = model.Phone;
        customer.Email = model.Email;
        customer.Address = model.Address;
        customer.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Customer updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await db.Customers.Include(c => c.SalesInvoices).Include(c => c.OpeningBalancePayments).FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();

        if (customer.SalesInvoices.Count != 0)
        {
            TempData["Error"] = "Cannot delete a customer that has sales invoices.";
            return RedirectToAction(nameof(Index));
        }
        if (customer.OpeningBalancePayments.Count != 0)
        {
            TempData["Error"] = "Cannot delete a customer that has payments against their opening balance.";
            return RedirectToAction(nameof(Index));
        }

        // Otherwise the opening balance would stay in Accounts Receivable with no customer.
        if (customer.OpeningBalance != 0)
        {
            await accountingService.ReverseJournalEntriesForReferenceAsync(
                AccountingService.CustomerOpeningBalanceReference(customer.Id), $"customer {customer.Name} deleted");
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync();
        TempData["Success"] = "Customer deleted.";
        return RedirectToAction(nameof(Index));
    }
}
