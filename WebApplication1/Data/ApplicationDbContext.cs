using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.PettyCash;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext? tenant = null)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public const string TenantIdProperty = "TenantId";

    // Entities that carry a shadow TenantId and are filtered/stamped per tenant. Everything the
    // business owns is here; platform-wide tables (Tenants, plans, billing, backups, roles) are not.
    // The filters read these two members, which EF turns into per-query parameters.
    private int CurrentTenantId => tenant?.TenantId ?? 0;
    // A context built without a tenant context (unit tests, design-time tooling) is unfiltered.
    private bool FiltersBypassed => tenant is null || tenant.IsFilterBypassed;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    // SaaS billing (platform-level tables: scoped by an explicit TenantId column, not the tenant filter).
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantFeatureOverride> TenantFeatureOverrides => Set<TenantFeatureOverride>();
    public DbSet<SubscriptionHistory> SubscriptionHistories => Set<SubscriptionHistory>();
    public DbSet<SaasInvoice> SaasInvoices => Set<SaasInvoice>();
    public DbSet<SaasInvoiceItem> SaasInvoiceItems => Set<SaasInvoiceItem>();
    public DbSet<SaasPayment> SaasPayments => Set<SaasPayment>();
    public DbSet<SaasAuditLog> SaasAuditLogs => Set<SaasAuditLog>();
    public DbSet<SaasNotification> SaasNotifications => Set<SaasNotification>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<DataExportRequest> DataExportRequests => Set<DataExportRequest>();
    public DbSet<DataAgreementAcceptance> DataAgreementAcceptances => Set<DataAgreementAcceptance>();
    public DbSet<PaymentProviderConfig> PaymentProviders => Set<PaymentProviderConfig>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<SaasRefund> SaasRefunds => Set<SaasRefund>();

    public DbSet<CompanySetting> CompanySettings => Set<CompanySetting>();
    public DbSet<BackupSetting> BackupSettings => Set<BackupSetting>();
    public DbSet<DatabaseBackup> DatabaseBackups => Set<DatabaseBackup>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<UnitOfMeasure> UnitOfMeasures => Set<UnitOfMeasure>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<ProductWarehouseStock> ProductWarehouseStocks => Set<ProductWarehouseStock>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<StockTransferPurchaseInvoice> StockTransferPurchaseInvoices => Set<StockTransferPurchaseInvoice>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentItem> StockAdjustmentItems => Set<StockAdjustmentItem>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<UserWarehouse> UserWarehouses => Set<UserWarehouse>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceItem> SalesInvoiceItems => Set<SalesInvoiceItem>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnItem> SalesReturnItems => Set<SalesReturnItem>();

    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<ProductPriceHistory> ProductPriceHistories => Set<ProductPriceHistory>();

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PettyCashName> PettyCashNames => Set<PettyCashName>();
    public DbSet<PettyCashEntry> PettyCashEntries => Set<PettyCashEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // First: declares the shadow TenantId on every tenant entity, which the per-tenant
        // unique indexes below refer to.
        ConfigureTenancy(builder);

        builder.Entity<Product>()
            .HasIndex(TenantIdProperty, "Sku").IsUnique();

        builder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Product>()
            .HasOne(p => p.UnitOfMeasure)
            .WithMany(u => u.Products)
            .HasForeignKey(p => p.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransaction>()
            .HasOne(s => s.Product)
            .WithMany(p => p.StockTransactions)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransaction>()
            .HasOne(s => s.Warehouse)
            .WithMany()
            .HasForeignKey(s => s.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransaction>()
            .HasOne(s => s.Batch)
            .WithMany()
            .HasForeignKey(s => s.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductBatch>()
            .HasIndex(b => new { b.WarehouseId, b.ProductId, b.PurchaseDate });

        builder.Entity<ProductBatch>()
            .HasIndex(b => b.RemainingQuantity);

        builder.Entity<ProductBatch>()
            .HasOne(b => b.Product)
            .WithMany()
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductBatch>()
            .HasOne(b => b.Warehouse)
            .WithMany()
            .HasForeignKey(b => b.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductBatch>()
            .HasOne(b => b.PurchaseInvoiceItem)
            .WithOne(i => i.Batch)
            .HasForeignKey<ProductBatch>(b => b.PurchaseInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Warehouse>()
            .HasIndex(TenantIdProperty, "Code").IsUnique();

        builder.Entity<ProductWarehouseStock>()
            .HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();

        builder.Entity<ProductWarehouseStock>()
            .HasOne(s => s.Product)
            .WithMany(p => p.WarehouseStocks)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductWarehouseStock>()
            .HasOne(s => s.Warehouse)
            .WithMany(w => w.Stocks)
            .HasForeignKey(s => s.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UserWarehouse>()
            .HasKey(uw => new { uw.UserId, uw.WarehouseId });

        builder.Entity<UserWarehouse>()
            .HasOne(uw => uw.User)
            .WithMany(u => u.UserWarehouses)
            .HasForeignKey(uw => uw.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserWarehouse>()
            .HasOne(uw => uw.Warehouse)
            .WithMany()
            .HasForeignKey(uw => uw.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransfer>()
            .HasIndex(TenantIdProperty, "TransferNumber").IsUnique();

        builder.Entity<StockTransfer>()
            .HasOne(t => t.FromWarehouse)
            .WithMany()
            .HasForeignKey(t => t.FromWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransfer>()
            .HasOne(t => t.ToWarehouse)
            .WithMany()
            .HasForeignKey(t => t.ToWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransferItem>()
            .HasOne(i => i.StockTransfer)
            .WithMany(t => t.Items)
            .HasForeignKey(i => i.StockTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockTransferPurchaseInvoice>()
            .HasKey(l => new { l.StockTransferId, l.PurchaseInvoiceId });

        builder.Entity<StockTransferPurchaseInvoice>()
            .HasOne(l => l.StockTransfer)
            .WithMany(t => t.PurchaseInvoiceLinks)
            .HasForeignKey(l => l.StockTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockTransferPurchaseInvoice>()
            .HasOne(l => l.PurchaseInvoice)
            .WithMany()
            .HasForeignKey(l => l.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransferItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockAdjustment>()
            .HasIndex(TenantIdProperty, "AdjustmentNumber").IsUnique();

        builder.Entity<StockAdjustment>()
            .HasOne(a => a.Warehouse)
            .WithMany()
            .HasForeignKey(a => a.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockAdjustmentItem>()
            .HasOne(i => i.StockAdjustment)
            .WithMany(a => a.Items)
            .HasForeignKey(i => i.StockAdjustmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockAdjustmentItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RolePermission>()
            .HasIndex(p => new { p.RoleName, p.PermissionKey }).IsUnique();

        builder.Entity<UserPermission>()
            .HasIndex(p => new { p.UserId, p.PermissionKey }).IsUnique();

        builder.Entity<UserPermission>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseInvoice>()
            .HasIndex(TenantIdProperty, "InvoiceNumber").IsUnique();

        builder.Entity<PurchaseInvoice>()
            .HasOne(p => p.Supplier)
            .WithMany(s => s.PurchaseInvoices)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseInvoice>()
            .HasOne(p => p.Warehouse)
            .WithMany()
            .HasForeignKey(p => p.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseInvoiceItem>()
            .HasOne(i => i.PurchaseInvoice)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseInvoiceItem>()
            .HasOne(i => i.Product)
            .WithMany(p => p.PurchaseInvoiceItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesInvoice>()
            .HasIndex(TenantIdProperty, "InvoiceNumber").IsUnique();

        builder.Entity<SalesInvoice>()
            .HasOne(s => s.Customer)
            .WithMany(c => c.SalesInvoices)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesInvoice>()
            .HasOne(s => s.Warehouse)
            .WithMany()
            .HasForeignKey(s => s.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesInvoiceItem>()
            .HasOne(i => i.SalesInvoice)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SalesInvoiceItem>()
            .HasOne(i => i.Product)
            .WithMany(p => p.SalesInvoiceItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesInvoiceItem>()
            .HasOne(i => i.Batch)
            .WithMany()
            .HasForeignKey(i => i.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesReturn>()
            .HasIndex(TenantIdProperty, "ReturnNumber").IsUnique();

        builder.Entity<SalesReturn>()
            .HasOne(r => r.SalesInvoice)
            .WithMany(s => s.Returns)
            .HasForeignKey(r => r.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesReturnItem>()
            .HasOne(i => i.SalesReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.SalesReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SalesReturnItem>()
            .HasOne(i => i.SalesInvoiceItem)
            .WithMany()
            .HasForeignKey(i => i.SalesInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseReturn>()
            .HasIndex(TenantIdProperty, "ReturnNumber").IsUnique();

        builder.Entity<PurchaseReturn>()
            .HasOne(r => r.PurchaseInvoice)
            .WithMany(p => p.Returns)
            .HasForeignKey(r => r.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseReturnItem>()
            .HasOne(i => i.PurchaseReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.PurchaseReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseReturnItem>()
            .HasOne(i => i.PurchaseInvoiceItem)
            .WithMany()
            .HasForeignKey(i => i.PurchaseInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Account>()
            .HasIndex(TenantIdProperty, "Code").IsUnique();

        builder.Entity<JournalEntry>()
            .HasIndex(TenantIdProperty, "EntryNumber").IsUnique();

        builder.Entity<JournalEntryLine>()
            .HasOne(l => l.JournalEntry)
            .WithMany(j => j.Lines)
            .HasForeignKey(l => l.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<JournalEntryLine>()
            .HasOne(l => l.Account)
            .WithMany(a => a.JournalEntryLines)
            .HasForeignKey(l => l.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Payment>()
            .HasOne(p => p.PurchaseInvoice)
            .WithMany(p => p.Payments)
            .HasForeignKey(p => p.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Payment>()
            .HasOne(p => p.SalesInvoice)
            .WithMany(s => s.Payments)
            .HasForeignKey(p => p.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Set only on a payment against the party's opening balance (no invoice) — see
        // Payment.CustomerId / SupplierId.
        builder.Entity<Payment>()
            .HasOne(p => p.Customer)
            .WithMany(c => c.OpeningBalancePayments)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Payment>()
            .HasOne(p => p.Supplier)
            .WithMany(s => s.OpeningBalancePayments)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductPriceHistory>()
            .HasIndex(h => new { h.ProductId, h.ChangedAt });

        builder.Entity<ProductPriceHistory>()
            .HasOne(h => h.Product)
            .WithMany()
            .HasForeignKey(h => h.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductPriceHistory>()
            .HasOne(h => h.ChangedByUser)
            .WithMany()
            .HasForeignKey(h => h.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PettyCashName>()
            .HasIndex(TenantIdProperty, "Name").IsUnique();

        builder.Entity<PettyCashEntry>()
            .HasIndex(e => e.Date);

        builder.Entity<PettyCashEntry>()
            .HasOne(e => e.PettyCashName)
            .WithMany(n => n.Entries)
            .HasForeignKey(e => e.PettyCashNameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PettyCashEntry>()
            .HasOne(e => e.Warehouse)
            .WithMany()
            .HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

    }

    private static readonly Type[] TenantEntityTypes =
    [
        typeof(CompanySetting), typeof(Category), typeof(UnitOfMeasure), typeof(Product), typeof(StockTransaction),
        typeof(Warehouse), typeof(ProductWarehouseStock), typeof(StockTransfer), typeof(StockTransferItem),
        typeof(StockTransferPurchaseInvoice), typeof(StockAdjustment), typeof(StockAdjustmentItem),
        typeof(UserPermission), typeof(UserWarehouse), typeof(Supplier), typeof(PurchaseInvoice),
        typeof(PurchaseInvoiceItem), typeof(PurchaseReturn), typeof(PurchaseReturnItem), typeof(Customer),
        typeof(SalesInvoice), typeof(SalesInvoiceItem), typeof(SalesReturn), typeof(SalesReturnItem),
        typeof(ProductBatch), typeof(ProductPriceHistory), typeof(Account), typeof(JournalEntry),
        typeof(JournalEntryLine), typeof(Payment), typeof(PettyCashName), typeof(PettyCashEntry)
    ];

    private static readonly HashSet<Type> TenantEntityTypeSet = [.. TenantEntityTypes];

    /// <summary>True for entities that carry a TenantId and are filtered per tenant.</summary>
    public static bool IsTenantEntity(Type clrType) => TenantEntityTypeSet.Contains(clrType);

    private void ConfigureTenancy(ModelBuilder builder)
    {
        builder.Entity<Tenant>(t =>
        {
            t.HasIndex(x => x.Code).IsUnique();
            t.HasIndex(x => x.Status);
            t.HasIndex(x => x.PortalHost).IsUnique().HasFilter("[PortalHost] IS NOT NULL");
        });

        builder.Entity<SubscriptionPlan>(p =>
        {
            p.HasIndex(x => x.Code).IsUnique();
            p.HasMany(x => x.Features).WithOne(f => f.Plan).HasForeignKey(f => f.PlanId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PlanFeature>().HasIndex(f => new { f.PlanId, f.FeatureKey }).IsUnique();

        builder.Entity<TenantSubscription>(s =>
        {
            s.HasIndex(x => x.TenantId).IsUnique();
            s.HasIndex(x => x.Status);
            s.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            s.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<TenantFeatureOverride>().HasIndex(o => new { o.TenantId, o.FeatureKey }).IsUnique();
        builder.Entity<SubscriptionHistory>().HasIndex(h => new { h.TenantId, h.CreatedAt });

        builder.Entity<SaasInvoice>(i =>
        {
            i.HasIndex(x => x.InvoiceNumber).IsUnique();
            i.HasIndex(x => new { x.TenantId, x.Status });
            // One live invoice per subscription billing period: duplicate generation is rejected by the database.
            i.HasIndex(x => new { x.SubscriptionId, x.PeriodStart }).IsUnique().HasFilter("[Status] <> 5 AND [SubscriptionId] IS NOT NULL");
            i.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            i.HasMany(x => x.Items).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            i.HasMany(x => x.Payments).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SaasPayment>(p =>
        {
            p.HasIndex(x => new { x.Provider, x.ProviderTransactionId }).IsUnique().HasFilter("[ProviderTransactionId] IS NOT NULL");
            p.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            p.HasIndex(x => x.TenantId);
        });
        builder.Entity<PaymentProviderConfig>().HasIndex(p => p.Code).IsUnique();
        builder.Entity<PaymentAttempt>(a =>
        {
            a.HasIndex(x => x.Reference).IsUnique();
            a.HasIndex(x => new { x.Status, x.CreatedAt });
            a.HasIndex(x => x.InvoiceId);
            a.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentWebhookEvent>().HasIndex(e => new { e.Provider, e.EventKey }).IsUnique();
        builder.Entity<SaasRefund>(r =>
        {
            r.HasIndex(x => x.PaymentId);
            r.HasIndex(x => x.RefundTransId).IsUnique().HasFilter("[RefundTransId] IS NOT NULL");
        });
        builder.Entity<SaasAuditLog>().HasIndex(a => new { a.TenantId, a.At });
        builder.Entity<DataExportRequest>(r =>
        {
            r.HasIndex(x => new { x.TenantId, x.RequestedAt });
            r.HasIndex(x => x.Status);
            r.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DataAgreementAcceptance>(a =>
        {
            a.HasIndex(x => new { x.TenantId, x.Version });
            a.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SaasNotification>(n =>
        {
            n.HasIndex(x => new { x.TenantId, x.DedupeKey, x.Channel }).IsUnique();
            n.HasIndex(x => new { x.Status, x.Channel });
        });

        // Users belong to one tenant; platform staff have none. Not query-filtered on purpose:
        // cookie validation and sign-in look users up before any tenant is known. User lists are
        // scoped explicitly (UsersController etc.).
        builder.Entity<ApplicationUser>(u =>
        {
            u.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            u.HasIndex(x => x.TenantId);
        });

        var configure = typeof(ApplicationDbContext).GetMethod(nameof(ConfigureTenantEntity), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var type in TenantEntityTypes)
        {
            configure.MakeGenericMethod(type).Invoke(this, [builder]);
        }
    }

    private void ConfigureTenantEntity<T>(ModelBuilder builder) where T : class
    {
        var entity = builder.Entity<T>();
        entity.Property<int>(TenantIdProperty);
        entity.HasIndex(TenantIdProperty);
        entity.HasOne<Tenant>().WithMany().HasForeignKey(TenantIdProperty).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(e => FiltersBypassed || EF.Property<int>(e, TenantIdProperty) == CurrentTenantId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOnSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantOnSave();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // New rows are stamped with the current tenant; any write that would touch another tenant's
    // row (or one with no tenant) is rejected before it reaches the database.
    private void EnforceTenantOnSave()
    {
        if (tenant is null)
        {
            return;
        }

        var current = tenant.TenantId;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || !TenantEntityTypeSet.Contains(entry.Metadata.ClrType))
            {
                continue;
            }

            var property = entry.Property(TenantIdProperty);
            var value = (int)(property.CurrentValue ?? 0);

            if (entry.State == EntityState.Added)
            {
                if (value == 0)
                {
                    if (current == 0)
                    {
                        throw new InvalidOperationException($"Cannot save {entry.Metadata.ClrType.Name}: no tenant in context.");
                    }
                    property.CurrentValue = current;
                }
                else if (!tenant.IsFilterBypassed && value != current)
                {
                    throw new InvalidOperationException($"Cannot save {entry.Metadata.ClrType.Name} for tenant {value} from tenant {current}.");
                }
            }
            else if (!tenant.IsFilterBypassed && value != current)
            {
                throw new InvalidOperationException($"Cross-tenant write blocked on {entry.Metadata.ClrType.Name}.");
            }
            else if (entry.State == EntityState.Modified && property.IsModified && property.OriginalValue is int original && original != value)
            {
                throw new InvalidOperationException("TenantId cannot be changed.");
            }
        }
    }
}
