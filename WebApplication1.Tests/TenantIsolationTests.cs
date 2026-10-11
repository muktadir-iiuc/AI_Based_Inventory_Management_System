using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

public class TenantIsolationTests
{
    private sealed class FakeTenantContext(int tenantId) : ITenantContext
    {
        public int TenantId { get; private set; } = tenantId;
        public bool IsFilterBypassed { get; private set; }

        public IDisposable UseTenant(int id)
        {
            var previous = TenantId;
            TenantId = id;
            return new Restore(() => TenantId = previous);
        }

        public IDisposable BypassFilters()
        {
            IsFilterBypassed = true;
            return new Restore(() => IsFilterBypassed = false);
        }

        private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }
    }

    private static (DbContextOptions<ApplicationDbContext> Options, FakeTenantContext Tenant) NewDb(int tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return (options, new FakeTenantContext(tenantId));
    }

    private static async Task SeedTwoTenantsAsync(ApplicationDbContext db, FakeTenantContext tenant)
    {
        db.Tenants.AddRange(new Tenant { Id = 1, Name = "A", Code = "a" }, new Tenant { Id = 2, Name = "B", Code = "b" });
        await db.SaveChangesAsync();

        foreach (var id in new[] { 1, 2 })
        {
            using var _ = tenant.UseTenant(id);
            var category = new Category { Name = $"Cat-{id}" };
            var unit = new UnitOfMeasure { Name = "Piece", Symbol = "pc" };
            db.Products.Add(new Product { Sku = "SKU-1", Name = $"Product of tenant {id}", Category = category, UnitOfMeasure = unit });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Queries_only_return_the_current_tenants_rows()
    {
        var (options, tenant) = NewDb(1);
        await using var db = new ApplicationDbContext(options, tenant);
        await SeedTwoTenantsAsync(db, tenant);

        tenant.UseTenant(1);
        var seenByOne = await db.Products.Select(p => p.Name).ToListAsync();
        tenant.UseTenant(2);
        var seenByTwo = await db.Products.Select(p => p.Name).ToListAsync();

        Assert.Equal(["Product of tenant 2"], seenByTwo);
        Assert.Equal(["Product of tenant 1"], seenByOne);
    }

    [Fact]
    public async Task No_tenant_sees_nothing()
    {
        var (options, tenant) = NewDb(1);
        await using var db = new ApplicationDbContext(options, tenant);
        await SeedTwoTenantsAsync(db, tenant);

        tenant.UseTenant(0);
        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.Categories.ToListAsync());
    }

    [Fact]
    public async Task Same_sku_is_allowed_in_different_tenants()
    {
        var (options, tenant) = NewDb(1);
        await using var db = new ApplicationDbContext(options, tenant);
        await SeedTwoTenantsAsync(db, tenant);

        using var _ = tenant.BypassFilters();
        Assert.Equal(2, await db.Products.CountAsync(p => p.Sku == "SKU-1"));
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_current_tenant()
    {
        var (options, tenant) = NewDb(2);
        await using var db = new ApplicationDbContext(options, tenant);
        await SeedTwoTenantsAsync(db, tenant);

        tenant.UseTenant(2);
        db.Warehouses.Add(new Warehouse { Code = "W", Name = "W" });
        await db.SaveChangesAsync();

        using var _ = tenant.BypassFilters();
        var stamped = db.Entry(await db.Warehouses.SingleAsync()).Property<int>(ApplicationDbContext.TenantIdProperty).CurrentValue;
        Assert.Equal(2, stamped);
    }

    [Fact]
    public async Task Saving_without_a_tenant_is_rejected()
    {
        var (options, tenant) = NewDb(0);
        await using var db = new ApplicationDbContext(options, tenant);

        db.Warehouses.Add(new Warehouse { Code = "W", Name = "W" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cannot_modify_another_tenants_row_even_if_loaded_through_a_bypass()
    {
        var (options, tenant) = NewDb(1);
        await using var db = new ApplicationDbContext(options, tenant);
        await SeedTwoTenantsAsync(db, tenant);

        Product otherTenantsProduct;
        using (tenant.BypassFilters())
        {
            otherTenantsProduct = await db.Products.IgnoreQueryFilters().Where(p => p.Name == "Product of tenant 2").SingleAsync();
        }

        tenant.UseTenant(1);
        otherTenantsProduct.Name = "hijacked";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cannot_insert_a_row_for_another_tenant()
    {
        var (options, tenant) = NewDb(1);
        await using var db = new ApplicationDbContext(options, tenant);
        await SeedTwoTenantsAsync(db, tenant);

        tenant.UseTenant(1);
        var warehouse = new Warehouse { Code = "X", Name = "X" };
        db.Warehouses.Add(warehouse);
        db.Entry(warehouse).Property<int>(ApplicationDbContext.TenantIdProperty).CurrentValue = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
