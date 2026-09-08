using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class ReportsController(ApplicationDbContext db, IForecastService forecastService) : Controller
{
    public async Task<IActionResult> StockValuation()
    {
        var warehouseIds = User.GetWarehouseIds();
        var vm = new StockValuationViewModel { WarehouseScoped = warehouseIds is not null };

        var rows = await BuildStockValuationRowsAsync(warehouseIds);

        vm.TotalCostValue = rows.Sum(r => r.ValueAtCost);
        vm.TotalSaleValue = rows.Sum(r => r.ValueAtSalePrice);
        vm.Rows = rows;

        return View(vm);
    }

    // Cost/sale price are now per-batch, not a single Product field, so a product with several
    // batches at different prices shows a stock-weighted average here — chosen so ValueAtCost
    // (Stock * CostPrice) still reconstructs the true total batch value exactly, matching §21's
    // "sum of each batch's remaining qty × its own price".
    private async Task<List<StockValuationRow>> BuildStockValuationRowsAsync(List<int>? warehouseIds)
    {
        var batchQuery = db.ProductBatches.Where(b => b.IsActive);
        if (warehouseIds is not null)
        {
            batchQuery = batchQuery.Where(b => warehouseIds.Contains(b.WarehouseId));
        }

        var batchTotals = await batchQuery
            .GroupBy(b => b.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Stock = g.Sum(b => b.RemainingQuantity),
                CostValue = g.Sum(b => b.RemainingQuantity * b.PurchasePrice),
                SaleValue = g.Sum(b => b.RemainingQuantity * b.SalePrice)
            })
            .ToDictionaryAsync(x => x.ProductId);

        var products = await db.Products
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Category!.Name).ThenBy(p => p.Name)
            .ToListAsync();

        return products.Select(p =>
        {
            batchTotals.TryGetValue(p.Id, out var t);
            var stock = t?.Stock ?? 0;
            return new StockValuationRow
            {
                ProductId = p.Id,
                Sku = p.Sku,
                ProductName = p.Name,
                CategoryName = p.Category?.Name,
                Stock = stock,
                CostPrice = stock > 0 ? t!.CostValue / stock : 0,
                SalePrice = stock > 0 ? t!.SaleValue / stock : 0
            };
        }).ToList();
    }

    public async Task<IActionResult> ReorderSuggestions()
    {
        var warehouseIds = User.GetWarehouseIds();
        var suggestions = await forecastService.GetReorderSuggestionsAsync(warehouseIds);
        ViewData["ReorderCount"] = suggestions.Count(r => r.ShouldReorder);
        return View(suggestions);
    }

    // One row per batch allocation actually sold — Unit Cost/Unit Sale Price are the exact
    // prices frozen on that SalesInvoiceItem at the time of sale (see §7: historical prices
    // never change because a batch's price changed later), so Profit here is always real.
    public async Task<IActionResult> SalesProfitability()
    {
        var warehouseIds = User.GetWarehouseIds();

        var query = db.SalesInvoiceItems
            .Include(i => i.SalesInvoice)
            .Include(i => i.Product)
            .Include(i => i.Batch)
            .Where(i => i.SalesInvoice!.Status == Models.Purchase.DocumentStatus.Posted);

        if (warehouseIds is not null)
        {
            query = query.Where(i => warehouseIds.Contains(i.SalesInvoice!.WarehouseId));
        }

        var rows = await query
            .OrderByDescending(i => i.SalesInvoice!.Date).ThenByDescending(i => i.Id)
            .Select(i => new SalesProfitabilityRow
            {
                Date = i.SalesInvoice!.Date,
                InvoiceNumber = i.SalesInvoice!.InvoiceNumber,
                ProductName = i.Product!.Name,
                BatchNumber = i.Batch != null ? i.Batch.BatchNumber : null,
                Quantity = i.Quantity,
                UnitCost = i.UnitCost,
                UnitPrice = i.UnitPrice
            })
            .ToListAsync();

        var vm = new SalesProfitabilityViewModel
        {
            WarehouseScoped = warehouseIds is not null,
            TotalSales = rows.Sum(r => r.SalesAmount),
            TotalCost = rows.Sum(r => r.CostAmount),
            TotalProfit = rows.Sum(r => r.Profit),
            Rows = rows
        };

        return View(vm);
    }
}
