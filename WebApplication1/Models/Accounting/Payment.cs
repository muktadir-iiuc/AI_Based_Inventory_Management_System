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

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
