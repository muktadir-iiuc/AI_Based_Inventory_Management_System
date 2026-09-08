using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Purchase;

public class PurchaseReturn
{
    public int Id { get; set; }

    public string ReturnNumber { get; set; } = string.Empty;

    public int PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public decimal TotalAmount => Items.Sum(i => i.Quantity * i.UnitCost);

    public ICollection<PurchaseReturnItem> Items { get; set; } = [];
}

public class PurchaseReturnItem
{
    public int Id { get; set; }

    public int PurchaseReturnId { get; set; }
    public PurchaseReturn? PurchaseReturn { get; set; }

    // The original line owns exactly one batch — returning against it identifies the batch
    // without a separate allocation-search step. Rejected if it would exceed the batch's
    // current RemainingQuantity (already-sold stock can't be returned).
    public int PurchaseInvoiceItemId { get; set; }
    public PurchaseInvoiceItem? PurchaseInvoiceItem { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    [NotMapped]
    public decimal LineTotal => Quantity * UnitCost;
}
