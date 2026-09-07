namespace WebApplication1.Models.ViewModels;

public class StockValuationRow
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public decimal Stock { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal ValueAtCost => Stock * CostPrice;
    public decimal ValueAtSalePrice => Stock * SalePrice;
}

public class StockValuationViewModel
{
    public bool WarehouseScoped { get; set; }
    public List<StockValuationRow> Rows { get; set; } = [];
}
