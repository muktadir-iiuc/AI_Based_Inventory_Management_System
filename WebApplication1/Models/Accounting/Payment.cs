using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;

namespace WebApplication1.Models.Accounting;

public enum PaymentDirection
{
    Out = 1, // paid to a supplier
    In = 2   // received from a customer
}

// How the money moved. Each method posts to its own asset account (see PaymentMethods), so
// the trial balance shows what is held in cash, in each mobile wallet and in the bank.
public enum PaymentMethod
{
    Cash = 1,
    [Display(Name = "bKash")] BKash = 2,
    Rocket = 3,
    Nagad = 4,
    Bank = 5
}

public static class PaymentMethods
{
    public static string AccountCode(PaymentMethod method) => method switch
    {
        PaymentMethod.BKash => SystemAccountCodes.BKash,
        PaymentMethod.Rocket => SystemAccountCodes.Rocket,
        PaymentMethod.Nagad => SystemAccountCodes.Nagad,
        PaymentMethod.Bank => SystemAccountCodes.Bank,
        _ => SystemAccountCodes.Cash
    };

    // Returns an error message, or null when valid. Bank payments must name the bank.
    public static string? Validate(PaymentMethod method, string? bankName)
    {
        if (!Enum.IsDefined(method)) return "Select a payment method.";
        if (method == PaymentMethod.Bank && string.IsNullOrWhiteSpace(bankName)) return "Enter the bank name for a bank payment.";
        if (bankName?.Trim().Length > 100) return "The bank name can be at most 100 characters.";
        return null;
    }

    // The bank name is only kept for bank payments.
    public static string? CleanBankName(PaymentMethod method, string? bankName) =>
        method == PaymentMethod.Bank ? bankName?.Trim() : null;

    public static string Name(PaymentMethod method) => method == PaymentMethod.BKash ? "bKash" : method.ToString();

    // "Bank (City Bank)", "bKash", "Cash" …
    public static string Describe(PaymentMethod method, string? bankName) =>
        method == PaymentMethod.Bank && !string.IsNullOrWhiteSpace(bankName) ? $"Bank ({bankName})" : Name(method);
}

public class Payment
{
    public int Id { get; set; }

    public string PaymentNumber { get; set; } = string.Empty;

    public PaymentDirection Direction { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public string? Notes { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    // Only for Method = Bank.
    [StringLength(100)]
    public string? BankName { get; set; }

    // A discount allowed to a customer when settling their account (Receive Payment): no money
    // moves, but it settles dues exactly like a payment, so every dues figure nets it automatically.
    public bool IsDiscount { get; set; }

    [NotMapped]
    public string MethodText => IsDiscount ? "Discount" : PaymentMethods.Describe(Method, BankName);

    public int? PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public int? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    // Set (with no invoice) only for a payment against the party's opening balance — the
    // balance brought forward when the customer/supplier was created, which has no invoice
    // of its own to pay against. Invoice payments leave these null; their party is reached
    // through the invoice instead.
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
