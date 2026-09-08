using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Inventory;

public enum StockTransactionType
{
    In = 1,
    Out = 2
}

public class StockTransaction
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public StockTransactionType Type { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    // Populated for batch-driven movements (purchase/sale/returns); null for movements that
    // predate the batch model or aren't tied to a specific batch (e.g. manual adjustments).
    public int? BatchId { get; set; }
    public ProductBatch? Batch { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? UnitCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? UnitSalePrice { get; set; }
}
