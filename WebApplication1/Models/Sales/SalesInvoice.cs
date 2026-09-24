using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Purchase;

namespace WebApplication1.Models.Sales;

public class SalesInvoice
{
    public int Id { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public DocumentStatus Status { get; set; } = DocumentStatus.Posted;

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Only IsCurrent lines count toward the totals — see SalesInvoiceItem.IsCurrent.
    // SubTotal is before the invoice discount; TotalAmount is what the customer actually owes
    // (subtotal minus the discount) and is what every ledger/due/receipt figure is built on.
    [NotMapped]
    public decimal SubTotal => Items.Where(i => i.IsCurrent).Sum(i => i.Quantity * i.UnitPrice);

    // The whole-invoice discount, stored as each line's DiscountShare (which always add up to it)
    // so that any sum over invoice lines is automatically net of the discount.
    [NotMapped]
    public decimal DiscountAmount => Items.Where(i => i.IsCurrent).Sum(i => i.DiscountShare);

    [NotMapped]
    public decimal TotalAmount => SubTotal - DiscountAmount;

    public ICollection<SalesInvoiceItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    // Inverse of SalesReturn.SalesInvoice - lets OutstandingDue-style calculations net off
    // returns without a separate query. See Customer.OutstandingDue.
    public ICollection<SalesReturn> Returns { get; set; } = [];
}

public class SalesInvoiceItem
{
    public int Id { get; set; }

    public int SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    // The specific batch this line's stock was drawn from (FIFO). Nullable only because rows
    // created before the batch model existed have no batch to point to; every new row sets it.
    // One requested quantity spanning multiple batches produces multiple SalesInvoiceItem rows.
    public int? BatchId { get; set; }
    public ProductBatch? Batch { get; set; }

    // False once this line has been superseded by an in-place Edit of the invoice. Rows are
    // never deleted here — SalesReturnItem holds a Restrict FK back to this row — so a
    // superseded line is kept and flagged instead, preserving the audit trail. Every query
    // that reads Items for totals/business logic must filter to IsCurrent.
    public bool IsCurrent { get; set; } = true;

    // This line's share of the invoice-level discount (see DiscountAllocator). The shares of an
    // invoice's current lines add up to exactly the discount given, and are fixed at posting time.
    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountShare { get; set; }

    // Before discount.
    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice;

    // After this line's share of the discount — what the customer pays for it.
    [NotMapped]
    public decimal NetTotal => Quantity * UnitPrice - DiscountShare;
}
