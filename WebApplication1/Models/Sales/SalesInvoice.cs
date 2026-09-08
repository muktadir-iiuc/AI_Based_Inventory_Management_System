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

    [NotMapped]
    public decimal TotalAmount => Items.Sum(i => i.Quantity * i.UnitPrice);

    public ICollection<SalesInvoiceItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
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

    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice;
}
