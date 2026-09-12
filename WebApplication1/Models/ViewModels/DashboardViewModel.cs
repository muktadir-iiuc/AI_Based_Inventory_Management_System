namespace WebApplication1.Models.ViewModels;

public class DashboardViewModel
{
    public bool WarehouseScoped { get; set; }
    public int TotalProducts { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int PendingPriceReviewCount { get; set; }
    public decimal TodaySalesTotal { get; set; }
    public decimal TodayPurchasesTotal { get; set; }
    public decimal CashBalance { get; set; }
    public decimal AccountsReceivableBalance { get; set; }
    public decimal AccountsPayableBalance { get; set; }
    public decimal StockValue { get; set; }
    public int ActiveCustomerCount { get; set; }
    public int ActiveSupplierCount { get; set; }

    public decimal ThisMonthSalesTotal { get; set; }
    public decimal LastMonthSalesTotal { get; set; }
    public decimal ThisMonthPurchasesTotal { get; set; }
    public decimal LastMonthPurchasesTotal { get; set; }

    public decimal? SalesChangePercent =>
        LastMonthSalesTotal == 0 ? null : Math.Round((ThisMonthSalesTotal - LastMonthSalesTotal) / LastMonthSalesTotal * 100, 1);
    public decimal? PurchasesChangePercent =>
        LastMonthPurchasesTotal == 0 ? null : Math.Round((ThisMonthPurchasesTotal - LastMonthPurchasesTotal) / LastMonthPurchasesTotal * 100, 1);

    public List<Sales.SalesInvoice> RecentSalesInvoices { get; set; } = [];
    public List<Purchase.PurchaseInvoice> RecentPurchaseInvoices { get; set; } = [];
    public List<Inventory.Product> LowStockProducts { get; set; } = [];
    public List<TopProductStat> TopProducts { get; set; } = [];
    public List<NamedValueStat> TopCustomers { get; set; } = [];
    public List<NamedValueStat> CategoryBreakdown { get; set; } = [];

    // Trend arrays cover 30 days; the view slices the tail client-side for the 7/14/30-day toggle.
    public List<string> TrendLabels { get; set; } = [];
    public List<decimal> SalesTrend { get; set; } = [];
    public List<decimal> PurchaseTrend { get; set; } = [];
}

public class TopProductStat
{
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal QuantitySold { get; set; }
    public decimal Revenue { get; set; }
}

public class NamedValueStat
{
    public string Name { get; set; } = string.Empty;
    public decimal Value { get; set; }
}
