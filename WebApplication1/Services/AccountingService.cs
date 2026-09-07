using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;

namespace WebApplication1.Services;

public class AccountingService(ApplicationDbContext db) : IAccountingService
{
    private async Task<Account> GetAccountAsync(string code) =>
        await db.Accounts.FirstAsync(a => a.Code == code);

    private async Task<string> NextEntryNumberAsync()
    {
        var count = await db.JournalEntries.CountAsync();
        return $"JE-{count + 1:D6}";
    }

    public async Task<JournalEntry> PostPurchaseInvoiceAsync(PurchaseInvoice invoice)
    {
        var inventory = await GetAccountAsync(SystemAccountCodes.Inventory);
        var payable = await GetAccountAsync(SystemAccountCodes.AccountsPayable);
        var total = invoice.Items.Sum(i => i.Quantity * i.UnitPrice);

        var entry = new JournalEntry
        {
            EntryNumber = await NextEntryNumberAsync(),
            Date = invoice.Date,
            Description = $"Purchase invoice {invoice.InvoiceNumber}",
            Source = JournalSource.PurchaseInvoice,
            SourceReference = invoice.InvoiceNumber,
            Lines =
            [
                new JournalEntryLine { AccountId = inventory.Id, Debit = total, Credit = 0, Memo = "Goods received into inventory" },
                new JournalEntryLine { AccountId = payable.Id, Debit = 0, Credit = total, Memo = $"Payable to supplier" }
            ]
        };

        db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task<JournalEntry> PostSalesInvoiceAsync(SalesInvoice invoice)
    {
        var receivable = await GetAccountAsync(SystemAccountCodes.AccountsReceivable);
        var salesRevenue = await GetAccountAsync(SystemAccountCodes.SalesRevenue);
        var cogs = await GetAccountAsync(SystemAccountCodes.CostOfGoodsSold);
        var inventory = await GetAccountAsync(SystemAccountCodes.Inventory);

        var saleTotal = invoice.Items.Sum(i => i.Quantity * i.UnitPrice);
        var costTotal = invoice.Items.Sum(i => i.Quantity * i.UnitCost);

        var entry = new JournalEntry
        {
            EntryNumber = await NextEntryNumberAsync(),
            Date = invoice.Date,
            Description = $"Sales invoice {invoice.InvoiceNumber}",
            Source = JournalSource.SalesInvoice,
            SourceReference = invoice.InvoiceNumber,
            Lines =
            [
                new JournalEntryLine { AccountId = receivable.Id, Debit = saleTotal, Credit = 0, Memo = "Amount receivable from customer" },
                new JournalEntryLine { AccountId = salesRevenue.Id, Debit = 0, Credit = saleTotal, Memo = "Revenue recognized" },
                new JournalEntryLine { AccountId = cogs.Id, Debit = costTotal, Credit = 0, Memo = "Cost of goods sold" },
                new JournalEntryLine { AccountId = inventory.Id, Debit = 0, Credit = costTotal, Memo = "Inventory reduced at cost" }
            ]
        };

        db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task<JournalEntry> PostPaymentAsync(Payment payment)
    {
        var cash = await GetAccountAsync(SystemAccountCodes.Cash);
        JournalEntry entry;

        if (payment.Direction == PaymentDirection.Out)
        {
            var payable = await GetAccountAsync(SystemAccountCodes.AccountsPayable);
            entry = new JournalEntry
            {
                EntryNumber = await NextEntryNumberAsync(),
                Date = payment.Date,
                Description = $"Payment {payment.PaymentNumber} to supplier",
                Source = JournalSource.Payment,
                SourceReference = payment.PaymentNumber,
                Lines =
                [
                    new JournalEntryLine { AccountId = payable.Id, Debit = payment.Amount, Credit = 0, Memo = "Payable settled" },
                    new JournalEntryLine { AccountId = cash.Id, Debit = 0, Credit = payment.Amount, Memo = "Cash paid out" }
                ]
            };
        }
        else
        {
            var receivable = await GetAccountAsync(SystemAccountCodes.AccountsReceivable);
            entry = new JournalEntry
            {
                EntryNumber = await NextEntryNumberAsync(),
                Date = payment.Date,
                Description = $"Receipt {payment.PaymentNumber} from customer",
                Source = JournalSource.Payment,
                SourceReference = payment.PaymentNumber,
                Lines =
                [
                    new JournalEntryLine { AccountId = cash.Id, Debit = payment.Amount, Credit = 0, Memo = "Cash received" },
                    new JournalEntryLine { AccountId = receivable.Id, Debit = 0, Credit = payment.Amount, Memo = "Receivable settled" }
                ]
            };
        }

        db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task ReverseJournalEntriesForReferenceAsync(string sourceReference, string reason)
    {
        var entries = await db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.SourceReference == sourceReference && e.Source != JournalSource.Reversal)
            .ToListAsync();

        foreach (var original in entries)
        {
            var reversal = new JournalEntry
            {
                EntryNumber = await NextEntryNumberAsync(),
                Date = DateTime.UtcNow,
                Description = $"Reversal of {original.EntryNumber}: {reason}",
                Source = JournalSource.Reversal,
                SourceReference = sourceReference,
                Lines = original.Lines.Select(l => new JournalEntryLine
                {
                    AccountId = l.AccountId,
                    Debit = l.Credit,
                    Credit = l.Debit,
                    Memo = $"Reversal of {original.EntryNumber}"
                }).ToList()
            };

            db.JournalEntries.Add(reversal);
        }
    }

    public async Task<List<TrialBalanceRow>> GetTrialBalanceAsync()
    {
        var accounts = await db.Accounts
            .Include(a => a.JournalEntryLines)
            .OrderBy(a => a.Code)
            .ToListAsync();

        return accounts.Select(a => new TrialBalanceRow
        {
            AccountCode = a.Code,
            AccountName = a.Name,
            Type = a.Type,
            Debit = a.JournalEntryLines.Sum(l => l.Debit),
            Credit = a.JournalEntryLines.Sum(l => l.Credit)
        }).ToList();
    }

    public async Task<List<JournalEntryLine>> GetLedgerAsync(int accountId)
    {
        return await db.JournalEntryLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == accountId)
            .OrderBy(l => l.JournalEntry!.Date)
            .ToListAsync();
    }
}
