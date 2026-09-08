using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using WebApplication1.Data;

namespace WebApplication1.Services;

public class ForecastService(ApplicationDbContext db) : IForecastService
{
    private const int HistoryDays = 90;
    private const int HorizonDays = 30;
    private const int WindowSize = 7;
    private const int MinPointsForSsa = 21; // >= 3x window size

    private class DailySales
    {
        public float Quantity { get; set; }
    }

    private class SalesForecast
    {
        public float[] Forecast { get; set; } = [];
    }

    public async Task<List<ReorderSuggestion>> GetReorderSuggestionsAsync(List<int>? warehouseIds = null)
    {
        var since = DateTime.UtcNow.Date.AddDays(-HistoryDays);

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

        var salesQuery = db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Date >= since);
        if (warehouseIds is not null)
        {
            salesQuery = salesQuery.Where(i => warehouseIds.Contains(i.SalesInvoice!.WarehouseId));
        }

        var salesByProduct = await salesQuery
            .Select(i => new { i.ProductId, i.SalesInvoice!.Date, i.Quantity })
            .ToListAsync();

        var results = new List<ReorderSuggestion>();

        foreach (var product in products)
        {
            var currentStock = stockByProduct.GetValueOrDefault(product.Id, 0);
            var productSales = salesByProduct.Where(s => s.ProductId == product.Id).ToList();

            // Build a continuous daily series (missing days = 0 units sold)
            var series = new List<float>();
            for (var day = since; day <= DateTime.UtcNow.Date; day = day.AddDays(1))
            {
                var qty = productSales
                    .Where(s => s.Date.Date == day)
                    .Sum(s => s.Quantity);
                series.Add((float)qty);
            }

            double forecastedDemand;
            string method;

            if (series.Count >= MinPointsForSsa && series.Sum(v => v) > 0)
            {
                try
                {
                    forecastedDemand = ForecastWithSsa(series);
                    method = "AI Forecast (SSA)";
                }
                catch
                {
                    forecastedDemand = MovingAverageForecast(series);
                    method = "Moving Average (fallback)";
                }
            }
            else
            {
                forecastedDemand = MovingAverageForecast(series);
                method = series.Sum(v => v) > 0 ? "Moving Average" : "No sales history";
            }

            var suggestedQty = Math.Max(0, (decimal)forecastedDemand - currentStock + product.ReorderLevel);
            var shouldReorder = currentStock <= product.ReorderLevel || (decimal)forecastedDemand > currentStock;

            results.Add(new ReorderSuggestion
            {
                ProductId = product.Id,
                Sku = product.Sku,
                ProductName = product.Name,
                CurrentStock = currentStock,
                ReorderLevel = product.ReorderLevel,
                Forecasted30DayDemand = Math.Round(forecastedDemand, 1),
                SuggestedReorderQty = shouldReorder ? Math.Round(suggestedQty, 0) : 0,
                ShouldReorder = shouldReorder,
                Method = method
            });
        }

        return results.OrderByDescending(r => r.ShouldReorder).ThenBy(r => r.ProductName).ToList();
    }

    private static double ForecastWithSsa(List<float> series)
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
            horizon: HorizonDays,
            confidenceLevel: 0.95f);

        var model = pipeline.Fit(dataView);
        var engine = model.CreateTimeSeriesEngine<DailySales, SalesForecast>(mlContext);
        var forecast = engine.Predict();

        return forecast.Forecast.Sum(v => Math.Max(0, v));
    }

    private static double MovingAverageForecast(List<float> series)
    {
        var recent = series.TakeLast(14).ToList();
        var dailyAverage = recent.Count > 0 ? recent.Average() : 0;
        return dailyAverage * HorizonDays;
    }
}
