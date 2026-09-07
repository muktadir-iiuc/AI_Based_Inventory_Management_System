using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Inventory;

public class ProductWarehouseStock
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }
}
