using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

public class HomeController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.Date;
        var trendStart = today.AddDays(-29);
        var warehouseId = User.GetWarehouseId();

        var thisMonthStart = new DateTime(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);

        var vm = new DashboardViewModel
        {
            WarehouseScoped = warehouseId.HasValue,
            TotalProducts = await db.Products.CountAsync(p => p.IsActive),
            TodaySalesTotal = await db.SalesInvoiceItems
                .Where(i => i.SalesInvoice!.Date.Date == today && (!warehouseId.HasValue || i.SalesInvoice!.WarehouseId == warehouseId))
                .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0,
            TodayPurchasesTotal = await db.PurchaseInvoiceItems
                .Where(i => i.PurchaseInvoice!.Date.Date == today && (!warehouseId.HasValue || i.PurchaseInvoice!.WarehouseId == warehouseId))
                .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0,
            RecentSalesInvoices = await db.SalesInvoices.Include(s => s.Customer)
                .Where(s => !warehouseId.HasValue || s.WarehouseId == warehouseId)
                .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).Take(5).ToListAsync(),
            RecentPurchaseInvoices = await db.PurchaseInvoices.Include(p => p.Supplier)
                .Where(p => !warehouseId.HasValue || p.WarehouseId == warehouseId)
                .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).Take(5).ToListAsync(),
            ActiveCustomerCount = await db.Customers.CountAsync(c => c.IsActive),
            ActiveSupplierCount = await db.Suppliers.CountAsync(s => s.IsActive)
        };

        if (warehouseId.HasValue)
        {
            var lowStock = await db.ProductWarehouseStocks
                .Include(s => s.Product)
                .Where(s => s.WarehouseId == warehouseId && s.Product!.IsActive && s.Quantity <= s.Product!.ReorderLevel)
                .OrderBy(s => s.Quantity)
                .Take(6)
                .ToListAsync();

            vm.LowStockCount = await db.ProductWarehouseStocks
                .Include(s => s.Product)
                .CountAsync(s => s.WarehouseId == warehouseId && s.Product!.IsActive && s.Quantity <= s.Product!.ReorderLevel);
            vm.OutOfStockCount = await db.ProductWarehouseStocks
                .Include(s => s.Product)
                .CountAsync(s => s.WarehouseId == warehouseId && s.Product!.IsActive && s.Quantity <= 0);
            vm.LowStockProducts = lowStock.Select(s => s.Product!).ToList();
            vm.StockValue = await db.ProductWarehouseStocks
                .Include(s => s.Product)
                .Where(s => s.WarehouseId == warehouseId && s.Product!.IsActive)
                .SumAsync(s => (decimal?)(s.Quantity * s.Product!.CostPrice)) ?? 0;
        }
        else
        {
            vm.LowStockCount = await db.Products.CountAsync(p => p.IsActive && p.CurrentStock <= p.ReorderLevel);
            vm.OutOfStockCount = await db.Products.CountAsync(p => p.IsActive && p.CurrentStock <= 0);
            vm.LowStockProducts = await db.Products
                .Where(p => p.IsActive && p.CurrentStock <= p.ReorderLevel)
                .OrderBy(p => p.CurrentStock)
                .Take(6).ToListAsync();
            vm.StockValue = await db.Products
                .Where(p => p.IsActive)
                .SumAsync(p => (decimal?)(p.CurrentStock * p.CostPrice)) ?? 0;
        }

        vm.CashBalance = await GetBalanceAsync(SystemAccountCodes.Cash, debitPositive: true);
        vm.AccountsReceivableBalance = await GetBalanceAsync(SystemAccountCodes.AccountsReceivable, debitPositive: true);
        vm.AccountsPayableBalance = await GetBalanceAsync(SystemAccountCodes.AccountsPayable, debitPositive: false);

        vm.ThisMonthSalesTotal = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Date >= thisMonthStart && (!warehouseId.HasValue || i.SalesInvoice!.WarehouseId == warehouseId))
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0;
        vm.LastMonthSalesTotal = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Date >= lastMonthStart && i.SalesInvoice!.Date < thisMonthStart && (!warehouseId.HasValue || i.SalesInvoice!.WarehouseId == warehouseId))
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0;
        vm.ThisMonthPurchasesTotal = await db.PurchaseInvoiceItems
            .Where(i => i.PurchaseInvoice!.Date >= thisMonthStart && (!warehouseId.HasValue || i.PurchaseInvoice!.WarehouseId == warehouseId))
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0;
        vm.LastMonthPurchasesTotal = await db.PurchaseInvoiceItems
            .Where(i => i.PurchaseInvoice!.Date >= lastMonthStart && i.PurchaseInvoice!.Date < thisMonthStart && (!warehouseId.HasValue || i.PurchaseInvoice!.WarehouseId == warehouseId))
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0;

        var trendWindowStart = trendStart;
        vm.TopProducts = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Date >= trendWindowStart && (!warehouseId.HasValue || i.SalesInvoice!.WarehouseId == warehouseId))
            .GroupBy(i => new { i.ProductId, i.Product!.Name, i.Product!.Sku })
            .Select(g => new TopProductStat
            {
                Name = g.Key.Name,
                Sku = g.Key.Sku,
                QuantitySold = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.Quantity * i.UnitPrice)
            })
            .OrderByDescending(p => p.Revenue)
            .Take(5)
            .ToListAsync();

        vm.TopCustomers = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Date >= trendWindowStart && (!warehouseId.HasValue || i.SalesInvoice!.WarehouseId == warehouseId))
            .GroupBy(i => new { i.SalesInvoice!.CustomerId, i.SalesInvoice!.Customer!.Name })
            .Select(g => new NamedValueStat { Name = g.Key.Name, Value = g.Sum(i => i.Quantity * i.UnitPrice) })
            .OrderByDescending(c => c.Value)
            .Take(5)
            .ToListAsync();

        vm.CategoryBreakdown = warehouseId.HasValue
            ? await db.ProductWarehouseStocks
                .Where(s => s.WarehouseId == warehouseId && s.Product!.IsActive)
                .GroupBy(s => s.Product!.Category!.Name)
                .Select(g => new NamedValueStat { Name = g.Key, Value = g.Sum(s => s.Quantity * s.Product!.CostPrice) })
                .OrderByDescending(c => c.Value)
                .Take(6)
                .ToListAsync()
            : await db.Products
                .Where(p => p.IsActive)
                .GroupBy(p => p.Category!.Name)
                .Select(g => new NamedValueStat { Name = g.Key, Value = g.Sum(p => p.CurrentStock * p.CostPrice) })
                .OrderByDescending(c => c.Value)
                .Take(6)
                .ToListAsync();

        var salesByDay = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Date >= trendStart && (!warehouseId.HasValue || i.SalesInvoice!.WarehouseId == warehouseId))
            .GroupBy(i => i.SalesInvoice!.Date.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(i => i.Quantity * i.UnitPrice) })
            .ToDictionaryAsync(x => x.Date, x => x.Total);
        var purchasesByDay = await db.PurchaseInvoiceItems
            .Where(i => i.PurchaseInvoice!.Date >= trendStart && (!warehouseId.HasValue || i.PurchaseInvoice!.WarehouseId == warehouseId))
            .GroupBy(i => i.PurchaseInvoice!.Date.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(i => i.Quantity * i.UnitPrice) })
            .ToDictionaryAsync(x => x.Date, x => x.Total);

        for (var day = trendStart; day <= today; day = day.AddDays(1))
        {
            vm.TrendLabels.Add(day.ToString("MMM dd"));
            vm.SalesTrend.Add(salesByDay.GetValueOrDefault(day, 0));
            vm.PurchaseTrend.Add(purchasesByDay.GetValueOrDefault(day, 0));
        }

        return View(vm);
    }

    private async Task<decimal> GetBalanceAsync(string accountCode, bool debitPositive)
    {
        var account = await db.Accounts.Include(a => a.JournalEntryLines).FirstOrDefaultAsync(a => a.Code == accountCode);
        if (account is null)
        {
            return 0;
        }

        var debit = account.JournalEntryLines.Sum(l => l.Debit);
        var credit = account.JournalEntryLines.Sum(l => l.Credit);
        return debitPositive ? debit - credit : credit - debit;
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
