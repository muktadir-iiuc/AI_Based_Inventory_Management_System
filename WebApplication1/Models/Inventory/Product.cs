using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Common;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;

namespace WebApplication1.Models.Inventory;

public class Product : BaseEntity
{
    [Required, StringLength(30)]
    public string Sku { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int UnitOfMeasureId { get; set; }
    public UnitOfMeasure? UnitOfMeasure { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SalePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentStock { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReorderLevel { get; set; } = 10;

    public ICollection<StockTransaction> StockTransactions { get; set; } = [];
    public ICollection<PurchaseInvoiceItem> PurchaseInvoiceItems { get; set; } = [];
    public ICollection<SalesInvoiceItem> SalesInvoiceItems { get; set; } = [];
    public ICollection<ProductWarehouseStock> WarehouseStocks { get; set; } = [];
}
