using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;
using WebApplication1.Models.Saas;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

[RequireFeature(Features.Reports)]
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

        // Sales Officers/Viewers get the sale-price valuation only; cost is zeroed before the view.
        if (!User.CanSeePurchasePrice())
        {
            rows.ForEach(r => r.CostPrice = 0);
        }

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

    // Filters: warehouse (a warehouse-restricted user can only pick their own; any other id yields
    // nothing) and the forecast horizon in days, capped at the AI-suggested maximum — the longest
    // horizon the model forecast accurately when backtested on that warehouse scope's own sales.
    public async Task<IActionResult> ReorderSuggestions(int? warehouseId, int? days)
    {
        var warehouseIds = User.GetWarehouseIds();

        var warehouseQuery = db.Warehouses.Where(w => w.IsActive);
        if (warehouseIds is not null)
        {
            warehouseQuery = warehouseQuery.Where(w => warehouseIds.Contains(w.Id));
        }
        var warehouses = await warehouseQuery.OrderBy(w => w.Name).ToListAsync();

        var scope = warehouseIds;
        if (warehouseId.HasValue)
        {
            scope = warehouseIds is null || warehouseIds.Contains(warehouseId.Value) ? [warehouseId.Value] : [];
        }

        var forecast = await forecastService.GetReorderSuggestionsAsync(scope, days);

        return View(new ReorderSuggestionsViewModel
        {
            Forecast = forecast,
            Warehouses = new SelectList(warehouses, "Id", "Name", warehouseId),
            WarehouseId = warehouseId,
            WarehouseScoped = warehouseIds is not null
        });
    }

    // Invoice-wise sales history: one master row per posted invoice, with its batch-wise lines
    // (Qty / Unit Price / Total Price) as the expandable detail. Filters: customer and an inclusive
    // From/To date range. A warehouse-restricted user only ever sees invoices of their own warehouse(s).
    public async Task<IActionResult> SalesHistory(int? customerId, int? productId, DateTime? fromDate, DateTime? toDate)
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
        if (productId.HasValue)
        {
            // Only invoices that sold this product, and of those only this product's lines.
            query = query.Where(s => s.Items.Any(i => i.IsCurrent && i.ProductId == productId));
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
        var products = await ProductOptionsAsync();

        var vm = new SalesHistoryViewModel
        {
            Customers = new SelectList(customers, "Id", "Name", customerId),
            CustomerId = customerId,
            Products = new SelectList(products, "Id", "Label", productId),
            ProductId = productId,
            FromDate = fromDate,
            ToDate = toDate,
            WarehouseScoped = warehouseIds is not null,
            Invoices = invoices.Select(s => new SalesHistoryInvoice
            {
                Id = s.Id,
                InvoiceNumber = s.InvoiceNumber,
                Date = s.Date.WithTime(s.CreatedAt),
                CustomerName = s.Customer?.Name ?? string.Empty,
                WarehouseName = s.Warehouse?.Name ?? string.Empty,
                // With a product filter the row covers only that product's lines, so it carries only
                // their share of the invoice discount (DiscountShare) — Total Price is then that
                // product's net sales on the invoice.
                Discount = productId.HasValue
                    ? s.Items.Where(i => i.ProductId == productId).Sum(i => i.DiscountShare)
                    : s.DiscountAmount,
                Lines = s.Items.Where(i => productId == null || i.ProductId == productId).OrderBy(i => i.Id).Select(i => new SalesHistoryLine
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

    // Invoice-wise purchase history, the mirror of SalesHistory: one master row per posted purchase
    // invoice with its batch lines (Qty / Unit Price / Total Price) as the expandable detail.
    // Filters: supplier, product and an inclusive From/To date range. Purchase prices are cost data,
    // so it is limited to the purchase-price viewers; warehouse-restricted users see only their own.
    [Authorize(Roles = Roles.PurchasePriceViewers)]
    public async Task<IActionResult> PurchaseHistory(int? supplierId, int? productId, DateTime? fromDate, DateTime? toDate)
    {
        var warehouseIds = User.GetWarehouseIds();

        var query = db.PurchaseInvoices
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            // IsCurrent: an edited invoice keeps its superseded lines, which must not be listed.
            .Include(p => p.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Product)
            .Include(p => p.Items.Where(i => i.IsCurrent)).ThenInclude(i => i.Batch)
            .Where(p => p.Status == Models.Purchase.DocumentStatus.Posted);

        if (warehouseIds is not null)
        {
            query = query.Where(p => warehouseIds.Contains(p.WarehouseId));
        }
        if (supplierId.HasValue)
        {
            query = query.Where(p => p.SupplierId == supplierId);
        }
        if (productId.HasValue)
        {
            query = query.Where(p => p.Items.Any(i => i.IsCurrent && i.ProductId == productId));
        }
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(p => p.Date >= from);
        }
        if (toDate.HasValue)
        {
            var toExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(p => p.Date < toExclusive);
        }

        var invoices = await query.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToListAsync();

        var suppliers = await db.Suppliers.OrderBy(s => s.Name).ToListAsync();
        var products = await ProductOptionsAsync();

        return View(new PurchaseHistoryViewModel
        {
            Suppliers = new SelectList(suppliers, "Id", "Name", supplierId),
            SupplierId = supplierId,
            Products = new SelectList(products, "Id", "Label", productId),
            ProductId = productId,
            FromDate = fromDate,
            ToDate = toDate,
            WarehouseScoped = warehouseIds is not null,
            Invoices = invoices.Select(p => new PurchaseHistoryInvoice
            {
                Id = p.Id,
                InvoiceNumber = p.InvoiceNumber,
                Date = p.Date.WithTime(p.CreatedAt),
                SupplierName = p.Supplier?.Name ?? string.Empty,
                WarehouseName = p.Warehouse?.Name ?? string.Empty,
                Lines = p.Items.Where(i => productId == null || i.ProductId == productId).OrderBy(i => i.Id).Select(i => new PurchaseHistoryLine
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
        });
    }

    // One row per batch allocation actually sold — Unit Cost/Unit Sale Price are the exact
    // prices frozen on that SalesInvoiceItem at the time of sale (see §7: historical prices
    // never change because a batch's price changed later), so Profit here is always real.
    //
    // Filters: warehouse (a warehouse-restricted user can only ever see their own, so the picker
    // lists just those and any other id yields no rows) and an inclusive From/To date range.
    // Built entirely on unit costs, so hidden from Sales Officers and Viewers.
    [Authorize(Roles = Roles.PurchasePriceViewers)]
    public async Task<IActionResult> SalesProfitability(int? warehouseId, int? productId, DateTime? fromDate, DateTime? toDate)
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
        if (productId.HasValue)
        {
            query = query.Where(i => i.ProductId == productId);
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
                CreatedAt = i.SalesInvoice!.CreatedAt,
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
            Products = new SelectList(await ProductOptionsAsync(), "Id", "Label", productId),
            ProductId = productId,
            FromDate = fromDate,
            ToDate = toDate,
            TotalSales = rows.Sum(r => r.SalesAmount),
            TotalCost = rows.Sum(r => r.CostAmount),
            TotalProfit = rows.Sum(r => r.Profit),
            Rows = rows
        };

        return View(vm);
    }

    // Product picker for the report filters: "SKU - Name - Brand - Size", like the Sales History lines.
    private async Task<List<ProductOption>> ProductOptionsAsync() =>
        (await db.Products.OrderBy(p => p.Name).Select(p => new { p.Id, p.Sku, p.Name, p.Brand, p.Size }).ToListAsync())
        .Select(p => new ProductOption(p.Id, string.Join(" - ", new[] { p.Sku, p.Name, p.Brand, p.Size }
            .Where(v => !string.IsNullOrWhiteSpace(v)))))
        .ToList();

    private sealed record ProductOption(int Id, string Label);
}
