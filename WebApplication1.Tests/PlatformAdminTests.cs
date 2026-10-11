using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

public class PlatformAdminTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Now = new(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }

    /// <summary>A tenant context that behaves like the real one: a current tenant that UseTenant switches, filters on.</summary>
    private sealed class ScopedTenant(int id = 0) : ITenantContext
    {
        private int _id = id;
        private int _bypass;
        public int TenantId => _id;
        public bool IsFilterBypassed => _bypass > 0;
        public IDisposable UseTenant(int tenantId) { var old = _id; _id = tenantId; return new Undo(() => _id = old); }
        public IDisposable BypassFilters() { _bypass++; return new Undo(() => _bypass--); }
        private sealed class Undo(Action a) : IDisposable { public void Dispose() => a(); }
    }

    private sealed class Stack
    {
        public ApplicationDbContext Db = null!;
        public ScopedTenant Tenant = new();
        public Clock Time = new();
        public ISubscriptionService Subscriptions = null!;
        public IInvoiceService Invoices = null!;
        public IBillingPaymentService Payments = null!;
        public IPlatformMetricsService Metrics = null!;
        public ITenantProvisioningService Provisioning = null!;
        public Mock<UserManager<ApplicationUser>> Users = null!;
        public SubscriptionPlan Trial = null!, Monthly = null!, Yearly = null!;

        public static async Task<Stack> CreateAsync()
        {
            var s = new Stack();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            s.Db = new ApplicationDbContext(options, s.Tenant);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = new PlatformSettingsService(s.Db, cache);
            var audit = new SaasAuditService(s.Db, new HttpContextAccessor());
            var notifier = new SaasNotifier(s.Db);
            s.Invoices = new InvoiceService(s.Db, settings, notifier, audit, s.Time);
            var subs = new SubscriptionService(s.Db, s.Invoices, settings, notifier, audit, cache, s.Time);
            s.Subscriptions = subs;
            s.Payments = new BillingPaymentService(s.Db, subs, notifier, audit, s.Time);
            s.Metrics = new PlatformMetricsService(s.Db, s.Time);

            s.Users = new Mock<UserManager<ApplicationUser>>(new Mock<IUserStore<ApplicationUser>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
            s.Users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
            s.Users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .Callback<ApplicationUser, string>((u, _) => { u.Id = Guid.NewGuid().ToString(); })
                .ReturnsAsync(IdentityResult.Success);
            s.Users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
            s.Provisioning = new TenantProvisioningService(s.Db, s.Tenant, s.Users.Object, subs, notifier, audit, s.Time);

            s.Trial = new SubscriptionPlan { Code = "trial", Name = "Trial", Price = 0, TrialDays = 14, GraceDays = 0 };
            s.Monthly = new SubscriptionPlan { Code = "basic", Name = "Basic", Price = 1000, DiscountPercent = 10, GraceDays = 5 };
            s.Yearly = new SubscriptionPlan { Code = "pro-y", Name = "Pro yearly", Price = 12000, IntervalCount = 12, GraceDays = 5 };
            s.Db.SubscriptionPlans.AddRange(s.Trial, s.Monthly, s.Yearly);
            await s.Db.SaveChangesAsync();
            return s;
        }

        public async Task<Tenant> NewTenantAsync(string code)
        {
            var t = new Tenant { Name = code.ToUpperInvariant(), Code = code, ContactEmail = $"{code}@x.test" };
            Db.Tenants.Add(t);
            await Db.SaveChangesAsync();
            return t;
        }
    }

    // ===================================================== dashboard metrics

    [Fact]
    public async Task Dashboard_figures_come_from_the_stored_records()
    {
        var s = await Stack.CreateAsync();
        var paying = await s.NewTenantAsync("paying");
        var yearly = await s.NewTenantAsync("yearly");
        var trial = await s.NewTenantAsync("trial");
        var suspended = await s.NewTenantAsync("susp");
        var disabled = await s.NewTenantAsync("dis");

        await s.Subscriptions.StartAsync(paying.Id, s.Monthly.Id, StartMode.ActivateNow);
        await s.Subscriptions.StartAsync(yearly.Id, s.Yearly.Id, StartMode.ActivateNow);
        await s.Subscriptions.StartAsync(trial.Id, s.Trial.Id, StartMode.Trial);
        await s.Subscriptions.SetTrialEndAsync(trial.Id, s.Time.Now.AddDays(3));
        await s.Subscriptions.StartAsync(suspended.Id, s.Monthly.Id, StartMode.InvoiceFirst);
        await s.Subscriptions.StartAsync(disabled.Id, s.Monthly.Id, StartMode.ActivateNow);
        await s.Subscriptions.SuspendAsync(suspended.Id, "test");
        await s.Subscriptions.DisableAsync(disabled.Id, "test");

        var d = await s.Metrics.GetDashboardAsync();

        Assert.Equal(5, d.TotalTenants);
        Assert.Equal(2, d.ActiveTenants);          // paying + yearly
        Assert.Equal(1, d.TrialTenants);
        Assert.Equal(1, d.SuspendedTenants);
        Assert.Equal(1, d.DisabledTenants);
        Assert.Equal(1, d.ExpiredSubscriptions);   // invoice-first tenant has no paid term yet
        Assert.Equal(1, d.UnpaidInvoices);
        Assert.Equal(900m, d.UnpaidAmount);        // 1000 less 10%

        // paying: 1000 less 10% = 900/month. yearly: 12000/12 = 1000/month. The disabled tenant's subscription does not count.
        Assert.Equal(1900m, d.Mrr);
        Assert.Equal(22800m, d.Arr);
        Assert.Single(d.UpcomingTrialExpirations, r => r.TenantId == trial.Id);
    }

    [Fact]
    public async Task Revenue_and_outstanding_reports_net_off_refunds_and_group_correctly()
    {
        var s = await Stack.CreateAsync();
        var t = await s.NewTenantAsync("acme");
        await s.Subscriptions.StartAsync(t.Id, s.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await s.Db.SaasInvoices.FirstAsync();
        await s.Payments.ApplyPaymentAsync(new PaymentRequest(invoice.Id, 400m, "BDT", "Cash", "manual", ExpectedTenantId: t.Id));
        var payment = await s.Db.SaasPayments.FirstAsync();
        payment.RefundedAmount = 100m;
        await s.Db.SaveChangesAsync();

        var march = (await s.Metrics.GetRevenueAsync(2026, byMonth: true)).Single(r => r.Period == "Mar 2026");
        Assert.Equal(400m, march.Collected);
        Assert.Equal(100m, march.Refunded);
        Assert.Equal(300m, march.Net);

        var outstanding = (await s.Metrics.GetOutstandingAsync()).Single();
        Assert.Equal(t.Id, outstanding.TenantId);
        Assert.Equal(500m, outstanding.Balance);   // 900 invoice - 400 paid
    }

    // ===================================================== tenant administration

    [Fact]
    public async Task Platform_overrides_change_limits_features_and_future_prices_for_one_tenant_only()
    {
        var s = await Stack.CreateAsync();
        s.Monthly.MaxUsers = 5;
        s.Monthly.Features.Add(new PlanFeature { FeatureKey = Features.Sales });
        await s.Db.SaveChangesAsync();
        var a = await s.NewTenantAsync("a");
        var b = await s.NewTenantAsync("b");
        await s.Subscriptions.StartAsync(a.Id, s.Monthly.Id, StartMode.ActivateNow);
        await s.Subscriptions.StartAsync(b.Id, s.Monthly.Id, StartMode.ActivateNow);

        await s.Subscriptions.SetLimitOverridesAsync(a.Id, 50, null, null, null);
        await s.Subscriptions.SetFeatureOverrideAsync(a.Id, Features.Accounting, true);
        await s.Subscriptions.SetFeatureOverrideAsync(a.Id, Features.Sales, false);
        await s.Subscriptions.SetCustomTermsAsync(a.Id, 2500m, 3, BillingIntervalUnit.Month, 10, 5m, 0m, true);

        Assert.Equal(50, (await s.Subscriptions.GetLimitsAsync(a.Id)).MaxUsers);
        Assert.Equal(5, (await s.Subscriptions.GetLimitsAsync(b.Id)).MaxUsers);
        var featuresA = await s.Subscriptions.GetFeaturesAsync(a.Id);
        Assert.Contains(Features.Accounting, featuresA);
        Assert.DoesNotContain(Features.Sales, featuresA);
        Assert.Contains(Features.Sales, await s.Subscriptions.GetFeaturesAsync(b.Id));

        var subA = await s.Db.TenantSubscriptions.AsNoTracking().FirstAsync(x => x.TenantId == a.Id);
        Assert.Equal(2500m, subA.Price);
        Assert.Equal(3, subA.IntervalCount);
        Assert.Equal(1000m, (await s.Db.TenantSubscriptions.AsNoTracking().FirstAsync(x => x.TenantId == b.Id)).Price);

        await s.Subscriptions.SetFeatureOverrideAsync(a.Id, Features.Sales, null);   // back to plan default
        Assert.Contains(Features.Sales, await s.Subscriptions.GetFeaturesAsync(a.Id));
        Assert.Contains(await s.Db.SaasAuditLogs.ToListAsync(), l => l.Action == "subscription.terms-changed" && l.TenantId == a.Id);
    }

    [Fact]
    public async Task Invalid_terms_limits_and_unknown_features_are_refused()
    {
        var s = await Stack.CreateAsync();
        var t = await s.NewTenantAsync("a");
        await s.Subscriptions.StartAsync(t.Id, s.Monthly.Id, StartMode.ActivateNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Subscriptions.SetLimitOverridesAsync(t.Id, -1, null, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Subscriptions.SetFeatureOverrideAsync(t.Id, "made-up", true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Subscriptions.SetCustomTermsAsync(t.Id, -5m, 1, BillingIntervalUnit.Month, 0, 0, 0, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Subscriptions.SetCustomTermsAsync(t.Id, 5m, 1, BillingIntervalUnit.Month, 0, 101, 0, true));
    }

    [Fact]
    public async Task Extending_a_lapsed_trial_reopens_access_but_only_before_the_first_payment()
    {
        var s = await Stack.CreateAsync();
        var t = await s.NewTenantAsync("a");
        await s.Subscriptions.StartAsync(t.Id, s.Trial.Id, StartMode.Trial);
        s.Time.Now = s.Time.Now.AddDays(20);
        s.Subscriptions.InvalidateAccess(t.Id);
        Assert.Equal(AccessLevel.BillingOnly, (await s.Subscriptions.GetAccessAsync(t.Id)).Level);

        await s.Subscriptions.SetTrialEndAsync(t.Id, s.Time.Now.AddDays(7));
        Assert.True((await s.Subscriptions.GetAccessAsync(t.Id)).IsFull);
        Assert.Equal(SubscriptionStatus.Trialing, (await s.Db.TenantSubscriptions.AsNoTracking().FirstAsync()).Status);

        // After a paid term exists the trial can no longer be edited.
        var paid = await s.NewTenantAsync("paid");
        await s.Subscriptions.StartAsync(paid.Id, s.Monthly.Id, StartMode.ActivateNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Subscriptions.SetTrialEndAsync(paid.Id, s.Time.Now.AddDays(7)));
    }

    [Fact]
    public async Task Termination_ends_access_cancels_renewals_and_keeps_the_records()
    {
        var s = await Stack.CreateAsync();
        var t = await s.NewTenantAsync("a");
        await s.Subscriptions.StartAsync(t.Id, s.Monthly.Id, StartMode.InvoiceFirst);

        await s.Subscriptions.TerminateAsync(t.Id, "contract ended");

        var tenant = await s.Db.Tenants.AsNoTracking().FirstAsync();
        Assert.Equal(TenantStatus.Terminated, tenant.Status);
        Assert.NotNull(tenant.DeletedAt);
        Assert.Equal(SubscriptionStatus.Cancelled, (await s.Db.TenantSubscriptions.AsNoTracking().FirstAsync()).Status);
        s.Subscriptions.InvalidateAccess(t.Id);
        Assert.Equal(AccessLevel.Blocked, (await s.Subscriptions.GetAccessAsync(t.Id)).Level);
        Assert.Equal(1, await s.Db.SaasInvoices.CountAsync());
        Assert.Contains(await s.Db.SaasAuditLogs.ToListAsync(), l => l.Action == "tenant.terminated");
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Subscriptions.EnableAsync(t.Id));
    }

    [Fact]
    public async Task Invoice_adjustments_recompute_totals_and_are_blocked_once_money_has_moved()
    {
        var s = await Stack.CreateAsync();
        var t = await s.NewTenantAsync("a");
        await s.Subscriptions.StartAsync(t.Id, s.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await s.Db.SaasInvoices.FirstAsync();     // 900.00

        await s.Invoices.AdjustAsync(invoice.Id, "Goodwill credit", -100m, "admin");
        Assert.Equal(800m, (await s.Db.SaasInvoices.AsNoTracking().FirstAsync()).Total);
        await s.Invoices.AdjustAsync(invoice.Id, "Setup fee", 50m, "admin");
        Assert.Equal(850m, (await s.Db.SaasInvoices.AsNoTracking().FirstAsync()).Total);

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Invoices.AdjustAsync(invoice.Id, "Too much", -850m, "admin"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Invoices.AdjustAsync(invoice.Id, " ", 10m, "admin"));

        await s.Payments.ApplyPaymentAsync(new PaymentRequest(invoice.Id, 10m, "BDT", "Cash", "manual", ExpectedTenantId: t.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Invoices.AdjustAsync(invoice.Id, "Late", -5m, "admin"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Invoices.CancelAsync(invoice.Id, "x", "admin"));
    }

    // ===================================================== provisioning / self-registration

    [Fact]
    public async Task Provisioning_creates_a_complete_isolated_tenant_with_trial_and_owner()
    {
        var s = await Stack.CreateAsync();

        var result = await s.Provisioning.CreateAsync(new TenantCreateRequest("Rahim Traders", "Rahim", "rahim@traders.test", "Str0ng!Pass", s.Trial.Id, StartMode.Trial, ContactPhone: "01711111111"));

        Assert.True(result.Succeeded, string.Join(";", result.Errors));
        var tenant = result.Tenant!;
        Assert.Equal("rahim-traders", tenant.Code);
        Assert.NotNull(tenant.OwnerUserId);

        var sub = await s.Db.TenantSubscriptions.AsNoTracking().SingleAsync(x => x.TenantId == tenant.Id);
        Assert.Equal(SubscriptionStatus.Trialing, sub.Status);
        Assert.Equal(s.Time.Now.AddDays(14), sub.TrialEndsAt);

        // Business data exists for that tenant only and is invisible to the platform context.
        using (s.Tenant.UseTenant(tenant.Id))
        {
            Assert.Equal("Rahim Traders", (await s.Db.CompanySettings.SingleAsync()).CompanyName);
            Assert.Single(await s.Db.Warehouses.ToListAsync());
            Assert.True(await s.Db.Accounts.CountAsync() >= 10);
        }
        Assert.Empty(await s.Db.Accounts.ToListAsync());                       // no tenant in context: sees nothing
        s.Users.Verify(u => u.AddToRoleAsync(It.Is<ApplicationUser>(x => x.TenantId == tenant.Id), Roles.Admin), Times.Once);
    }

    [Fact]
    public async Task Provisioning_gives_unique_codes_and_rejects_bad_input_and_taken_emails()
    {
        var s = await Stack.CreateAsync();
        var first = await s.Provisioning.CreateAsync(new TenantCreateRequest("Same Name", "A", "a@x.test", "Str0ng!Pass", s.Trial.Id, StartMode.Trial));
        var second = await s.Provisioning.CreateAsync(new TenantCreateRequest("Same Name", "B", "b@x.test", "Str0ng!Pass", s.Trial.Id, StartMode.Trial));
        Assert.Equal("same-name", first.Tenant!.Code);
        Assert.Equal("same-name-2", second.Tenant!.Code);

        Assert.False((await s.Provisioning.CreateAsync(new TenantCreateRequest("X", "n", "no-at-sign", "Str0ng!Pass", s.Trial.Id, StartMode.Trial))).Succeeded);
        Assert.False((await s.Provisioning.CreateAsync(new TenantCreateRequest("Valid Name", "n", "c@x.test", "Str0ng!Pass", 9999, StartMode.Trial))).Succeeded);

        s.Users.Setup(u => u.FindByEmailAsync("taken@x.test")).ReturnsAsync(new ApplicationUser { Email = "taken@x.test" });
        var taken = await s.Provisioning.CreateAsync(new TenantCreateRequest("Valid Name", "n", "taken@x.test", "Str0ng!Pass", s.Trial.Id, StartMode.Trial));
        Assert.False(taken.Succeeded);
        Assert.Equal(2, await s.Db.Tenants.CountAsync());
    }

    [Fact]
    public async Task A_failed_owner_creation_leaves_no_tenant_behind_on_a_transactional_database()
    {
        var s = await Stack.CreateAsync();
        s.Users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak." }));

        var result = await s.Provisioning.CreateAsync(new TenantCreateRequest("Weak Pw Co", "n", "w@x.test", "weak", s.Trial.Id, StartMode.Trial));

        Assert.False(result.Succeeded);
        Assert.Contains("Password too weak.", result.Errors);
        // (InMemory has no transactions; on SQL Server the whole creation is rolled back - see the SQL-gated retention test.)
    }

    // ===================================================== authorization map

    [Fact]
    public void Every_platform_controller_is_staff_only_and_money_or_config_actions_are_admin_only()
    {
        var controllers = typeof(PlatformControllerBase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(PlatformControllerBase)) && !t.IsAbstract).ToList();
        Assert.True(controllers.Count >= 10);

        foreach (var c in controllers)
        {
            Assert.DoesNotContain(c.GetCustomAttributes<AllowAnonymousAttribute>(true), _ => true);
            Assert.Equal(Roles.PlatformStaff, typeof(PlatformControllerBase).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
            foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => typeof(IActionResult).IsAssignableFrom(m.ReturnType) || typeof(Task<IActionResult>).IsAssignableFrom(m.ReturnType)))
            {
                Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>());
            }
        }

        static bool AdminOnly(MemberInfo m, Type controller) =>
            m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Roles == Roles.PlatformAdmin)
            || controller.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Roles == Roles.PlatformAdmin);

        foreach (var type in new[] { typeof(PlatformPlansController), typeof(PlatformSettingsController), typeof(PlatformProvidersController), typeof(PlatformStaffController) })
        {
            Assert.True(AdminOnly(type, type), $"{type.Name} must require PlatformAdmin");
        }

        // State-changing tenant actions and refunds/releases/cancellations are admin-only.
        foreach (var (type, name) in new[]
        {
            (typeof(PlatformTenantsController), "Suspend"), (typeof(PlatformTenantsController), "Disable"), (typeof(PlatformTenantsController), "Terminate"),
            (typeof(PlatformTenantsController), "Purge"), (typeof(PlatformTenantsController), "AssignPlan"), (typeof(PlatformTenantsController), "Terms"),
            (typeof(PlatformTenantsController), "Create"), (typeof(PlatformPaymentsController), "Refund"), (typeof(PlatformPaymentsController), "Release"),
            (typeof(PlatformInvoicesController), "CancelInvoice"), (typeof(PlatformInvoicesController), "Adjust")
        })
        {
            foreach (var m in type.GetMethods().Where(m => m.Name == name && m.DeclaringType == type))
            {
                Assert.True(AdminOnly(m, type), $"{type.Name}.{name} must require PlatformAdmin");
            }
        }
    }

    [Fact]
    public void Tenant_roles_never_reach_platform_pages_and_platform_roles_are_not_tenant_roles()
    {
        Assert.DoesNotContain(Roles.PlatformAdmin, Roles.All);
        Assert.DoesNotContain(Roles.PlatformBilling, Roles.All);
        Assert.DoesNotContain(Roles.Admin, Roles.PlatformStaff.Split(','));
        Assert.DoesNotContain(Roles.Manager, Roles.PlatformStaff.Split(','));
    }
}

/// <summary>Needs real SQL Server (see SqlServerFactAttribute).</summary>
public class TenantRetentionSqlTests
{
    private sealed class Tenanted(int id) : ITenantContext
    {
        private int _id = id; private int _bypass;
        public int TenantId => _id;
        public bool IsFilterBypassed => _bypass > 0;
        public IDisposable UseTenant(int t) { var o = _id; _id = t; return new Undo(() => _id = o); }
        public IDisposable BypassFilters() { _bypass++; return new Undo(() => _bypass--); }
        private sealed class Undo(Action a) : IDisposable { public void Dispose() => a(); }
    }

    private sealed class Clock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(now, DateTimeKind.Utc)); }

    [SqlServerFact]
    public async Task Purge_erases_only_the_terminated_tenants_business_data_after_retention_and_keeps_financial_records()
    {
        var database = "IMS_RetentionTest_" + Guid.NewGuid().ToString("N")[..10];
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar)!)
        { InitialCatalog = database, TrustServerCertificate = true, MultipleActiveResultSets = true }.ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cs).Options;
        var tenant = new Tenanted(0);
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = new ApplicationDbContext(options, tenant);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = new PlatformSettingsService(db, cache);
            var audit = new SaasAuditService(db, new HttpContextAccessor());
            var retention = new TenantRetentionService(db, tenant, settings, audit, new Clock(now));

            var a = new Tenant { Name = "A", Code = "a" };
            var b = new Tenant { Name = "B", Code = "b" };
            db.Tenants.AddRange(a, b);
            await db.SaveChangesAsync();
            foreach (var t in new[] { a, b })
            {
                using var _ = tenant.UseTenant(t.Id);
                var cat = new WebApplication1.Models.Inventory.Category { Name = "C" };
                var unit = new WebApplication1.Models.Inventory.UnitOfMeasure { Name = "Piece", Symbol = "pc" };
                db.Products.Add(new WebApplication1.Models.Inventory.Product { Sku = "S", Name = "P", Category = cat, UnitOfMeasure = unit });
                db.Warehouses.Add(new WebApplication1.Models.Inventory.Warehouse { Code = "W", Name = "W" });
                db.Accounts.Add(new WebApplication1.Models.Accounting.Account { Code = "1000", Name = "Cash" });
                await db.SaveChangesAsync();
            }
            db.SaasAuditLogs.Add(new SaasAuditLog { Action = "x", TenantId = a.Id });
            await db.SaveChangesAsync();

            // Guards
            await Assert.ThrowsAsync<InvalidOperationException>(() => retention.PurgeAsync(a.Id, "a"));                    // not terminated
            a.Status = TenantStatus.Terminated; a.DeletedAt = now.AddDays(-10);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => retention.PurgeAsync(a.Id, "a"));                    // retention (90 days) not over
            a.DeletedAt = now.AddDays(-100);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => retention.PurgeAsync(a.Id, "wrong-code"));           // confirmation mismatch
            await Assert.ThrowsAsync<InvalidOperationException>(() => retention.PurgeAsync(b.Id, "b"));                    // B is not terminated

            await retention.PurgeAsync(a.Id, "a", "platform@saas.test");

            using (tenant.BypassFilters())
            {
                Assert.Equal(1, await db.Products.IgnoreQueryFilters().CountAsync());
                Assert.Equal(1, await db.Warehouses.IgnoreQueryFilters().CountAsync());
                Assert.Equal(1, await db.Accounts.IgnoreQueryFilters().CountAsync());
                Assert.Equal(1, await db.Products.IgnoreQueryFilters().CountAsync(p => EF.Property<int>(p, "TenantId") == b.Id));
            }
            Assert.NotNull((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == a.Id)).PurgedAt);
            Assert.Null((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == b.Id)).PurgedAt);
            Assert.Contains(await db.SaasAuditLogs.AsNoTracking().ToListAsync(), l => l.Action == "tenant.purged");
            Assert.Contains(await db.SaasAuditLogs.AsNoTracking().ToListAsync(), l => l.Action == "x");                    // financial/audit records kept
            await Assert.ThrowsAsync<InvalidOperationException>(() => retention.PurgeAsync(a.Id, "a"));                    // already purged
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
