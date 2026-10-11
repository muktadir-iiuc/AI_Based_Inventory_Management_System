using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using WebApplication1.Data;
using WebApplication1.Models.Purchase;

namespace WebApplication1.Services;

public class ForecastService(ApplicationDbContext db) : IForecastService
{
    // Up to a year of history: enough for the model to learn weekly patterns and to backtest
    // horizons of several weeks, without one-off old seasons dominating.
    private const int HistoryDays = 365;
    private const int DefaultHorizonDays = 30;
    private const int WindowSize = 7;
    private const int MinPointsForSsa = 21; // >= 3x window size

    // Backtest settings for the AI-suggested maximum horizon. A horizon counts as accurate when,
    // averaged over several forecast start points, the forecast total is within this share of what
    // was actually sold — the figure that drives the suggested reorder quantity.
    private const double AccuracyLimit = 0.20;
    private const int HorizonStep = 7;
    private const int MaxCandidateHorizon = 180;
    private const int BacktestOrigins = 3;

    private class DailySales
    {
        public float Quantity { get; set; }
    }

    private class SalesForecast
    {
        public float[] Forecast { get; set; } = [];
    }

    public async Task<ReorderForecast> GetReorderSuggestionsAsync(List<int>? warehouseIds = null, int? horizonDays = null)
    {
        var today = DateTime.UtcNow.Date;
        var since = today.AddDays(-HistoryDays);

        var products = await db.Products
            .Where(p => p.IsActive)
            .ToListAsync();

        Dictionary<int, decimal> stockByProduct;
        if (warehouseIds is not null)
        {
            stockByProduct = await db.ProductWarehouseStocks
                .Where(s => warehouseIds.Contains(s.WarehouseId))
                .GroupBy(s => s.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(s => s.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity);
        }
        else
        {
            stockByProduct = products.ToDictionary(p => p.Id, p => p.CurrentStock);
        }

        // Real demand only: current lines (an edited invoice keeps its superseded ones) of posted
        // invoices, less what customers brought back (on the day it came back).
        var sold = await db.SalesInvoiceItems
            .Where(i => i.IsCurrent && i.SalesInvoice!.Status == DocumentStatus.Posted && i.SalesInvoice.Date >= since)
            .Where(i => warehouseIds == null || warehouseIds.Contains(i.SalesInvoice!.WarehouseId))
            .Select(i => new { i.ProductId, i.SalesInvoice!.Date, i.Quantity })
            .ToListAsync();
        var returned = await db.SalesReturnItems
            .Where(r => r.SalesReturn!.SalesInvoice!.Status == DocumentStatus.Posted && r.SalesReturn.Date >= since)
            .Where(r => warehouseIds == null || warehouseIds.Contains(r.SalesReturn!.SalesInvoice!.WarehouseId))
            .Select(r => new { r.SalesInvoiceItem!.ProductId, r.SalesReturn!.Date, Quantity = -r.Quantity })
            .ToListAsync();

        var daily = sold.Concat(returned)
            .GroupBy(s => (s.ProductId, Day: s.Date.Date))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));

        // Each product's series starts at its first sale (days before it existed aren't "zero demand").
        var firstSale = daily.Keys.GroupBy(k => k.ProductId).ToDictionary(g => g.Key, g => g.Min(k => k.Day));
        List<float> Series(Func<DateTime, decimal> qtyOn, DateTime start)
        {
            var series = new List<float>();
            for (var day = start; day <= today; day = day.AddDays(1))
            {
                series.Add((float)Math.Max(0, qtyOn(day)));
            }
            return series;
        }

        // AI-suggested maximum horizon: backtest the model on this scope's total daily demand.
        var result = new ReorderForecast
        {
            AccuracyLimitPercent = AccuracyLimit * 100
        };
        if (firstSale.Count > 0)
        {
            var start = firstSale.Values.Min();
            var totals = daily.GroupBy(kv => kv.Key.Day).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
            var totalSeries = Series(d => totals.GetValueOrDefault(d), start);
            result.HistoryDays = totalSeries.Count;
            (result.MaxHorizonDays, result.Checks) = SuggestMaxHorizon(totalSeries);
        }

        // The horizon: what was asked for (default 30), never beyond the AI-suggested maximum. When
        // nothing met the accuracy limit, the most accurate horizon tested is used, so the page never
        // shows a forecast horizon the backtest found worse than another.
        var horizon = horizonDays is > 0 ? horizonDays.Value : DefaultHorizonDays;
        if (result.MaxHorizonDays is { } max)
        {
            if (horizon > max)
            {
                horizon = max;
                result.HorizonClamped = horizonDays.HasValue;
            }
        }
        else if (result.Checks.Count > 0)
        {
            horizon = result.Checks.MinBy(c => c.ErrorPercent)!.Days;
            result.NoAccurateHorizon = true;
        }
        else
        {
            horizon = DefaultHorizonDays; // too little history to test anything: the standard view
        }
        result.HorizonDays = horizon;

        // Which model to trust at this horizon: the one that backtested better at the nearest tested
        // horizon. With nothing tested, SSA where a product has enough history (the previous behaviour).
        var nearest = result.Checks.MinBy(c => Math.Abs(c.Days - horizon));
        var useSsa = nearest?.SsaBetter ?? true;
        result.ChosenModel = nearest is null ? null : useSsa ? "SSA" : "Moving Average";

