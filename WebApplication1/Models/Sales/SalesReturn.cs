using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Sales;

public class SalesReturn
{
    public int Id { get; set; }

    public string ReturnNumber { get; set; } = string.Empty;

    public int SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public decimal TotalAmount => Items.Sum(i => i.Quantity * i.UnitPrice);

    public ICollection<SalesReturnItem> Items { get; set; } = [];
}

public class SalesReturnItem
{
    public int Id { get; set; }

    public int SalesReturnId { get; set; }
    public SalesReturn? SalesReturn { get; set; }

    // Ties the return straight back to the original batch allocation — no separate
    // "which batch was this from" lookup needed, the original line already recorded it.
    public int SalesInvoiceItemId { get; set; }
    public SalesInvoiceItem? SalesInvoiceItem { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    // Snapshot of the original line's prices at return time (always equal to the original
    // SalesInvoiceItem's, kept here so this row is self-contained for reporting).
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice;
}
