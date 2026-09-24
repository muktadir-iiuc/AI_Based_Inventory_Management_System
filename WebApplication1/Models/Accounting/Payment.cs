using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;

namespace WebApplication1.Models.Accounting;

public enum PaymentDirection
{
    Out = 1, // paid to a supplier
    In = 2   // received from a customer
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
