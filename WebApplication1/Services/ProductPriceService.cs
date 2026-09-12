using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Services;

public class ProductPriceService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    ILogger<ProductPriceService> logger) : IProductPriceService
{
    public ProductPriceHistory? ApplySalePriceChange(Product product, decimal newPrice, string changedByUserId)
    {
        if (product.SalePrice == newPrice)
        {
            return null;
        }

        var history = new ProductPriceHistory
        {
            ProductId = product.Id,
            OldPrice = product.SalePrice,
            NewPrice = newPrice,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow
        };
        db.ProductPriceHistories.Add(history);
        product.SalePrice = newPrice;

        logger.LogInformation("Product {ProductId} sale price changed from {OldPrice} to {NewPrice} by user {UserId}.",
            product.Id, history.OldPrice, history.NewPrice, changedByUserId);

        return history;
    }

    public IQueryable<Product> GetPendingMonthlyReviewQuery()
    {
        var (monthStart, nextMonthStart) = CurrentMonthRange();

        return db.Products.Where(p => p.IsActive && !db.ProductPriceHistories
            .Any(h => h.ProductId == p.Id && h.ChangedAt >= monthStart && h.ChangedAt < nextMonthStart));
    }

    public Task<int> GetPendingMonthlyReviewCountAsync() => GetPendingMonthlyReviewQuery().CountAsync();

    public Task<ProductPriceHistory?> GetLatestPriceChangeAsync(int productId) =>
        db.ProductPriceHistories
            .Include(h => h.ChangedByUser)
            .Where(h => h.ProductId == productId)
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefaultAsync();

    public Task<List<ProductPriceHistory>> GetPriceHistoryAsync(int productId) =>
        db.ProductPriceHistories
            .Include(h => h.ChangedByUser)
            .Where(h => h.ProductId == productId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync();

    public async Task NotifyManagersOfPriceChangeAsync(Product product, ProductPriceHistory change, string changedByName)
    {
        var managers = await userManager.GetUsersInRoleAsync(Roles.Manager);
        var subject = $"Product Price Updated - {product.Name}";
        var body =
            $"Product: {product.Name}\n" +
            $"Product Code: {product.Sku}\n\n" +
            $"Previous Price: {change.OldPrice:C}\n" +
            $"New Price: {change.NewPrice:C}\n\n" +
            $"Changed By: {changedByName}\n" +
            $"Changed At: {change.ChangedAt:dd-MMM-yyyy hh:mm tt} UTC";

        logger.LogInformation("Notifying {ManagerCount} manager(s) of price change for product {ProductId}.", managers.Count, product.Id);

        foreach (var manager in managers.Where(m => !string.IsNullOrWhiteSpace(m.Email)))
        {
            await emailService.SendAsync(manager.Email!, subject, body);
        }
    }

    private static (DateTime MonthStart, DateTime NextMonthStart) CurrentMonthRange()
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        return (monthStart, monthStart.AddMonths(1));
    }
}
