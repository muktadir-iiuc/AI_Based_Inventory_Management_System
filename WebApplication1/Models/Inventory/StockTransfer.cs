using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Purchase;

namespace WebApplication1.Models.Inventory;

public class StockTransfer
{
    public int Id { get; set; }

    public string TransferNumber { get; set; } = string.Empty;

    public int FromWarehouseId { get; set; }
    public Warehouse? FromWarehouse { get; set; }

    public int ToWarehouseId { get; set; }
    public Warehouse? ToWarehouse { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public DocumentStatus Status { get; set; } = DocumentStatus.Posted;

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StockTransferItem> Items { get; set; } = [];
}

public class StockTransferItem
{
    public int Id { get; set; }

    public int StockTransferId { get; set; }
    public StockTransfer? StockTransfer { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    // False once this line has been superseded by an in-place Edit of the transfer. Every
    // query that reads Items for display/business logic must filter to IsCurrent.
    public bool IsCurrent { get; set; } = true;
}
