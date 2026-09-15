using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.PettyCash;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;

namespace WebApplication1.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<CompanySetting> CompanySettings => Set<CompanySetting>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<UnitOfMeasure> UnitOfMeasures => Set<UnitOfMeasure>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<ProductWarehouseStock> ProductWarehouseStocks => Set<ProductWarehouseStock>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
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

        builder.Entity<Product>()
            .HasIndex(p => p.Sku).IsUnique();

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
            .HasIndex(w => w.Code).IsUnique();

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
            .HasIndex(t => t.TransferNumber).IsUnique();

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

        builder.Entity<StockTransferItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RolePermission>()
            .HasIndex(p => new { p.RoleName, p.PermissionKey }).IsUnique();

        builder.Entity<PurchaseInvoice>()
            .HasIndex(p => p.InvoiceNumber).IsUnique();

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
            .HasIndex(s => s.InvoiceNumber).IsUnique();

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
            .HasIndex(r => r.ReturnNumber).IsUnique();

        builder.Entity<SalesReturn>()
            .HasOne(r => r.SalesInvoice)
            .WithMany()
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
            .HasIndex(r => r.ReturnNumber).IsUnique();

        builder.Entity<PurchaseReturn>()
            .HasOne(r => r.PurchaseInvoice)
            .WithMany()
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
            .HasIndex(a => a.Code).IsUnique();

        builder.Entity<JournalEntry>()
            .HasIndex(j => j.EntryNumber).IsUnique();

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
            .HasIndex(n => n.Name).IsUnique();

        builder.Entity<PettyCashEntry>()
            .HasIndex(e => e.Date);

        builder.Entity<PettyCashEntry>()
            .HasOne(e => e.PettyCashName)
            .WithMany(n => n.Entries)
            .HasForeignKey(e => e.PettyCashNameId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
