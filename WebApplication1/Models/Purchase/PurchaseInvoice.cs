using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Models.Purchase;

public enum DocumentStatus
{
    Posted = 1,
    Cancelled = 2
}

public class PurchaseInvoice
{
    public int Id { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public DocumentStatus Status { get; set; } = DocumentStatus.Posted;

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Only IsCurrent lines count toward the total — see PurchaseInvoiceItem.IsCurrent.
    [NotMapped]
    public decimal TotalAmount => Items.Where(i => i.IsCurrent).Sum(i => i.Quantity * i.UnitPrice);

    public ICollection<PurchaseInvoiceItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    // Inverse of PurchaseReturn.PurchaseInvoice - lets due-calculations net off returns
    // without a separate query.
    public ICollection<PurchaseReturn> Returns { get; set; } = [];
}

public class PurchaseInvoiceItem
{
    public int Id { get; set; }

    public int PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    // The price the resulting batch will sell at — see ProductBatch. Not the same as
    // Product.SalePrice, which is only a suggested default shown when creating this line.
    [Column(TypeName = "decimal(18,2)")]
    public decimal SalePrice { get; set; }

    public ProductBatch? Batch { get; set; }

    // False once this line has been superseded by an in-place Edit of the invoice. Rows are
    // never deleted here — ProductBatch and StockTransaction both hold Restrict FKs back to
    // this row (and its batch), so a superseded line/batch is deactivated in place instead,
    // preserving the audit trail. Every query that reads Items for totals/business logic must
    // filter to IsCurrent; only Create ever leaves this at its true default.
    public bool IsCurrent { get; set; } = true;

    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice;
}
