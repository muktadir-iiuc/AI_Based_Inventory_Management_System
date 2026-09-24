using System.ComponentModel.DataAnnotations;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Models.ViewModels;

public class StockAdjustmentCreateViewModel
{
    [Required(ErrorMessage = "Please select a warehouse.")]
    public int WarehouseId { get; set; }

    [Required(ErrorMessage = "Please select a reason.")]
    public StockAdjustmentReason? Reason { get; set; }

    public string? Notes { get; set; }

    public List<StockAdjustmentLineInput> Items { get; set; } = [];
}

public class StockAdjustmentLineInput
{
    [Required]
    public int ProductId { get; set; }

    public StockAdjustmentDirection Direction { get; set; } = StockAdjustmentDirection.Decrease;

    [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; }

    // Increase lines only.
    public decimal? UnitCost { get; set; }
    public decimal? SalePrice { get; set; }
}