        foreach (var product in products)
        {
            var currentStock = stockByProduct.GetValueOrDefault(product.Id, 0);
            var series = firstSale.TryGetValue(product.Id, out var first)
                ? Series(d => daily.GetValueOrDefault((product.Id, d)), first)
                : [];

            double forecastedDemand;
            string method;

            if (useSsa && series.Count >= MinPointsForSsa && series.Sum(v => v) > 0)
            {
                try
                {
                    forecastedDemand = ForecastWithSsa(series, horizon);
                    method = "AI Forecast (SSA)";
                }
                catch
                {
                    forecastedDemand = MovingAverageForecast(series, horizon);
                    method = "Moving Average (fallback)";
                }
            }
            else
            {
                forecastedDemand = MovingAverageForecast(series, horizon);
                method = series.Sum(v => v) == 0 ? "No sales history"
                    : useSsa ? "Moving Average" : "Moving Average (backtested best)";
            }

            var suggestedQty = Math.Max(0, (decimal)forecastedDemand - currentStock + product.ReorderLevel);
            var shouldReorder = currentStock <= product.ReorderLevel || (decimal)forecastedDemand > currentStock;

            result.Suggestions.Add(new ReorderSuggestion
            {
                ProductId = product.Id,
                Sku = product.Sku,
                ProductName = product.Name,
                CurrentStock = currentStock,
                ReorderLevel = product.ReorderLevel,
                ForecastedDemand = Math.Round(forecastedDemand, 1),
                SuggestedReorderQty = shouldReorder ? Math.Round(suggestedQty, 0) : 0,
                ShouldReorder = shouldReorder,
                Method = method
            });
        }

        result.Suggestions = result.Suggestions.OrderByDescending(r => r.ShouldReorder).ThenBy(r => r.ProductName).ToList();
        return result;
    }

    // Walk-forward backtest: for each candidate horizon (7, 14, 21… days), hide the last h days of
    // real demand, forecast them from the history before, and compare the forecast total with what was
    // actually sold — repeated from BacktestOrigins start points a week apart and averaged, so one
    // unusual week can't decide it. A horizon is only tested when the history before it is at least
    // twice as long (an SSA model can't project further than about half of what it learned from).
    // Both SSA and the moving average are scored the same way; the better of the two counts. The
    // suggestion is the longest horizon whose average error stays within AccuracyLimit.
    private static (int? Max, List<HorizonCheck> Checks) SuggestMaxHorizon(List<float> series)
    {
        var checks = new List<HorizonCheck>();
        int? best = null;

        for (var h = HorizonStep; h <= MaxCandidateHorizon; h += HorizonStep)
        {
            var earliestTrainEnd = series.Count - h - (BacktestOrigins - 1) * HorizonStep;
            if (earliestTrainEnd < Math.Max(MinPointsForSsa, 2 * h)) break;

            var ssaErrors = new List<double>();
            var maErrors = new List<double>();
            for (var o = 0; o < BacktestOrigins; o++)
            {
                var trainEnd = series.Count - h - o * HorizonStep;
                var actual = series.Skip(trainEnd).Take(h).Sum();
                if (actual <= 0) continue;
                var train = series.Take(trainEnd).ToList();
                try
                {
                    ssaErrors.Add(Math.Abs(ForecastWithSsa(train, h) - actual) / actual);
                    maErrors.Add(Math.Abs(MovingAverageForecast(train, h) - actual) / actual);
                }
                catch
                {
                    // A start point the model can't fit just isn't counted (for either model, so they stay comparable).
                }
            }
            if (ssaErrors.Count == 0) continue;

            var check = new HorizonCheck(h, Math.Round(ssaErrors.Average() * 100, 1), Math.Round(maErrors.Average() * 100, 1), false);
            check = check with { Accurate = check.ErrorPercent <= AccuracyLimit * 100 };
            checks.Add(check);
            if (check.Accurate) best = h;
        }

        return (best, checks);
    }

    private static double ForecastWithSsa(List<float> series, int horizon)
    {
        var mlContext = new MLContext(seed: 1);
        var data = series.Select(v => new DailySales { Quantity = v }).ToList();
        var dataView = mlContext.Data.LoadFromEnumerable(data);

        var pipeline = mlContext.Forecasting.ForecastBySsa(
            outputColumnName: nameof(SalesForecast.Forecast),
            inputColumnName: nameof(DailySales.Quantity),
            windowSize: WindowSize,
            seriesLength: series.Count,
            trainSize: series.Count,
            horizon: horizon,
            confidenceLevel: 0.95f);

        var model = pipeline.Fit(dataView);
        var engine = model.CreateTimeSeriesEngine<DailySales, SalesForecast>(mlContext);
        var forecast = engine.Predict();

        return forecast.Forecast.Sum(v => Math.Max(0, v));
    }

    private static double MovingAverageForecast(List<float> series, int horizon)
    {
        var recent = series.TakeLast(14).ToList();
        var dailyAverage = recent.Count > 0 ? recent.Average() : 0;
        return dailyAverage * horizon;
    }
}
