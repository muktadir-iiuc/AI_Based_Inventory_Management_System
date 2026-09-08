using WebApplication1.Models.Accounting;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;

namespace WebApplication1.Services;

public interface IAccountingService
{
    Task<JournalEntry> PostPurchaseInvoiceAsync(PurchaseInvoice invoice);
    Task<JournalEntry> PostSalesInvoiceAsync(SalesInvoice invoice);
    Task<JournalEntry> PostSalesReturnAsync(SalesReturn salesReturn);
    Task<JournalEntry> PostPurchaseReturnAsync(PurchaseReturn purchaseReturn);
    Task<JournalEntry> PostPaymentAsync(Payment payment);
    Task ReverseJournalEntriesForReferenceAsync(string sourceReference, string reason);
    Task<List<TrialBalanceRow>> GetTrialBalanceAsync();
    Task<List<JournalEntryLine>> GetLedgerAsync(int accountId);
}

public class TrialBalanceRow
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
