using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Services;

public class StockService(ApplicationDbContext db) : IStockService
{
    public async Task ReceiveStockAsync(int productId, int warehouseId, decimal quantity, string reference,
        ProductBatch? batch = null, decimal? unitCost = null, decimal? unitSalePrice = null, string? notes = null)
    {
        var product = await db.Products.FirstAsync(p => p.Id == productId);
        product.CurrentStock += quantity;

        var warehouseStock = await GetOrCreateWarehouseStockAsync(productId, warehouseId);
        warehouseStock.Quantity += quantity;

        db.StockTransactions.Add(new StockTransaction
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Type = StockTransactionType.In,
            Quantity = quantity,
            Reference = reference,
            Batch = batch,
            UnitCost = unitCost,
            UnitSalePrice = unitSalePrice,
            Notes = notes
        });
    }

    public async Task IssueStockAsync(int productId, int warehouseId, decimal quantity, string reference,
        ProductBatch? batch = null, decimal? unitCost = null, decimal? unitSalePrice = null, string? notes = null)
    {
        var product = await db.Products.FirstAsync(p => p.Id == productId);
        product.CurrentStock -= quantity;

        var warehouseStock = await GetOrCreateWarehouseStockAsync(productId, warehouseId);
        warehouseStock.Quantity -= quantity;

        db.StockTransactions.Add(new StockTransaction
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Type = StockTransactionType.Out,
            Quantity = quantity,
            Reference = reference,
            Batch = batch,
            UnitCost = unitCost,
            UnitSalePrice = unitSalePrice,
            Notes = notes
        });
    }

    public async Task ReverseTransactionsForReferenceAsync(string reference)
    {
        // !IsReversed matters when the same reference is reposted in place (an Edit) rather than
        // only ever cancelled once — without it, a later reversal would match these rows again
        // (the reversal rows themselves live under a different "REVERSAL-{reference}" tag, so
        // they're never at risk of being re-matched; it's only the originals that need marking).
        var transactions = await db.StockTransactions
            .Where(t => t.Reference == reference && !t.IsReversed)
            .ToListAsync();

        foreach (var t in transactions)
        {
            var product = await db.Products.FirstAsync(p => p.Id == t.ProductId);
            var delta = t.Type == StockTransactionType.In ? -t.Quantity : t.Quantity;
            product.CurrentStock += delta;

            var warehouseStock = await GetOrCreateWarehouseStockAsync(t.ProductId, t.WarehouseId);
            warehouseStock.Quantity += delta;

            db.StockTransactions.Add(new StockTransaction
            {
                ProductId = t.ProductId,
                WarehouseId = t.WarehouseId,
                Type = t.Type == StockTransactionType.In ? StockTransactionType.Out : StockTransactionType.In,
                Quantity = t.Quantity,
                Reference = $"REVERSAL-{reference}",
                Notes = $"Reversal of {reference}"
            });

            t.IsReversed = true;
        }
    }

    public async Task<decimal> GetStockAsync(int productId, int warehouseId)
    {
        var stock = await db.ProductWarehouseStocks
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
        return stock?.Quantity ?? 0;
    }

    private async Task<ProductWarehouseStock> GetOrCreateWarehouseStockAsync(int productId, int warehouseId)
    {
        var stock = await db.ProductWarehouseStocks
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);

        if (stock is null)
        {
            stock = new ProductWarehouseStock { ProductId = productId, WarehouseId = warehouseId, Quantity = 0 };
            db.ProductWarehouseStocks.Add(stock);
        }

        return stock;
    }
}
