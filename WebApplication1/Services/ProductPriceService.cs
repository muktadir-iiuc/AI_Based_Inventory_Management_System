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
    ICompanySettingsService companySettings,
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
        var recipients = managers.Where(m => !string.IsNullOrWhiteSpace(m.Email)).ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var company = await companySettings.GetAsync();
        var subject = $"Sale Price Updated: {product.Name} ({product.Sku})";

        logger.LogInformation("Notifying {ManagerCount} manager(s) of price change for product {ProductId}.", recipients.Count, product.Id);

        foreach (var manager in recipients)
        {
            var body = BuildPriceChangeEmailBody(company.CompanyName, manager.FullName, product, change, changedByName);
            await emailService.SendAsync(manager.Email!, subject, body, isBodyHtml: true);
        }
    }

    private static string BuildPriceChangeEmailBody(
        string companyName, string recipientName, Product product, ProductPriceHistory change, string changedByName)
    {
        var direction = change.PriceIncreased ? "increased" : "decreased";
        var changeColor = change.PriceIncreased ? "#15803d" : "#b91c1c";
        var changeSummary = change.PercentChange is decimal percent
            ? $"{direction} by {percent:0.##}%"
            : direction;

        return $"""
            <div style="font-family: 'Segoe UI', Arial, sans-serif; color:#1f2937; max-width:520px; margin:0 auto;">
              <div style="background:#0f766e; padding:16px 24px; border-radius:8px 8px 0 0;">
                <span style="color:#ffffff; font-size:16px; font-weight:600;">{companyName}</span>
              </div>
              <div style="border:1px solid #e5e7eb; border-top:none; border-radius:0 0 8px 8px; padding:24px;">
                <p style="margin:0 0 12px;">Hello {recipientName},</p>
                <p style="margin:0 0 20px; line-height:1.5;">
                  <strong>{changedByName}</strong> {changeSummary} the sale price for
                  <strong>{product.Name}</strong> <span style="color:#6b7280;">({product.Sku})</span>.
                </p>
                <table style="width:100%; border-collapse:collapse; margin-bottom:20px;" cellpadding="0" cellspacing="0">
                  <tr>
                    <td style="padding:10px 12px; background:#f9fafb; border:1px solid #e5e7eb;">Previous Price</td>
                    <td style="padding:10px 12px; border:1px solid #e5e7eb; text-align:right;">{change.OldPrice:C}</td>
                  </tr>
                  <tr>
                    <td style="padding:10px 12px; background:#f9fafb; border:1px solid #e5e7eb;">New Price</td>
                    <td style="padding:10px 12px; border:1px solid #e5e7eb; text-align:right; font-weight:600; color:{changeColor};">{change.NewPrice:C}</td>
                  </tr>
                </table>
                <p style="margin:0 0 4px; color:#6b7280; font-size:13px;">Changed by {changedByName}</p>
                <p style="margin:0 0 24px; color:#6b7280; font-size:13px;">{change.ChangedAt:dddd, dd MMM yyyy hh:mm tt} UTC</p>
                <p style="margin:0; font-size:12px; color:#9ca3af; border-top:1px solid #e5e7eb; padding-top:16px;">
                  This is an automated notification from the {companyName} Inventory Management System. Please do not reply to this email.
                </p>
              </div>
            </div>
            """;
    }

    private static (DateTime MonthStart, DateTime NextMonthStart) CurrentMonthRange()
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        return (monthStart, monthStart.AddMonths(1));
    }
}
