using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Common;
using WebApplication1.Models.Purchase;

namespace WebApplication1.Models.Inventory;

// One row per purchase invoice line: the authoritative source of purchase/sale price for the
// stock it represents. Sales consume batches oldest-first (FIFO) via IFifoAllocationService,
// never Product.CostPrice/SalePrice, which are only reference defaults for the next purchase.
public class ProductBatch : BaseEntity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    // Null for batches synthesized by the batch-model migration from pre-existing purchase
    // history, or future opening-stock/adjustment batches with no originating purchase line.
    public int? PurchaseInvoiceItemId { get; set; }
    public PurchaseInvoiceItem? PurchaseInvoiceItem { get; set; }

    [Required, StringLength(30)]
    public string BatchNumber { get; set; } = string.Empty;

    public DateTime PurchaseDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PurchasePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SalePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OriginalQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RemainingQuantity { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [NotMapped]
    public decimal SoldQuantity => OriginalQuantity - RemainingQuantity;

    // Optimistic concurrency: two simultaneous sales decrementing this batch's RemainingQuantity
    // cause the second SaveChangesAsync to throw DbUpdateConcurrencyException, which
    // IFifoAllocationService's callers catch and retry against freshly-read quantities.
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
