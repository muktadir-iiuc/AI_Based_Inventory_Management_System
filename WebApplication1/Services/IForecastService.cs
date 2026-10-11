namespace WebApplication1.Services;

public class ReorderSuggestion
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal ReorderLevel { get; set; }
    /// <summary>Forecast demand over <see cref="ReorderForecast.HorizonDays"/> days.</summary>
    public double ForecastedDemand { get; set; }
    public decimal SuggestedReorderQty { get; set; }
    public bool ShouldReorder { get; set; }
    public string Method { get; set; } = string.Empty;
}

/// <summary>One backtest: the last <see cref="Days"/> days of real sales were hidden, forecast from the
/// history before them by both models, and compared with what was actually sold. The better model wins.</summary>
public record HorizonCheck(int Days, double SsaErrorPercent, double MovingAverageErrorPercent, bool Accurate)
{
    public bool SsaBetter => SsaErrorPercent <= MovingAverageErrorPercent;
    public double ErrorPercent => Math.Min(SsaErrorPercent, MovingAverageErrorPercent);
}

public class ReorderForecast
{
    public List<ReorderSuggestion> Suggestions { get; set; } = [];

    /// <summary>The horizon the forecasts above were made for.</summary>
    public int HorizonDays { get; set; }

    /// <summary>The longest horizon the model forecast accurately in backtesting on this scope's own
    /// sales — null when there isn't enough history to test, or no horizon met the accuracy limit.</summary>
    public int? MaxHorizonDays { get; set; }

    /// <summary>True when the requested horizon was longer than <see cref="MaxHorizonDays"/> and was cut back.</summary>
    public bool HorizonClamped { get; set; }

    /// <summary>True when the backtest ran but no horizon met the accuracy limit; the horizon is then
    /// fixed at the most accurate one tested (<see cref="HorizonDays"/>).</summary>
    public bool NoAccurateHorizon { get; set; }

    /// <summary>The model the backtest found more accurate at <see cref="HorizonDays"/>: "SSA",
    /// "Moving Average", or null when there was nothing to compare on.</summary>
    public string? ChosenModel { get; set; }

    public List<HorizonCheck> Checks { get; set; } = [];

    /// <summary>Days of sales history the model learned from.</summary>
    public int HistoryDays { get; set; }

    public double AccuracyLimitPercent { get; set; }
}

public interface IForecastService
{
    /// <summary>
    /// Per-product demand forecast and reorder suggestion. When warehouseIds is given, current stock
    /// and sales history are scoped to those warehouses only. The longest horizon allowed is found by
    /// backtesting the model on that same sales history; a requested horizon beyond it is cut back.
    /// </summary>
    Task<ReorderForecast> GetReorderSuggestionsAsync(List<int>? warehouseIds = null, int? horizonDays = null);
}
