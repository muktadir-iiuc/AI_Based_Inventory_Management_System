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
        var warehouseId = User.GetWarehouseId();
        var vm = new StockValuationViewModel { WarehouseScoped = warehouseId.HasValue };

        if (warehouseId.HasValue)
        {
            vm.Rows = await db.ProductWarehouseStocks
                .Include(s => s.Product).ThenInclude(p => p!.Category)
                .Where(s => s.WarehouseId == warehouseId && s.Product!.IsActive)
                .OrderBy(s => s.Product!.Category!.Name).ThenBy(s => s.Product!.Name)
                .Select(s => new StockValuationRow
                {
                    ProductId = s.ProductId,
                    Sku = s.Product!.Sku,
                    ProductName = s.Product!.Name,
                    CategoryName = s.Product!.Category!.Name,
                    Stock = s.Quantity,
                    CostPrice = s.Product!.CostPrice,
                    SalePrice = s.Product!.SalePrice
                }).ToListAsync();
        }
        else
        {
            vm.Rows = await db.Products
                .Include(p => p.Category)
                .Where(p => p.IsActive)
                .OrderBy(p => p.Category!.Name).ThenBy(p => p.Name)
                .Select(p => new StockValuationRow
                {
                    ProductId = p.Id,
                    Sku = p.Sku,
                    ProductName = p.Name,
                    CategoryName = p.Category!.Name,
                    Stock = p.CurrentStock,
                    CostPrice = p.CostPrice,
                    SalePrice = p.SalePrice
                }).ToListAsync();
        }

        return View(vm);
    }

    public async Task<IActionResult> ReorderSuggestions()
    {
        var warehouseId = User.GetWarehouseId();
        var suggestions = await forecastService.GetReorderSuggestionsAsync(warehouseId);
        return View(suggestions);
    }
}
