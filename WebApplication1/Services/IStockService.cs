using WebApplication1.Models.Inventory;

namespace WebApplication1.Services;

public interface IStockService
{
    // batch is accepted as the entity (not an id) so a newly-created, not-yet-saved batch can be
    // passed straight through — EF Core resolves the FK via navigation fixup at SaveChangesAsync
    // time regardless of save order.
    Task ReceiveStockAsync(int productId, int warehouseId, decimal quantity, string reference,
        ProductBatch? batch = null, decimal? unitCost = null, decimal? unitSalePrice = null, string? notes = null);
    Task IssueStockAsync(int productId, int warehouseId, decimal quantity, string reference,
        ProductBatch? batch = null, decimal? unitCost = null, decimal? unitSalePrice = null, string? notes = null);
    Task ReverseTransactionsForReferenceAsync(string reference);
    Task<decimal> GetStockAsync(int productId, int warehouseId);
}
