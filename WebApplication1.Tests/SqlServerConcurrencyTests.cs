using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

/// <summary>Runs only when IMS_TEST_SQL names a SQL Server (connection string without a Database): the database's unique indexes and row versions are what actually stop duplicate callbacks.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvVar = "IMS_TEST_SQL";
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"Set {EnvVar} to a SQL Server connection string (no Database=) to run concurrency tests against real SQL Server.";
        }
    }
}

public class SqlServerConcurrencyTests
{
    private sealed class Tenant1 : ITenantContext
    {
        public int TenantId => 1;
        public bool IsFilterBypassed => true;
        public IDisposable UseTenant(int tenantId) => new Noop();
        public IDisposable BypassFilters() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    }

    private static DbContextOptions<ApplicationDbContext> Options(string database) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar)!)
                { InitialCatalog = database, TrustServerCertificate = true, MultipleActiveResultSets = true }.ConnectionString)
            .Options;

    private static (ApplicationDbContext Db, IBillingPaymentService Payments) NewStack(DbContextOptions<ApplicationDbContext> options)
    {
        var db = new ApplicationDbContext(options, new Tenant1());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var time = new Clock();
        var settings = new PlatformSettingsService(db, cache);
        var audit = new SaasAuditService(db, new HttpContextAccessor());
        var notifier = new SaasNotifier(db);
        var invoices = new InvoiceService(db, settings, notifier, audit, time);
        var subs = new SubscriptionService(db, invoices, settings, notifier, audit, cache, time);
        return (db, new BillingPaymentService(db, subs, notifier, audit, time));
    }

    private static async Task<(string Database, int InvoiceId)> CreateDatabaseWithInvoiceAsync()
    {
        var database = "IMS_ConcurrencyTest_" + Guid.NewGuid().ToString("N")[..10];
        var options = Options(database);
        var (db, _) = NewStack(options);
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Name = "Acme", Code = "acme", ContactEmail = "a@acme.test" });
        db.SubscriptionPlans.Add(new SubscriptionPlan { Code = "basic", Name = "Basic", Price = 1000, TaxPercent = 5, DiscountPercent = 10, GraceDays = 5 });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var time = new Clock();
        var settings = new PlatformSettingsService(db, cache);
        var audit = new SaasAuditService(db, new HttpContextAccessor());
        var notifier = new SaasNotifier(db);
        var invoices = new InvoiceService(db, settings, notifier, audit, time);
        var subs = new SubscriptionService(db, invoices, settings, notifier, audit, cache, time);
        await subs.StartAsync(db.Tenants.Single().Id, db.SubscriptionPlans.Single().Id, StartMode.InvoiceFirst);
        var invoiceId = (await db.SaasInvoices.SingleAsync()).Id;
        await db.DisposeAsync();
        return (database, invoiceId);
    }

    private static async Task DropAsync(string database)
    {
        await using var db = new ApplicationDbContext(Options(database), new Tenant1());
        await db.Database.EnsureDeletedAsync();
    }

    [SqlServerFact]
    public async Task Many_simultaneous_callbacks_for_the_same_transaction_record_exactly_one_payment()
    {
        var (database, invoiceId) = await CreateDatabaseWithInvoiceAsync();
        try
        {
            var options = Options(database);
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                await Task.Yield();
                var (db, payments) = NewStack(options);
                await using (db)
                {
                    return await payments.ApplyPaymentAsync(new PaymentRequest(invoiceId, 945m, "BDT", "bKash", "sslcommerz",
                        ProviderTransactionId: "BANK-RACE", IdempotencyKey: "PA-RACE", ExpectedTenantId: 1));
                }
            }));

            Assert.Equal(1, results.Count(r => r.Outcome == PaymentOutcome.Applied));
            Assert.All(results, r => Assert.True(r.Succeeded, r.Message));

            await using var check = new ApplicationDbContext(Options(database), new Tenant1());
            Assert.Equal(1, await check.SaasPayments.CountAsync());
            var invoice = await check.SaasInvoices.SingleAsync();
            Assert.Equal(945m, invoice.AmountPaid);
            Assert.Equal(InvoiceStatus.Paid, invoice.Status);
            Assert.Equal(1, await check.SubscriptionHistories.CountAsync(h => h.Event == "subscription.renewed" || h.Event == "subscription.reactivated"));
            var sub = await check.TenantSubscriptions.SingleAsync();
            Assert.Equal(SubscriptionStatus.Active, sub.Status);
            Assert.Equal(invoice.PeriodEnd, sub.CurrentPeriodEnd);
        }
        finally { await DropAsync(database); }
    }

    [SqlServerFact]
    public async Task Two_different_payments_racing_for_one_invoice_cannot_both_be_applied()
    {
        var (database, invoiceId) = await CreateDatabaseWithInvoiceAsync();
        try
        {
            var options = Options(database);
            var results = await Task.WhenAll(new[] { "TXN-A", "TXN-B", "TXN-C", "TXN-D" }.Select(async txn =>
            {
                await Task.Yield();
                var (db, payments) = NewStack(options);
                await using (db)
                {
                    return await payments.ApplyPaymentAsync(new PaymentRequest(invoiceId, 945m, "BDT", "Card", "sslcommerz", ProviderTransactionId: txn, IdempotencyKey: "KEY-" + txn, ExpectedTenantId: 1));
                }
            }));

            Assert.Equal(1, results.Count(r => r.Outcome == PaymentOutcome.Applied));
            await using var check = new ApplicationDbContext(Options(database), new Tenant1());
            Assert.Equal(945m, (await check.SaasInvoices.SingleAsync()).AmountPaid);          // never double-counted
            Assert.Equal(1, await check.SaasPayments.CountAsync(p => p.Status == SaasPaymentStatus.Succeeded));
        }
        finally { await DropAsync(database); }
    }

    [SqlServerFact]
    public async Task The_database_itself_refuses_a_second_invoice_for_the_same_period_and_a_second_transaction_id()
    {
        var (database, invoiceId) = await CreateDatabaseWithInvoiceAsync();
        try
        {
            await using var db = new ApplicationDbContext(Options(database), new Tenant1());
            var first = await db.SaasInvoices.AsNoTracking().SingleAsync();

            db.SaasInvoices.Add(new SaasInvoice { InvoiceNumber = "DUP", TenantId = 1, SubscriptionId = first.SubscriptionId, PeriodStart = first.PeriodStart, PeriodEnd = first.PeriodEnd, DueDate = first.DueDate, Currency = "BDT", PlanName = "Basic" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.SaasPayments.Add(new SaasPayment { InvoiceId = invoiceId, TenantId = 1, Amount = 1, Currency = "BDT", Provider = "sslcommerz", ProviderTransactionId = "SAME" });
            await db.SaveChangesAsync();
            db.SaasPayments.Add(new SaasPayment { InvoiceId = invoiceId, TenantId = 1, Amount = 1, Currency = "BDT", Provider = "sslcommerz", ProviderTransactionId = "SAME" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally { await DropAsync(database); }
    }
}
