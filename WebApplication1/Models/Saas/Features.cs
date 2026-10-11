namespace WebApplication1.Models.Saas;

/// <summary>
/// Catalogue of plan-gated modules. A plan lists the keys it includes; enforcement is by
/// [RequireFeature] on the controllers (server side) - hiding menu items is only cosmetic.
/// Core product/stock/warehouse/user management is always included.
/// </summary>
public static class Features
{
    public const string Purchasing = "purchasing";
    public const string Sales = "sales";
    public const string Accounting = "accounting";
    public const string PettyCash = "petty-cash";
    public const string StockTransfers = "stock-transfers";
    public const string StockAdjustments = "stock-adjustments";
    public const string Reports = "reports";
    public const string AiAssistant = "ai-assistant";

    public static readonly (string Key, string Label)[] All =
    [
        (Purchasing, "Purchasing (suppliers, purchase invoices and returns)"),
        (Sales, "Sales (customers, sales invoices and returns)"),
        (Accounting, "Accounting (chart of accounts, journals, payments)"),
        (PettyCash, "Petty cash"),
        (StockTransfers, "Stock transfers between warehouses"),
        (StockAdjustments, "Stock adjustments"),
        (Reports, "Reports and analytics"),
        (AiAssistant, "AI assistant and forecasting")
    ];

    public static bool IsKnown(string key) => All.Any(f => f.Key == key);
}
