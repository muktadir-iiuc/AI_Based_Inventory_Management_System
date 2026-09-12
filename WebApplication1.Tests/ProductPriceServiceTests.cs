using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebApplication1.Data;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Services;

namespace WebApplication1.Tests;

public class ProductPriceServiceTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static ProductPriceService CreateService(
        ApplicationDbContext db, out Mock<UserManager<ApplicationUser>> userManager, out Mock<IEmailService> emailService)
    {
        userManager = MockUserManager();
        emailService = new Mock<IEmailService>();
        var companySettings = new Mock<ICompanySettingsService>();
        companySettings.Setup(c => c.GetAsync()).ReturnsAsync(new CompanySetting { CompanyName = "Test Co" });
        return new ProductPriceService(db, userManager.Object, emailService.Object, companySettings.Object, NullLogger<ProductPriceService>.Instance);
    }

    private static Product SeedProduct(ApplicationDbContext db, decimal salePrice = 100m)
    {
        var product = new Product { Sku = "SKU-1", Name = "Test Product", SalePrice = salePrice, IsActive = true };
        db.Products.Add(product);
        db.SaveChanges();
        return product;
    }

    // Test 1: no current-month history -> pending.
    [Fact]
    public async Task PendingMonthlyReview_ProductWithNoHistory_IsPending()
    {
        using var db = NewDb();
        var service = CreateService(db, out _, out _);
        var product = SeedProduct(db);

        var pending = await service.GetPendingMonthlyReviewQuery().ToListAsync();

        Assert.Contains(pending, p => p.Id == product.Id);
    }

    // Test 2: has current-month history -> not pending.
    [Fact]
    public async Task PendingMonthlyReview_ProductWithCurrentMonthHistory_IsNotPending()
    {
        using var db = NewDb();
        var service = CreateService(db, out _, out _);
        var product = SeedProduct(db);
        db.ProductPriceHistories.Add(new ProductPriceHistory
        {
            ProductId = product.Id, OldPrice = 100, NewPrice = 110, ChangedByUserId = "u1", ChangedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var pending = await service.GetPendingMonthlyReviewQuery().ToListAsync();

        Assert.DoesNotContain(pending, p => p.Id == product.Id);
    }

    // Test 6: last month's update does not satisfy the current month.
    [Fact]
    public async Task PendingMonthlyReview_OnlyPreviousMonthHistory_IsPending()
    {
        using var db = NewDb();
        var service = CreateService(db, out _, out _);
        var product = SeedProduct(db);
        var lastMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddDays(-1);
        db.ProductPriceHistories.Add(new ProductPriceHistory
        {
            ProductId = product.Id, OldPrice = 90, NewPrice = 100, ChangedByUserId = "u1", ChangedAt = lastMonth
        });
        db.SaveChanges();

        var pending = await service.GetPendingMonthlyReviewQuery().ToListAsync();

        Assert.Contains(pending, p => p.Id == product.Id);
    }

    // Test 3: unchanged price creates no history.
    [Fact]
    public void ApplySalePriceChange_SamePrice_ReturnsNullAndCreatesNoHistory()
    {
        using var db = NewDb();
        var service = CreateService(db, out _, out _);
        var product = SeedProduct(db, salePrice: 100m);

        var result = service.ApplySalePriceChange(product, 100m, "user-1");

        Assert.Null(result);
        Assert.Empty(db.ProductPriceHistories);
        Assert.Equal(100m, product.SalePrice);
    }

    // Test 4: an actual price change is recorded with old/new price and the acting user.
    [Fact]
    public async Task ApplySalePriceChange_DifferentPrice_CreatesHistoryAndUpdatesProduct()
    {
        using var db = NewDb();
        var service = CreateService(db, out _, out _);
        var product = SeedProduct(db, salePrice: 100m);

        var result = service.ApplySalePriceChange(product, 120m, "user-1");
        await db.SaveChangesAsync();

        Assert.NotNull(result);
        Assert.Equal(100m, result!.OldPrice);
        Assert.Equal(120m, result.NewPrice);
        Assert.Equal("user-1", result.ChangedByUserId);
        Assert.Equal(120m, product.SalePrice);
        Assert.Single(db.ProductPriceHistories);
    }

    // Test 5: multiple changes to the same product in one month are all recorded, not overwritten.
    [Fact]
    public async Task ApplySalePriceChange_MultipleChangesSameMonth_RecordsEveryChange()
    {
        using var db = NewDb();
        var service = CreateService(db, out _, out _);
        var product = SeedProduct(db, salePrice: 100m);

        await db.SaveChangesAsync(); // no-op change should not count
        var first = service.ApplySalePriceChange(product, 110m, "user-1");
        await db.SaveChangesAsync();
        var second = service.ApplySalePriceChange(product, 115m, "user-2");
        await db.SaveChangesAsync();
        var third = service.ApplySalePriceChange(product, 120m, "user-1");
        await db.SaveChangesAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(3, await db.ProductPriceHistories.CountAsync(h => h.ProductId == product.Id));
        Assert.Equal(120m, product.SalePrice);
    }

    // Test 5/8: every actual price change sends the Manager(s) an email with the required fields.
    [Fact]
    public async Task NotifyManagersOfPriceChangeAsync_SendsOneEmailPerManager_WithRequiredDetails()
    {
        using var db = NewDb();
        var service = CreateService(db, out var userManager, out var emailService);
        var product = SeedProduct(db, salePrice: 100m);
        var change = service.ApplySalePriceChange(product, 150m, "user-1")!;
        await db.SaveChangesAsync();

        var managers = new List<ApplicationUser>
        {
            new() { Id = "m1", Email = "manager1@example.com" },
            new() { Id = "m2", Email = "manager2@example.com" }
        };
        userManager.Setup(m => m.GetUsersInRoleAsync(Roles.Manager)).ReturnsAsync(managers);

        string? capturedSubject = null;
        string? capturedBody = null;
        emailService
            .Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, string, bool>((_, subject, body, _) => { capturedSubject = subject; capturedBody = body; })
            .Returns(Task.CompletedTask);

        await service.NotifyManagersOfPriceChangeAsync(product, change, "Mohammad");

        emailService.Verify(e => e.SendAsync("manager1@example.com", It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);
        emailService.Verify(e => e.SendAsync("manager2@example.com", It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);
        Assert.Contains(product.Name, capturedSubject);
        Assert.Contains("100", capturedBody);
        Assert.Contains("150", capturedBody);
        Assert.Contains("Mohammad", capturedBody);
        Assert.Contains("increased by 50%", capturedBody);
    }
}
