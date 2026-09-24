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
    public Microsoft.AspNetCore.Mvc.Rendering.SelectList Warehouses { get; set; } = null!;
    public int? WarehouseId { get; set; }
    public decimal TotalCostValue { get; set; }
    public decimal TotalSaleValue { get; set; }
    public List<StockValuationRow> Rows { get; set; } = null!;
}

// One row per batch allocation actually sold (see SalesInvoiceItem) — profit is computed from
// the real batch cost and real sale price used at the time, never a current/blended price.
public class SalesProfitabilityRow
{
    public DateTime Date { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? BatchNumber { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SalesAmount => Quantity * UnitPrice;
    public decimal CostAmount => Quantity * UnitCost;
    public decimal Profit => SalesAmount - CostAmount;
}

public class SalesProfitabilityViewModel
{
    public bool WarehouseScoped { get; set; }
    public Microsoft.AspNetCore.Mvc.Rendering.SelectList Warehouses { get; set; } = null!;
    public int? WarehouseId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public List<SalesProfitabilityRow> Rows { get; set; } = null!;
}
