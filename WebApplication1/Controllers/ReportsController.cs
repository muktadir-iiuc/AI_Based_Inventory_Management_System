using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class ReportsController(ApplicationDbContext db, IForecastService forecastService) : Controller
{
    public async Task<IActionResult> StockValuation(int? warehouseId)
    {
        var warehouseIds = User.GetWarehouseIds();

        var warehouseQuery = db.Warehouses.Where(w => w.IsActive);
        if (warehouseIds is not null)
        {
            warehouseQuery = warehouseQuery.Where(w => warehouseIds.Contains(w.Id));
        }
        var warehouses = await warehouseQuery.OrderBy(w => w.Name).ToListAsync();

        // A picked warehouse narrows the scope to just that one — but never beyond what the user
        // may see: a restricted user asking for someone else's warehouse gets an empty scope
        // (zero stock), not that warehouse's figures.
        var effectiveIds = warehouseIds;
        if (warehouseId.HasValue)
        {
            effectiveIds = warehouseIds is null || warehouseIds.Contains(warehouseId.Value)
                ? [warehouseId.Value]
                : [];
        }

        var vm = new StockValuationViewModel
        {
            WarehouseScoped = warehouseIds is not null,
            Warehouses = new SelectList(warehouses, "Id", "Name", warehouseId),
            WarehouseId = warehouseId
        };

        var rows = await BuildStockValuationRowsAsync(effectiveIds);

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

    // Invoice-wise sales history: one master row per posted invoice, with its batch-wise lines
    // (Qty / Unit Price / Total Price) as the expandable detail. Filters: customer and an inclusive
    // From/To date range. A warehouse-restricted user only ever sees invoices of their own warehouse(s).
    public async Task<IActionResult> SalesHistory(int? customerId, DateTime? fromDate, DateTime? toDate)
    {
        var warehouseIds = User.GetWarehouseIds();

        var query = db.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            // IsCurrent: an edited invoice keeps its superseded lines, which must not be listed.
            .Include(s => s.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Product)
            .Include(s => s.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Batch)
            .Where(s => s.Status == Models.Purchase.DocumentStatus.Posted);

        if (warehouseIds is not null)
        {
            query = query.Where(s => warehouseIds.Contains(s.WarehouseId));
        }
        if (customerId.HasValue)
        {
            query = query.Where(s => s.CustomerId == customerId);
        }
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(s => s.Date >= from);
        }
        if (toDate.HasValue)
        {
            var toExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(s => s.Date < toExclusive);
        }

        var invoices = await query.OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).ToListAsync();

        var customers = await db.Customers.OrderBy(c => c.Name).ToListAsync();

        var vm = new SalesHistoryViewModel
        {
            Customers = new SelectList(customers, "Id", "Name", customerId),
            CustomerId = customerId,
            FromDate = fromDate,
            ToDate = toDate,
            WarehouseScoped = warehouseIds is not null,
            Invoices = invoices.Select(s => new SalesHistoryInvoice
            {
                Id = s.Id,
                InvoiceNumber = s.InvoiceNumber,
                Date = s.Date,
                CustomerName = s.Customer?.Name ?? string.Empty,
                WarehouseName = s.Warehouse?.Name ?? string.Empty,
                Discount = s.DiscountAmount,
                Lines = s.Items.OrderBy(i => i.Id).Select(i => new SalesHistoryLine
                {
                    Product = i.Product is null
                        ? string.Empty
                        : string.Join(" - ", new[] { i.Product.Sku, i.Product.Name, i.Product.Brand, i.Product.Size }
                            .Where(v => !string.IsNullOrWhiteSpace(v))),
                    BatchNumber = i.Batch?.BatchNumber,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                }).ToList()
            }).ToList()
        };

        return View(vm);
    }

    // One row per batch allocation actually sold — Unit Cost/Unit Sale Price are the exact
    // prices frozen on that SalesInvoiceItem at the time of sale (see §7: historical prices
    // never change because a batch's price changed later), so Profit here is always real.
    //
    // Filters: warehouse (a warehouse-restricted user can only ever see their own, so the picker
    // lists just those and any other id yields no rows) and an inclusive From/To date range.
    public async Task<IActionResult> SalesProfitability(int? warehouseId, DateTime? fromDate, DateTime? toDate)
    {
        var warehouseIds = User.GetWarehouseIds();

        var query = db.SalesInvoiceItems
            .Include(i => i.SalesInvoice)
            .Include(i => i.Product)
            .Include(i => i.Batch)
            // IsCurrent: an edited invoice keeps its superseded lines, which must not be counted again.
            .Where(i => i.IsCurrent && i.SalesInvoice!.Status == Models.Purchase.DocumentStatus.Posted);

        if (warehouseIds is not null)
        {
            query = query.Where(i => warehouseIds.Contains(i.SalesInvoice!.WarehouseId));
        }
        if (warehouseId.HasValue)
        {
            query = query.Where(i => i.SalesInvoice!.WarehouseId == warehouseId);
        }
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(i => i.SalesInvoice!.Date >= from);
        }
        if (toDate.HasValue)
        {
            var toExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(i => i.SalesInvoice!.Date < toExclusive);
        }

        var warehouseQuery = db.Warehouses.Where(w => w.IsActive);
        if (warehouseIds is not null)
        {
            warehouseQuery = warehouseQuery.Where(w => warehouseIds.Contains(w.Id));
        }
        var warehouses = await warehouseQuery.OrderBy(w => w.Name).ToListAsync();

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
                UnitPrice = i.UnitPrice,
                Discount = i.DiscountShare
            })
            .ToListAsync();

        var vm = new SalesProfitabilityViewModel
        {
            WarehouseScoped = warehouseIds is not null,
            Warehouses = new SelectList(warehouses, "Id", "Name", warehouseId),
            WarehouseId = warehouseId,
            FromDate = fromDate,
            ToDate = toDate,
            TotalSales = rows.Sum(r => r.SalesAmount),
            TotalCost = rows.Sum(r => r.CostAmount),
            TotalProfit = rows.Sum(r => r.Profit),
            Rows = rows
        };

        return View(vm);
    }
}
