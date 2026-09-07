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
    public IActionResult CreateAccount() => View(new Account());

    [HttpPost]
    [Authorize(Roles = Roles.AccountingManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(Account model)
    {
        if (await db.Accounts.AnyAsync(a => a.Code == model.Code))
        {
            ModelState.AddModelError(nameof(Account.Code), "This account code is already in use.");
        }
        if (!ModelState.IsValid) return View(model);

        model.CreatedBy = User.Identity?.Name;
        db.Accounts.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "Account created.";
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
        return View(await db.JournalEntries.Include(j => j.Lines)
            .OrderByDescending(j => j.Date).ThenByDescending(j => j.Id).ToListAsync());
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
        var warehouseId = User.GetWarehouseId();
        var query = db.Payments
            .Include(p => p.PurchaseInvoice).ThenInclude(pi => pi!.Supplier)
            .Include(p => p.SalesInvoice).ThenInclude(si => si!.Customer)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(p =>
                (p.PurchaseInvoice != null && p.PurchaseInvoice.WarehouseId == warehouseId) ||
                (p.SalesInvoice != null && p.SalesInvoice.WarehouseId == warehouseId));
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
        return View(new LedgerViewModel { Account = account, Lines = lines });
    }

    private async Task PopulateAccountsAsync()
    {
        ViewData["Accounts"] = new SelectList(await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Code).ToListAsync(), "Id", "Code");
        ViewData["AccountsFull"] = await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Code)
            .Select(a => new { a.Id, a.Code, a.Name }).ToListAsync();
    }

    private async Task PopulateInvoicesAsync()
    {
        var warehouseId = User.GetWarehouseId();

        var purchaseInvoices = db.PurchaseInvoices.Include(p => p.Supplier)
            .Where(p => p.Status == Models.Purchase.DocumentStatus.Posted);
        var salesInvoices = db.SalesInvoices.Include(s => s.Customer)
            .Where(s => s.Status == Models.Purchase.DocumentStatus.Posted);

        if (warehouseId.HasValue)
        {
            purchaseInvoices = purchaseInvoices.Where(p => p.WarehouseId == warehouseId);
            salesInvoices = salesInvoices.Where(s => s.WarehouseId == warehouseId);
        }

        ViewData["PurchaseInvoices"] = new SelectList(
            await purchaseInvoices.OrderByDescending(p => p.Date)
                .Select(p => new { p.Id, Label = p.InvoiceNumber + " - " + p.Supplier!.Name }).ToListAsync(),
            "Id", "Label");

        ViewData["SalesInvoices"] = new SelectList(
            await salesInvoices.OrderByDescending(s => s.Date)
                .Select(s => new { s.Id, Label = s.InvoiceNumber + " - " + s.Customer!.Name }).ToListAsync(),
            "Id", "Label");
    }
}
