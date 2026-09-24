using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;

namespace WebApplication1.Services;

public class AccountingService(ApplicationDbContext db) : IAccountingService
{
    private async Task<Account> GetAccountAsync(string code) =>
        await db.Accounts.FirstAsync(a => a.Code == code);

    // Counts pending (not-yet-saved) JournalEntry rows too, not just what's already in the
    // database — an in-place Edit calls this twice in the same unsaved unit of work (once to
    // reverse the old entry, once to post the new one), and without counting the pending
    // reversal, the second call would compute the same number as the first and collide on
    // JournalEntries' unique EntryNumber index at SaveChangesAsync.
    private async Task<string> NextEntryNumberAsync()
    {
        var savedCount = await db.JournalEntries.CountAsync();
        var pendingCount = db.ChangeTracker.Entries<JournalEntry>().Count(e => e.State == EntityState.Added);
        return $"JE-{savedCount + pendingCount + 1:D6}";
    }

    // Booked at approval time, at the cost of the batches actually removed/added: a decrease
    // is a loss (Dr Stock Adjustment, Cr Inventory), an increase a gain (Dr Inventory, Cr Stock
    // Adjustment). A request with both directions posts both pairs in one balanced entry.
    public async Task<JournalEntry> PostStockAdjustmentAsync(StockAdjustment adjustment, decimal increaseCost, decimal decreaseCost)
    {
        var inventory = await GetAccountAsync(SystemAccountCodes.Inventory);
        var adjustmentAccount = await GetAccountAsync(SystemAccountCodes.StockAdjustment);

        var lines = new List<JournalEntryLine>();
        if (decreaseCost > 0)
        {
            lines.Add(new JournalEntryLine { AccountId = adjustmentAccount.Id, Debit = decreaseCost, Credit = 0, Memo = "Stock written off" });
            lines.Add(new JournalEntryLine { AccountId = inventory.Id, Debit = 0, Credit = decreaseCost, Memo = "Inventory reduced at cost" });
        }
        if (increaseCost > 0)
        {
            lines.Add(new JournalEntryLine { AccountId = inventory.Id, Debit = increaseCost, Credit = 0, Memo = "Inventory increased at cost" });
            lines.Add(new JournalEntryLine { AccountId = adjustmentAccount.Id, Debit = 0, Credit = increaseCost, Memo = "Stock gain" });
        }

        var entry = new JournalEntry
        {
            EntryNumber = await NextEntryNumberAsync(),
            Date = DateTime.UtcNow,
            Description = $"Stock adjustment {adjustment.AdjustmentNumber}",
            Source = JournalSource.StockAdjustment,
            SourceReference = adjustment.AdjustmentNumber,
            Lines = lines
        };

        db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task<JournalEntry> PostPurchaseInvoiceAsync(PurchaseInvoice invoice)
    {
        var inventory = await GetAccountAsync(SystemAccountCodes.Inventory);
        var payable = await GetAccountAsync(SystemAccountCodes.AccountsPayable);
        // Superseded lines (from an in-place Edit) stay in Items for audit purposes but must
        // never contribute to the posted total — see PurchaseInvoiceItem.IsCurrent.
        var total = invoice.Items.Where(i => i.IsCurrent).Sum(i => i.Quantity * i.UnitPrice);

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

        // Superseded lines (from an in-place Edit) stay in Items for audit purposes but must
        // never contribute to the posted total — see SalesInvoiceItem.IsCurrent.
        // Net of the invoice discount: revenue is recognised, and the customer owes, only what
        // they are actually charged. (Cost of goods sold is unaffected by a discount.)
        var saleTotal = invoice.Items.Where(i => i.IsCurrent).Sum(i => i.Quantity * i.UnitPrice - i.DiscountShare);
        var costTotal = invoice.Items.Where(i => i.IsCurrent).Sum(i => i.Quantity * i.UnitCost);

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

    // Mirrors PostSalesInvoiceAsync in reverse: revenue and cost are unwound at exactly the
    // prices frozen on the original sale's batch allocations (SalesReturnItem.UnitPrice/UnitCost
    // are copied from those, never recomputed from current batch or Product prices).
    public async Task<JournalEntry> PostSalesReturnAsync(SalesReturn salesReturn)
    {
        var receivable = await GetAccountAsync(SystemAccountCodes.AccountsReceivable);
        var salesRevenue = await GetAccountAsync(SystemAccountCodes.SalesRevenue);
        var cogs = await GetAccountAsync(SystemAccountCodes.CostOfGoodsSold);
        var inventory = await GetAccountAsync(SystemAccountCodes.Inventory);

        var returnTotal = salesReturn.Items.Sum(i => i.Quantity * i.UnitPrice - i.DiscountShare);
        var costTotal = salesReturn.Items.Sum(i => i.Quantity * i.UnitCost);

        var entry = new JournalEntry
        {
            EntryNumber = await NextEntryNumberAsync(),
            Date = salesReturn.Date,
            Description = $"Sales return {salesReturn.ReturnNumber}",
            Source = JournalSource.SalesReturn,
            SourceReference = salesReturn.ReturnNumber,
            Lines =
            [
                new JournalEntryLine { AccountId = salesRevenue.Id, Debit = returnTotal, Credit = 0, Memo = "Revenue reversed for returned goods" },
                new JournalEntryLine { AccountId = receivable.Id, Debit = 0, Credit = returnTotal, Memo = "Amount receivable reduced" },
                new JournalEntryLine { AccountId = inventory.Id, Debit = costTotal, Credit = 0, Memo = "Inventory restored at cost" },
                new JournalEntryLine { AccountId = cogs.Id, Debit = 0, Credit = costTotal, Memo = "Cost of goods sold reversed" }
            ]
        };

        db.JournalEntries.Add(entry);
        return entry;
    }

    // Mirrors PostPurchaseInvoiceAsync in reverse.
    public async Task<JournalEntry> PostPurchaseReturnAsync(PurchaseReturn purchaseReturn)
    {
        var inventory = await GetAccountAsync(SystemAccountCodes.Inventory);
        var payable = await GetAccountAsync(SystemAccountCodes.AccountsPayable);
        var total = purchaseReturn.Items.Sum(i => i.Quantity * i.UnitCost);

        var entry = new JournalEntry
        {
            EntryNumber = await NextEntryNumberAsync(),
            Date = purchaseReturn.Date,
            Description = $"Purchase return {purchaseReturn.ReturnNumber}",
            Source = JournalSource.PurchaseReturn,
            SourceReference = purchaseReturn.ReturnNumber,
            Lines =
            [
                new JournalEntryLine { AccountId = payable.Id, Debit = total, Credit = 0, Memo = "Payable reduced for returned goods" },
                new JournalEntryLine { AccountId = inventory.Id, Debit = 0, Credit = total, Memo = "Inventory reduced at cost" }
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

    // Source reference shared by a party's opening-balance entry and any later reversal of it
    // (e.g. on delete). Needs the party's saved Id, so the party must be saved before posting.
    public static string CustomerOpeningBalanceReference(int customerId) => $"OB-CUS-{customerId}";
    public static string SupplierOpeningBalanceReference(int supplierId) => $"OB-SUP-{supplierId}";

    // A customer's opening balance is receivable that existed before the system did, so its
    // other side is Owner's Equity (the same account the seeded opening capital uses). A
    // negative balance is an advance received — the same entry with the sides swapped.
    public async Task<JournalEntry> PostCustomerOpeningBalanceAsync(Customer customer)
    {
        var receivable = await GetAccountAsync(SystemAccountCodes.AccountsReceivable);
        var equity = await GetAccountAsync(SystemAccountCodes.OwnersEquity);
        var amount = Math.Abs(customer.OpeningBalance);
        var isDue = customer.OpeningBalance > 0;

        var entry = new JournalEntry
        {
            EntryNumber = await NextEntryNumberAsync(),
            Date = customer.OpeningBalanceDate ?? DateTime.UtcNow.Date,
            Description = $"Opening balance for customer {customer.Name}",
            Source = JournalSource.OpeningBalance,
            SourceReference = CustomerOpeningBalanceReference(customer.Id),
            Lines =
            [
                new JournalEntryLine { AccountId = receivable.Id, Debit = isDue ? amount : 0, Credit = isDue ? 0 : amount, Memo = isDue ? "Opening receivable brought forward" : "Opening customer advance brought forward" },
                new JournalEntryLine { AccountId = equity.Id, Debit = isDue ? 0 : amount, Credit = isDue ? amount : 0, Memo = "Opening balance equity" }
            ]
        };

        db.JournalEntries.Add(entry);
        return entry;
    }

    // Mirror of PostCustomerOpeningBalanceAsync on the payable side: a positive balance is
    // money we owe the supplier (credits Accounts Payable), a negative one an advance we paid.
    public async Task<JournalEntry> PostSupplierOpeningBalanceAsync(Supplier supplier)
    {
        var payable = await GetAccountAsync(SystemAccountCodes.AccountsPayable);
        var equity = await GetAccountAsync(SystemAccountCodes.OwnersEquity);
        var amount = Math.Abs(supplier.OpeningBalance);
        var isDue = supplier.OpeningBalance > 0;

        var entry = new JournalEntry
        {
            EntryNumber = await NextEntryNumberAsync(),
            Date = supplier.OpeningBalanceDate ?? DateTime.UtcNow.Date,
            Description = $"Opening balance for supplier {supplier.Name}",
            Source = JournalSource.OpeningBalance,
            SourceReference = SupplierOpeningBalanceReference(supplier.Id),
            Lines =
            [
                new JournalEntryLine { AccountId = equity.Id, Debit = isDue ? amount : 0, Credit = isDue ? 0 : amount, Memo = "Opening balance equity" },
                new JournalEntryLine { AccountId = payable.Id, Debit = isDue ? 0 : amount, Credit = isDue ? amount : 0, Memo = isDue ? "Opening payable brought forward" : "Opening supplier advance brought forward" }
            ]
        };

        db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task ReverseJournalEntriesForReferenceAsync(string sourceReference, string reason)
    {
        // !IsReversed matters when the same reference is reposted in place (an Edit) rather than
        // only ever cancelled once — without it, a later reversal would match the original entry
        // again even though it was already reversed (reversal entries are always Source=Reversal,
        // so they're excluded on their own; it's the originals that need the extra flag).
        var entries = await db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.SourceReference == sourceReference && e.Source != JournalSource.Reversal && !e.IsReversed)
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
            original.IsReversed = true;
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
