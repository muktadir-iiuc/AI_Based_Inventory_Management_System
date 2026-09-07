namespace WebApplication1.Services;

public class ReorderSuggestion
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal ReorderLevel { get; set; }
    public double Forecasted30DayDemand { get; set; }
    public decimal SuggestedReorderQty { get; set; }
    public bool ShouldReorder { get; set; }
    public string Method { get; set; } = string.Empty;
}

public interface IForecastService
{
    /// <summary>When warehouseId is given, current stock and sales history are scoped to that warehouse only.</summary>
    Task<List<ReorderSuggestion>> GetReorderSuggestionsAsync(int? warehouseId = null);
}
