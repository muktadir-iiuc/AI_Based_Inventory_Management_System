using WebApplication1.Models.Inventory;

namespace WebApplication1.Services;

public interface IProductPriceService
{
    // Mutates the tracked product's SalePrice and stages a ProductPriceHistory row in-memory
    // when newPrice actually differs from the current price; returns null (and changes nothing)
    // when it doesn't, since an unchanged price must not create history or notifications.
    // Caller owns SaveChangesAsync, keeping the price update and its history atomic.
    ProductPriceHistory? ApplySalePriceChange(Product product, decimal newPrice, string changedByUserId);

    // Active products with no ProductPriceHistory row in the current calendar month — the
    // monthly review is "database-driven": this is always recomputed, never persisted.
    IQueryable<Product> GetPendingMonthlyReviewQuery();

    Task<int> GetPendingMonthlyReviewCountAsync();

    Task<ProductPriceHistory?> GetLatestPriceChangeAsync(int productId);

    Task<List<ProductPriceHistory>> GetPriceHistoryAsync(int productId);

    // Emails every user in the Manager role about one actual price change. Never throws: a
    // temporary mail failure must not undo the already-committed price change (see IEmailService).
    Task NotifyManagersOfPriceChangeAsync(Product product, ProductPriceHistory change, string changedByName);
}
