using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class AccountingController(ApplicationDbContext db, IAccountingService accountingService) : Controller
{
    // ---- Chart of Accounts ----

    public async Task<IActionResult> ChartOfAccounts()
    {
        return View(await db.Accounts.OrderBy(a => a.Code).ToListAsync());
    }

    [Authorize(Roles = Roles.AccountingManagers)]
    public async Task<IActionResult> CreateAccount()
    {
        ViewData["NextCodes"] = await GenerateNextAccountCodesAsync();
        return View(new Account());
    }

    [HttpPost]
    [Authorize(Roles = Roles.AccountingManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(Account model)
    {
        ModelState.Remove(nameof(Account.Code)); // auto-generated below, not user input

        if (!ModelState.IsValid)
        {
            ViewData["NextCodes"] = await GenerateNextAccountCodesAsync();
            return View(model);
        }

        model.Code = await GenerateAccountCodeAsync(model.Type);
        model.CreatedBy = User.Identity?.Name;
        db.Accounts.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Account created with code {model.Code}.";
        return RedirectToAction(nameof(ChartOfAccounts));
    }

    [Authorize(Roles = Roles.AccountingManagers)]
    public async Task<IActionResult> EditAccount(int id)
    {
        var account = await db.Accounts.FindAsync(id);
        if (account is null) return NotFound();
        return View(account);
    }

    [HttpPost]
    [Authorize(Roles = Roles.AccountingManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAccount(int id, Account model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var account = await db.Accounts.FindAsync(id);
        if (account is null) return NotFound();

        if (!account.IsSystemAccount)
        {
            account.Code = model.Code;
            account.Type = model.Type;
        }
        account.Name = model.Name;
        account.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Account updated.";
        return RedirectToAction(nameof(ChartOfAccounts));
    }

    // ---- Journal Entries ----

    public async Task<IActionResult> JournalEntries()
    {
        return View(await db.JournalEntries.Include(j => j.Lines).OrderByDescending(j => j.Date).ThenByDescending(j => j.Id).ToListAsync());
    }

    public async Task<IActionResult> JournalEntryDetails(int id)
    {
        var entry = await db.JournalEntries
            .Include(j => j.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(j => j.Id == id);
        if (entry is null) return NotFound();
        return View(entry);
    }

    [Authorize(Roles = Roles.AccountingManagers)]
    public async Task<IActionResult> CreateJournalEntry()
    {
        await PopulateAccountsAsync();
        return View(new JournalEntryCreateViewModel
        {
            Lines = [new JournalLineInput(), new JournalLineInput()]
        });
    }

    [HttpPost]
    [Authorize(Roles = Roles.AccountingManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateJournalEntry(JournalEntryCreateViewModel model)
    {
        model.Lines = model.Lines.Where(l => l.AccountId > 0 && (l.Debit > 0 || l.Credit > 0)).ToList();

        var totalDebit = model.Lines.Sum(l => l.Debit);
        var totalCredit = model.Lines.Sum(l => l.Credit);

        if (model.Lines.Count < 2)
        {
            ModelState.AddModelError(string.Empty, "A journal entry needs at least two lines.");
        }
        else if (totalDebit != totalCredit)
        {
            ModelState.AddModelError(string.Empty, $"Total debit ({totalDebit:C}) must equal total credit ({totalCredit:C}).");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAccountsAsync();
            return View(model);
        }

        var count = await db.JournalEntries.CountAsync();
        var entry = new JournalEntry
        {
            EntryNumber = $"JE-{count + 1:D6}",
            Date = model.Date,
            Description = model.Description,
            Source = JournalSource.Manual,
            CreatedBy = User.Identity?.Name,
            Lines = model.Lines.Select(l => new JournalEntryLine
            {
                AccountId = l.AccountId,
                Debit = l.Debit,
                Credit = l.Credit,
                Memo = l.Memo
            }).ToList()
        };

        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Journal entry {entry.EntryNumber} posted.";
        return RedirectToAction(nameof(JournalEntryDetails), new { id = entry.Id });
    }

    // ---- Payments & Receipts ----

    public async Task<IActionResult> Payments()
    {
        var warehouseIds = User.GetWarehouseIds();
        var query = db.Payments
            .Include(p => p.PurchaseInvoice).ThenInclude(pi => pi!.Supplier)
            .Include(p => p.SalesInvoice).ThenInclude(si => si!.Customer)
            .AsQueryable();

        if (warehouseIds is not null)
        {
            query = query.Where(p =>
                (p.PurchaseInvoice != null && warehouseIds.Contains(p.PurchaseInvoice.WarehouseId)) ||
                (p.SalesInvoice != null && warehouseIds.Contains(p.SalesInvoice.WarehouseId)));
        }

        return View(await query.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToListAsync());
    }

    [Authorize(Roles = Roles.AccountingManagers)]
    public async Task<IActionResult> CreatePayment()
    {
        await PopulateInvoicesAsync();
        return View(new PaymentCreateViewModel());
    }

    [HttpPost]
    [Authorize(Roles = Roles.AccountingManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePayment(PaymentCreateViewModel model)
    {
        if (model.Direction == PaymentDirection.Out && model.PurchaseInvoiceId is null)
        {
            ModelState.AddModelError(nameof(model.PurchaseInvoiceId), "Select the purchase invoice being paid.");
        }
        if (model.Direction == PaymentDirection.In && model.SalesInvoiceId is null)
        {
            ModelState.AddModelError(nameof(model.SalesInvoiceId), "Select the sales invoice being received.");
        }

        // A payment can't exceed what's actually still owed on the invoice — re-checked here
        // against a fresh load rather than trusting the client, since the due amount can have
        // changed (another payment posted) since the page was rendered.
        if (model.Direction == PaymentDirection.Out && model.PurchaseInvoiceId is not null)
        {
            var invoice = await db.PurchaseInvoices
                .Include(p => p.Items)
                .Include(p => p.Payments)
                .FirstOrDefaultAsync(p => p.Id == model.PurchaseInvoiceId);
            var due = invoice is null ? 0 : invoice.TotalAmount - invoice.Payments.Sum(p => p.Amount);
            if (model.Amount > due)
            {
                ModelState.AddModelError(nameof(model.Amount), $"Amount cannot exceed this invoice's due of {due:N2}.");
            }
        }
        else if (model.Direction == PaymentDirection.In && model.SalesInvoiceId is not null)
        {
            var invoice = await db.SalesInvoices
                .Include(s => s.Items)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == model.SalesInvoiceId);
            var due = invoice is null ? 0 : invoice.TotalAmount - invoice.Payments.Sum(p => p.Amount);
            if (model.Amount > due)
            {
                ModelState.AddModelError(nameof(model.Amount), $"Amount cannot exceed this invoice's due of {due:N2}.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateInvoicesAsync();
            return View(model);
        }

        var count = await db.Payments.CountAsync();
        var payment = new Payment
        {
            PaymentNumber = $"PAY-{count + 1:D6}",
            Direction = model.Direction,
            Date = model.Date,
            Amount = model.Amount,
            Notes = model.Notes,
            PurchaseInvoiceId = model.Direction == PaymentDirection.Out ? model.PurchaseInvoiceId : null,
            SalesInvoiceId = model.Direction == PaymentDirection.In ? model.SalesInvoiceId : null,
            CreatedBy = User.Identity?.Name
        };

        db.Payments.Add(payment);
        await accountingService.PostPaymentAsync(payment);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Payment {payment.PaymentNumber} recorded.";
        return RedirectToAction(nameof(Payments));
    }

    // ---- Reports ----

    public async Task<IActionResult> TrialBalance()
    {
        return View(await accountingService.GetTrialBalanceAsync());
    }

    public async Task<IActionResult> Ledger(int accountId)
    {
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null) return NotFound();

        var lines = await accountingService.GetLedgerAsync(accountId);
        var normalDebit = account.Type is AccountType.Asset or AccountType.Expense;
        decimal running = 0;
        var rows = lines.Select(l =>
        {
            running += normalDebit ? (l.Debit - l.Credit) : (l.Credit - l.Debit);
            return new LedgerLineRow(l.JournalEntry?.Date, l.JournalEntryId, l.JournalEntry?.EntryNumber, l.Memo, l.Debit, l.Credit, running);
        }).ToList();

        return View(new LedgerViewModel { Account = account, Rows = rows });
    }

    private async Task PopulateAccountsAsync()
    {
        ViewData["Accounts"] = new SelectList(await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Code).ToListAsync(), "Id", "Code");
        ViewData["AccountsFull"] = await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Code)
            .Select(a => new { a.Id, a.Code, a.Name }).ToListAsync();
    }

    private async Task PopulateInvoicesAsync()
    {
        var warehouseIds = User.GetWarehouseIds();

        var purchaseInvoicesQuery = db.PurchaseInvoices
            .Include(p => p.Supplier)
            .Include(p => p.Items)
            .Include(p => p.Payments)
            .Where(p => p.Status == Models.Purchase.DocumentStatus.Posted);
        var salesInvoicesQuery = db.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Where(s => s.Status == Models.Purchase.DocumentStatus.Posted);

        if (warehouseIds is not null)
        {
            purchaseInvoicesQuery = purchaseInvoicesQuery.Where(p => warehouseIds.Contains(p.WarehouseId));
            salesInvoicesQuery = salesInvoicesQuery.Where(s => warehouseIds.Contains(s.WarehouseId));
        }

        var purchaseInvoices = await purchaseInvoicesQuery.OrderByDescending(p => p.Date).ToListAsync();
        var salesInvoices = await salesInvoicesQuery.OrderByDescending(s => s.Date).ToListAsync();

        ViewData["PurchaseInvoices"] = new SelectList(
            purchaseInvoices.Select(p => new { p.Id, Label = p.InvoiceNumber + " - " + p.Supplier!.Name }),
            "Id", "Label");

        ViewData["SalesInvoices"] = new SelectList(
            salesInvoices.Select(s => new { s.Id, Label = s.InvoiceNumber + " - " + s.Customer!.Name }),
            "Id", "Label");

        // A supplier/customer's total outstanding balance spans every posted invoice they
        // have, not just the (possibly warehouse-scoped) ones in the dropdowns above — so this
        // is computed from a separate, unscoped query rather than summed from the lists above.
        var supplierOutstanding = await db.PurchaseInvoices
            .Where(p => p.Status == Models.Purchase.DocumentStatus.Posted)
            .Select(p => new { p.SupplierId, Due = p.Items.Sum(i => i.Quantity * i.UnitPrice) - p.Payments.Sum(pay => pay.Amount) })
            .ToListAsync();
        var supplierOutstandingTotals = supplierOutstanding
            .GroupBy(x => x.SupplierId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Due));

        var customerOutstanding = await db.SalesInvoices
            .Where(s => s.Status == Models.Purchase.DocumentStatus.Posted)
            .Select(s => new { s.CustomerId, Due = s.Items.Sum(i => i.Quantity * i.UnitPrice) - s.Payments.Sum(pay => pay.Amount) })
            .ToListAsync();
        var customerOutstandingTotals = customerOutstanding
            .GroupBy(x => x.CustomerId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Due));

        ViewData["PurchaseInvoiceMeta"] = purchaseInvoices.Select(p => new
        {
            p.Id,
            SupplierName = p.Supplier!.Name,
            InvoiceAmount = p.TotalAmount,
            InvoiceDue = p.TotalAmount - p.Payments.Sum(pay => pay.Amount),
            SupplierOutstanding = supplierOutstandingTotals.GetValueOrDefault(p.SupplierId)
        }).ToList();

        ViewData["SalesInvoiceMeta"] = salesInvoices.Select(s => new
        {
            s.Id,
            CustomerName = s.Customer!.Name,
            InvoiceAmount = s.TotalAmount,
            InvoiceDue = s.TotalAmount - s.Payments.Sum(pay => pay.Amount),
            CustomerOutstanding = customerOutstandingTotals.GetValueOrDefault(s.CustomerId)
        }).ToList();
    }

    // Account codes follow the chart-of-accounts numbering convention seeded in DbInitializer
    // (Asset 1xxx, Liability 2xxx, Equity 3xxx, Income 4xxx, Expense 5xxx), spaced by 100 within
    // each type's block so codes stay stable and readable as more accounts are added.
    private async Task<string> GenerateAccountCodeAsync(AccountType type)
    {
        var baseCode = (int)type * 1000;
        var existingCodes = await db.Accounts.Select(a => a.Code).ToListAsync();

        var maxInBlock = existingCodes
            .Select(c => int.TryParse(c, out var n) ? n : -1)
            .Where(n => n >= baseCode && n < baseCode + 1000)
            .DefaultIfEmpty(baseCode)
            .Max();

        return (maxInBlock + 100).ToString();
    }

    private async Task<Dictionary<AccountType, string>> GenerateNextAccountCodesAsync()
    {
        var result = new Dictionary<AccountType, string>();
        foreach (AccountType type in Enum.GetValues<AccountType>())
        {
            result[type] = await GenerateAccountCodeAsync(type);
        }
        return result;
    }
}
