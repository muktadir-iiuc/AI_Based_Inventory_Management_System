using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebApplication1.Data;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

/// <summary>Regression guards for the ways tenant data can leak around a global query filter.</summary>
public class TenantLeakageTests
{
    private sealed class Ctx : ITenantContext
    {
        private int _id; private int _bypass;
        public Ctx(int id) => _id = id;
        public int TenantId => _id;
        public bool IsFilterBypassed => _bypass > 0;
        public IDisposable UseTenant(int t) { var o = _id; _id = t; return new Undo(() => _id = o); }
        public IDisposable BypassFilters() { _bypass++; return new Undo(() => _bypass--); }
        private sealed class Undo(Action a) : IDisposable { public void Dispose() => a(); }
    }

    private static async Task<(ApplicationDbContext Db, Ctx Tenant, int ProductOfOne, int ProductOfTwo)> TwoTenantsAsync()
    {
        var tenant = new Ctx(0);
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        db.Tenants.AddRange(new Tenant { Id = 1, Name = "A", Code = "a" }, new Tenant { Id = 2, Name = "B", Code = "b" });
        await db.SaveChangesAsync();
        var ids = new List<int>();
        foreach (var id in new[] { 1, 2 })
        {
            using var _ = tenant.UseTenant(id);
            db.Products.Add(new Product { Sku = "S", Name = $"P{id}", Category = new Category { Name = "C" }, UnitOfMeasure = new UnitOfMeasure { Name = "Piece", Symbol = "pc" } });
            await db.SaveChangesAsync();
            ids.Add((await db.Products.SingleAsync()).Id);
        }
        return (db, tenant, ids[0], ids[1]);
    }

    // The controllers load records by id with FindAsync. If Find skipped the tenant filter, any logged-in user could open another
    // business's record by guessing ids.
    [Fact]
    public async Task FindAsync_by_id_cannot_return_another_tenants_row()
    {
        var (db, tenant, productOfOne, productOfTwo) = await TwoTenantsAsync();
        db.ChangeTracker.Clear();

        using (tenant.UseTenant(1))
        {
            Assert.NotNull(await db.Products.FindAsync(productOfOne));
            Assert.Null(await db.Products.FindAsync(productOfTwo));
        }
        db.ChangeTracker.Clear();
        using (tenant.UseTenant(2))
        {
            Assert.Null(await db.Products.FindAsync(productOfOne));
        }
    }

    [Fact]
    public async Task Navigation_includes_never_pull_in_another_tenants_children()
    {
        var (db, tenant, _, _) = await TwoTenantsAsync();
        using (tenant.UseTenant(1))
        {
            var cat = await db.Categories.SingleAsync();
            Assert.Equal(1, await db.Products.CountAsync(p => p.CategoryId == cat.Id));
            Assert.Single(await db.Categories.Include(c => c.Products).ToListAsync());
        }
    }

    // Roles are global rows, so "all managers" spans every tenant. A price change in one business must not email the others.
    [Fact]
    public async Task Price_change_emails_go_only_to_managers_of_the_same_tenant()
    {
        var tenant = new Ctx(1);
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        db.Tenants.AddRange(new Tenant { Id = 1, Name = "A", Code = "a" }, new Tenant { Id = 2, Name = "B", Code = "b" });
        await db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(new Mock<IUserStore<ApplicationUser>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(m => m.GetUsersInRoleAsync(Roles.Manager)).ReturnsAsync(
        [
            new ApplicationUser { Id = "m1", Email = "mine@a.test", TenantId = 1 },
            new ApplicationUser { Id = "m2", Email = "other@b.test", TenantId = 2 }
        ]);
        var email = new Mock<IEmailService>();
        var company = new Mock<ICompanySettingsService>();
        company.Setup(c => c.GetAsync()).ReturnsAsync(new CompanySetting { CompanyName = "A Co" });
        var service = new ProductPriceService(db, userManager.Object, email.Object, company.Object, NullLogger<ProductPriceService>.Instance, tenant);

        var product = new Product { Id = 5, Sku = "S", Name = "P", SalePrice = 100m };
        var change = service.ApplySalePriceChange(product, 150m, "u1")!;
        await service.NotifyManagersOfPriceChangeAsync(product, change, "someone");

        email.Verify(e => e.SendAsync("mine@a.test", It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);
        email.Verify(e => e.SendAsync("other@b.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Live_notification_groups_are_namespaced_per_tenant()
    {
        Assert.NotEqual(WebApplication1.Hubs.NotificationsHub.AllWarehousesGroup(1), WebApplication1.Hubs.NotificationsHub.AllWarehousesGroup(2));
        Assert.NotEqual(WebApplication1.Hubs.NotificationsHub.WarehouseGroup(1, 7), WebApplication1.Hubs.NotificationsHub.WarehouseGroup(2, 7));
    }

    [Fact]
    public async Task Every_tenant_scoped_entity_in_the_model_has_the_filter_and_a_tenant_key()
    {
        var (db, _, _, _) = await TwoTenantsAsync();
        var scoped = db.Model.GetEntityTypes().Where(t => ApplicationDbContext.IsTenantEntity(t.ClrType)).ToList();
        Assert.True(scoped.Count >= 30);
        foreach (var t in scoped)
        {
            Assert.NotNull(t.GetQueryFilter());
            Assert.NotNull(t.FindProperty(ApplicationDbContext.TenantIdProperty));
        }

        // Anything that is "business data" must be in that list: every entity whose type lives in an inventory/sales/purchase/accounting/petty-cash namespace.
        var businessNamespaces = new[] { "WebApplication1.Models.Inventory", "WebApplication1.Models.Sales", "WebApplication1.Models.Purchase", "WebApplication1.Models.Accounting", "WebApplication1.Models.PettyCash" };
        var unscoped = db.Model.GetEntityTypes().Where(t => businessNamespaces.Contains(t.ClrType.Namespace) && !ApplicationDbContext.IsTenantEntity(t.ClrType)).Select(t => t.ClrType.Name).ToList();
        Assert.Empty(unscoped);
    }
}
