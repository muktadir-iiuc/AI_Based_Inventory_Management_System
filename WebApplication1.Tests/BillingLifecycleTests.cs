using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

public class BillingLifecycleTests
{
    private sealed class FakeTime(DateTime start) : TimeProvider
    {
        public DateTime Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class FixedTenant(int id) : ITenantContext
    {
        public int TenantId => id;
        public bool IsFilterBypassed => true; // platform-style access: these tests exercise billing, not isolation
        public IDisposable UseTenant(int tenantId) => new Noop();
        public IDisposable BypassFilters() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class Harness
    {
        public ApplicationDbContext Db = null!;
        public FakeTime Time = new(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));
        public ISubscriptionService Subscriptions = null!;
        public IBillingPaymentService Payments = null!;
        public ISubscriptionLifecycleService Lifecycle = null!;
        public IEntitlementService Entitlements = null!;
        public SubscriptionPlan Trial = null!, Monthly = null!;
        public Tenant Tenant = null!;

        public static async Task<Harness> CreateAsync()
        {
            var h = new Harness();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var tenantContext = new FixedTenant(1);
            h.Db = new ApplicationDbContext(options, tenantContext);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = new PlatformSettingsService(h.Db, cache);
            var audit = new SaasAuditService(h.Db, new HttpContextAccessor());
            var notifier = new SaasNotifier(h.Db);
            var invoices = new InvoiceService(h.Db, settings, notifier, audit, h.Time);
            var subs = new SubscriptionService(h.Db, invoices, settings, notifier, audit, cache, h.Time);
            h.Subscriptions = subs;
            h.Payments = new BillingPaymentService(h.Db, subs, notifier, audit, h.Time);
            h.Lifecycle = new SubscriptionLifecycleService(h.Db, tenantContext, invoices, subs, settings, notifier, audit, h.Time, NullLogger<SubscriptionLifecycleService>.Instance);
            h.Entitlements = new EntitlementService(h.Db, tenantContext, subs);

            h.Tenant = new Tenant { Id = 1, Name = "Acme", Code = "acme", ContactEmail = "owner@acme.test" };
            h.Db.Tenants.Add(h.Tenant);
            h.Trial = new SubscriptionPlan { Code = "trial", Name = "Trial", Price = 0, TrialDays = 14, GraceDays = 0, MaxUsers = 2, MaxProducts = 1, MaxWarehouses = 1 };
            h.Monthly = new SubscriptionPlan { Code = "basic", Name = "Basic", Price = 1000, TaxPercent = 5, DiscountPercent = 10, GraceDays = 5, MaxUsers = 5 };
            h.Trial.Features.Add(new PlanFeature { FeatureKey = Features.Sales });
            h.Monthly.Features.Add(new PlanFeature { FeatureKey = Features.Sales });
            h.Monthly.Features.Add(new PlanFeature { FeatureKey = Features.Reports });
            h.Db.SubscriptionPlans.AddRange(h.Trial, h.Monthly);
            await h.Db.SaveChangesAsync();
            return h;
        }

        public Task<LifecycleReport> RunJobAsync() => Lifecycle.RunAsync();

        public async Task<SaasInvoice> InvoiceAsync() => await Db.SaasInvoices.Include(i => i.Items).OrderByDescending(i => i.Id).FirstAsync();

        public Task<PaymentResult> PayAsync(SaasInvoice invoice, decimal? amount = null, string? txn = null, string currency = "BDT", int? tenantId = 1, string method = "bKash") =>
            Payments.ApplyPaymentAsync(new PaymentRequest(invoice.Id, amount ?? invoice.Total - invoice.AmountPaid, currency, method, "sslcommerz", txn, ExpectedTenantId: tenantId));

        public async Task<TenantSubscription> SubAsync() => await Db.TenantSubscriptions.AsNoTracking().FirstAsync();
        public async Task<Tenant> TenantAsync() => await Db.Tenants.AsNoTracking().FirstAsync();
    }

    // ------------------------------------------------------------------ math

    [Fact]
    public void Invoice_amounts_apply_discount_then_tax_with_two_decimals()
    {
        var a = BillingMath.Compute(1000m, 10m, 5m);
        Assert.Equal(1000.00m, a.SubTotal);
        Assert.Equal(100.00m, a.Discount);
        Assert.Equal(45.00m, a.Tax);      // 5% of 900
        Assert.Equal(945.00m, a.Total);

        var rounded = BillingMath.Compute(99.99m, 0m, 7.5m);
        Assert.Equal(7.50m, rounded.Tax); // 7.49925 -> 7.50
        Assert.Equal(107.49m, rounded.Total);
    }

    [Fact]
    public void Yearly_and_custom_intervals_add_correctly()
    {
        var start = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc), BillingMath.AddInterval(start, 12, BillingIntervalUnit.Month));
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), BillingMath.AddInterval(start, 60, BillingIntervalUnit.Day));
    }

    // ------------------------------------------------------------------ access rules

    [Fact]
    public async Task Trial_gives_access_until_it_ends_even_if_no_job_has_run()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Trial.Id, StartMode.Trial);
        Assert.True((await h.Subscriptions.GetAccessAsync(1)).IsFull);

        h.Time.Advance(TimeSpan.FromDays(15));
        h.Subscriptions.InvalidateAccess(1);

        // The stored status is still Trialing (job has not run), yet access is already closed.
        Assert.Equal(SubscriptionStatus.Trialing, (await h.SubAsync()).Status);
        Assert.Equal("trial-expired", (await h.Subscriptions.GetAccessAsync(1)).Code);
    }

    [Fact]
    public async Task Trial_job_expires_the_subscription_once()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Trial.Id, StartMode.Trial);
        h.Time.Advance(TimeSpan.FromDays(15));

        var first = await h.RunJobAsync();
        var second = await h.RunJobAsync();

        Assert.Equal(1, first.TrialsExpired);
        Assert.Equal(0, second.TrialsExpired);
        Assert.Equal(SubscriptionStatus.Expired, (await h.SubAsync()).Status);
        Assert.Equal(1, await h.Db.SaasNotifications.CountAsync(n => n.Type == "trial-expired" && n.Channel == NotificationChannel.InApp));
    }

    // ------------------------------------------------------------------ invoicing

    [Fact]
    public async Task First_invoice_uses_plan_prices_and_a_second_request_for_the_same_period_returns_the_same_invoice()
    {
        var h = await Harness.CreateAsync();
        var sub = await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);

        var invoice = await h.InvoiceAsync();
        Assert.Equal(945.00m, invoice.Total);
        Assert.Equal("Basic", invoice.PlanName);
        Assert.StartsWith("SINV-", invoice.InvoiceNumber);

        var invoices = new InvoiceService(h.Db, new PlatformSettingsService(h.Db, new MemoryCache(new MemoryCacheOptions())),
            new SaasNotifier(h.Db), new SaasAuditService(h.Db, new HttpContextAccessor()), h.Time);
        var again = await invoices.CreateForPeriodAsync(sub, BillingTerms.FromPlan(h.Monthly), InvoicePurpose.NewSubscription, invoice.PeriodStart);
        Assert.Equal(invoice.Id, again.Id);
        Assert.Equal(1, await h.Db.SaasInvoices.CountAsync());
    }

    [Fact]
    public async Task Changing_the_plan_price_later_does_not_change_an_issued_invoice()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var before = (await h.InvoiceAsync()).Total;

        h.Monthly.Price = 5000m;
        await h.Db.SaveChangesAsync();

        Assert.Equal(before, (await h.InvoiceAsync()).Total);
    }

    // ------------------------------------------------------------------ payments

    [Fact]
    public async Task Verified_payment_activates_the_subscription_for_the_invoiced_period()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await h.InvoiceAsync();

        var result = await h.PayAsync(invoice, txn: "TXN-1");

        Assert.Equal(PaymentOutcome.Applied, result.Outcome);
        var sub = await h.SubAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(invoice.PeriodStart, sub.CurrentPeriodStart);
        Assert.Equal(invoice.PeriodEnd, sub.CurrentPeriodEnd);
        Assert.Equal(InvoiceStatus.Paid, (await h.InvoiceAsync()).Status);
        Assert.NotNull(result.Payment!.ReceiptNumber);
        Assert.True((await h.Subscriptions.GetAccessAsync(1)).IsFull);
    }

    [Fact]
    public async Task Repeated_callback_with_the_same_transaction_id_changes_nothing()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await h.InvoiceAsync();

        await h.PayAsync(invoice, txn: "TXN-1");
        var periodEnd = (await h.SubAsync()).CurrentPeriodEnd;
        var replay = await h.PayAsync(invoice, amount: invoice.Total, txn: "TXN-1");

        Assert.Equal(PaymentOutcome.Duplicate, replay.Outcome);
        Assert.Equal(1, await h.Db.SaasPayments.CountAsync());
        Assert.Equal(periodEnd, (await h.SubAsync()).CurrentPeriodEnd);
        Assert.Equal(invoice.Total, (await h.InvoiceAsync()).AmountPaid);
    }

    [Fact]
    public async Task A_second_payment_with_a_new_transaction_id_cannot_extend_a_paid_invoice_again()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await h.InvoiceAsync();
        await h.PayAsync(invoice, txn: "TXN-1");
        var periodEnd = (await h.SubAsync()).CurrentPeriodEnd;

        var second = await h.PayAsync(invoice, amount: 10m, txn: "TXN-2");

        Assert.Equal(PaymentOutcome.Rejected, second.Outcome);
        Assert.Equal(periodEnd, (await h.SubAsync()).CurrentPeriodEnd);
    }

    [Fact]
    public async Task Idempotency_key_also_blocks_duplicates()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await h.InvoiceAsync();
        PaymentRequest Req() => new(invoice.Id, 100m, "BDT", "Cash", "manual", IdempotencyKey: "attempt-9");

        Assert.Equal(PaymentOutcome.Applied, (await h.Payments.ApplyPaymentAsync(Req())).Outcome);
        Assert.Equal(PaymentOutcome.Duplicate, (await h.Payments.ApplyPaymentAsync(Req())).Outcome);
        Assert.Equal(100m, (await h.InvoiceAsync()).AmountPaid);
    }

    [Theory]
    [InlineData("wrong-currency")]
    [InlineData("wrong-tenant")]
    [InlineData("overpay")]
    [InlineData("zero")]
    public async Task Invalid_payments_are_rejected_and_leave_everything_unchanged(string kind)
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await h.InvoiceAsync();

        var result = kind switch
        {
            "wrong-currency" => await h.PayAsync(invoice, currency: "USD", txn: "T"),
            "wrong-tenant" => await h.PayAsync(invoice, tenantId: 2, txn: "T"),
            "overpay" => await h.PayAsync(invoice, amount: invoice.Total + 1, txn: "T"),
            _ => await h.PayAsync(invoice, amount: 0m, txn: "T")
        };

        Assert.Equal(PaymentOutcome.Rejected, result.Outcome);
        Assert.Equal(0, await h.Db.SaasPayments.CountAsync());
        Assert.Equal(0m, (await h.InvoiceAsync()).AmountPaid);
        Assert.Equal(SubscriptionStatus.Expired, (await h.SubAsync()).Status);
    }

    [Fact]
    public async Task Partial_payment_does_not_activate_the_subscription()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await h.InvoiceAsync();

        await h.PayAsync(invoice, amount: 100m, txn: "T1");

        Assert.Equal(InvoiceStatus.PartiallyPaid, (await h.InvoiceAsync()).Status);
        Assert.Equal(SubscriptionStatus.Expired, (await h.SubAsync()).Status);

        await h.PayAsync(invoice, amount: invoice.Total - 100m, txn: "T2");
        Assert.Equal(SubscriptionStatus.Active, (await h.SubAsync()).Status);
    }

    [Fact]
    public async Task Tenant_submitted_manual_payment_counts_only_after_confirmation()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        var invoice = await h.InvoiceAsync();

        var submitted = await h.Payments.ApplyPaymentAsync(new PaymentRequest(invoice.Id, invoice.Total, "BDT", "BankTransfer", "manual",
            Reference: "SLIP-77", ExpectedTenantId: 1, RequiresConfirmation: true));

        Assert.Equal(PaymentOutcome.AwaitingConfirmation, submitted.Outcome);
        Assert.Equal(0m, (await h.InvoiceAsync()).AmountPaid);
        Assert.Equal(SubscriptionStatus.Expired, (await h.SubAsync()).Status);

        var confirmed = await h.Payments.ConfirmManualPaymentAsync(submitted.Payment!.Id, "platform@saas.test");
        Assert.Equal(PaymentOutcome.Applied, confirmed.Outcome);
        Assert.Equal(SubscriptionStatus.Active, (await h.SubAsync()).Status);

        var again = await h.Payments.ConfirmManualPaymentAsync(submitted.Payment.Id);
        Assert.Equal(PaymentOutcome.Duplicate, again.Outcome);
        Assert.Equal(invoice.Total, (await h.InvoiceAsync()).AmountPaid);
    }

    // ------------------------------------------------------------------ renewal, overdue, grace, suspension

    private static async Task<Harness> ActiveTenantAsync()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        await h.PayAsync(await h.InvoiceAsync(), txn: "FIRST");
        return h;
    }

    [Fact]
    public async Task Renewal_invoice_is_generated_once_before_the_term_ends()
    {
        var h = await ActiveTenantAsync();
        var firstEnd = (await h.SubAsync()).CurrentPeriodEnd!.Value;
        h.Time.Now = firstEnd.AddDays(-6);

        var a = await h.RunJobAsync();
        var b = await h.RunJobAsync();

        Assert.Equal(1, a.RenewalInvoices);
        Assert.Equal(0, b.RenewalInvoices);
        var renewal = await h.InvoiceAsync();
        Assert.Equal(InvoicePurpose.Renewal, renewal.Purpose);
        Assert.Equal(firstEnd, renewal.PeriodStart);
        Assert.Equal(2, await h.Db.SaasInvoices.CountAsync());
    }

    [Fact]
    public async Task Unpaid_invoice_goes_overdue_then_grace_then_suspends_automatically()
    {
        var h = await ActiveTenantAsync();
        var firstEnd = (await h.SubAsync()).CurrentPeriodEnd!.Value;

        h.Time.Now = firstEnd.AddDays(-6);
        await h.RunJobAsync();                                    // renewal invoice, due at firstEnd

        h.Time.Now = firstEnd.AddHours(1);
        var overdue = await h.RunJobAsync();
        Assert.Equal(1, overdue.Overdue);
        Assert.Equal(InvoiceStatus.Overdue, (await h.InvoiceAsync()).Status);
        Assert.Equal(SubscriptionStatus.GracePeriod, (await h.SubAsync()).Status);
        h.Subscriptions.InvalidateAccess(1);
        Assert.True((await h.Subscriptions.GetAccessAsync(1)).IsFull);

        h.Time.Now = firstEnd.AddDays(5).AddHours(1);             // grace (5 days) is over
        var suspended = await h.RunJobAsync();

        Assert.Equal(1, suspended.Suspended);
        Assert.Equal(SubscriptionStatus.Suspended, (await h.SubAsync()).Status);
        var tenant = await h.TenantAsync();
        Assert.True(tenant.SuspendedForNonPayment);
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        h.Subscriptions.InvalidateAccess(1);
        Assert.Equal(AccessLevel.BillingOnly, (await h.Subscriptions.GetAccessAsync(1)).Level);

        // Re-running does not suspend (or notify) twice.
        Assert.Equal(0, (await h.RunJobAsync()).Suspended);
        Assert.Equal(1, await h.Db.SaasNotifications.CountAsync(n => n.Type == "subscription-suspended" && n.Channel == NotificationChannel.InApp));
    }

    [Fact]
    public async Task Grace_period_end_closes_access_even_before_the_job_suspends()
    {
        var h = await ActiveTenantAsync();
        var firstEnd = (await h.SubAsync()).CurrentPeriodEnd!.Value;
        h.Time.Now = firstEnd.AddDays(-6); await h.RunJobAsync();
        h.Time.Now = firstEnd.AddHours(1); await h.RunJobAsync();

        h.Time.Now = firstEnd.AddDays(6);   // job has not run again
        h.Subscriptions.InvalidateAccess(1);

        Assert.Equal("grace-ended", (await h.Subscriptions.GetAccessAsync(1)).Code);
    }

    [Fact]
    public async Task Payment_after_suspension_restores_access_automatically()
    {
        var h = await ActiveTenantAsync();
        var firstEnd = (await h.SubAsync()).CurrentPeriodEnd!.Value;
        h.Time.Now = firstEnd.AddDays(-6); await h.RunJobAsync();
        h.Time.Now = firstEnd.AddHours(1); await h.RunJobAsync();
        h.Time.Now = firstEnd.AddDays(6); await h.RunJobAsync();
        Assert.Equal(SubscriptionStatus.Suspended, (await h.SubAsync()).Status);

        var renewal = await h.InvoiceAsync();
        await h.PayAsync(renewal, txn: "LATE");

        var sub = await h.SubAsync();
        var tenant = await h.TenantAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.False(tenant.SuspendedForNonPayment);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.True((await h.Subscriptions.GetAccessAsync(1)).IsFull);
        Assert.Equal(renewal.PeriodEnd, sub.CurrentPeriodEnd);
    }

    [Fact]
    public async Task Payment_never_lifts_an_administrative_suspension()
    {
        var h = await ActiveTenantAsync();
        var firstEnd = (await h.SubAsync()).CurrentPeriodEnd!.Value;
        h.Time.Now = firstEnd.AddDays(-6); await h.RunJobAsync();
        h.Time.Now = firstEnd.AddHours(1); await h.RunJobAsync();
        h.Time.Now = firstEnd.AddDays(6); await h.RunJobAsync();
        await h.Subscriptions.SuspendAsync(1, "chargeback fraud suspected");

        await h.PayAsync(await h.InvoiceAsync(), txn: "LATE");

        var tenant = await h.TenantAsync();
        Assert.True(tenant.IsAdminHold);
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        h.Subscriptions.InvalidateAccess(1);
        Assert.Equal("admin-hold", (await h.Subscriptions.GetAccessAsync(1)).Code);

        // The admin lifting the hold is what restores access (payment was already settled).
        await h.Subscriptions.ReactivateAsync(1);
        Assert.True((await h.Subscriptions.GetAccessAsync(1)).IsFull);
    }

    [Fact]
    public async Task Lifting_an_admin_hold_does_not_forgive_an_unpaid_invoice()
    {
        var h = await ActiveTenantAsync();
        var firstEnd = (await h.SubAsync()).CurrentPeriodEnd!.Value;
        h.Time.Now = firstEnd.AddDays(-6); await h.RunJobAsync();
        h.Time.Now = firstEnd.AddHours(1); await h.RunJobAsync();
        h.Time.Now = firstEnd.AddDays(6); await h.RunJobAsync();
        await h.Subscriptions.SuspendAsync(1, "review");
        await h.Subscriptions.ReactivateAsync(1);

        h.Subscriptions.InvalidateAccess(1);
        Assert.Equal("suspended-nonpayment", (await h.Subscriptions.GetAccessAsync(1)).Code);
    }

    [Fact]
    public async Task Payment_does_not_re_enable_a_disabled_tenant()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Monthly.Id, StartMode.InvoiceFirst);
        await h.Subscriptions.DisableAsync(1, "contract ended");

        await h.PayAsync(await h.InvoiceAsync(), txn: "T");

        h.Subscriptions.InvalidateAccess(1);
        Assert.Equal(AccessLevel.Blocked, (await h.Subscriptions.GetAccessAsync(1)).Level);
    }

    [Fact]
    public async Task Cancel_at_term_end_keeps_access_until_the_paid_term_finishes()
    {
        var h = await ActiveTenantAsync();
        var end = (await h.SubAsync()).CurrentPeriodEnd!.Value;
        await h.Subscriptions.CancelAsync(1, immediate: false);
        Assert.True((await h.Subscriptions.GetAccessAsync(1)).IsFull);

        h.Time.Now = end.AddDays(-6);
        Assert.Equal(0, (await h.RunJobAsync()).RenewalInvoices);   // no renewal for a cancelled subscription

        h.Time.Now = end.AddMinutes(1);
        Assert.Equal(1, (await h.RunJobAsync()).Cancelled);
        h.Subscriptions.InvalidateAccess(1);
        Assert.Equal(AccessLevel.BillingOnly, (await h.Subscriptions.GetAccessAsync(1)).Level);
    }

    // ------------------------------------------------------------------ entitlements

    [Fact]
    public async Task Plan_features_and_limits_come_from_the_subscription_with_overrides()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Trial.Id, StartMode.Trial);

        Assert.True(await h.Subscriptions.GetFeaturesAsync(1) is var f && f.Contains(Features.Sales) && !f.Contains(Features.Reports));
        var limits = await h.Subscriptions.GetLimitsAsync(1);
        Assert.Equal(2, limits.MaxUsers);

        h.Db.TenantFeatureOverrides.Add(new TenantFeatureOverride { TenantId = 1, FeatureKey = Features.Reports, IsEnabled = true });
        await h.Db.SaveChangesAsync();
        h.Subscriptions.InvalidateAccess(1);
        Assert.Contains(Features.Reports, await h.Subscriptions.GetFeaturesAsync(1));
    }

    [Fact]
    public async Task Adding_beyond_a_plan_limit_is_refused()
    {
        var h = await Harness.CreateAsync();
        await h.Subscriptions.StartAsync(1, h.Trial.Id, StartMode.Trial);   // MaxProducts = 1

        Assert.True(await h.Entitlements.CanAddAsync(LimitKind.Products));
        var category = new WebApplication1.Models.Inventory.Category { Name = "C" };
        var unit = new WebApplication1.Models.Inventory.UnitOfMeasure { Name = "Piece", Symbol = "pc" };
        h.Db.Products.Add(new WebApplication1.Models.Inventory.Product { Sku = "A", Name = "A", Category = category, UnitOfMeasure = unit });
        await h.Db.SaveChangesAsync();

        Assert.False(await h.Entitlements.CanAddAsync(LimitKind.Products));
    }

    // ------------------------------------------------------------------ plan change

    [Fact]
    public async Task Upgrade_invoice_switches_the_plan_only_when_paid_and_a_downgrade_waits_for_renewal()
    {
        var h = await ActiveTenantAsync();
        var pro = new SubscriptionPlan { Code = "pro", Name = "Pro", Price = 4000, GraceDays = 5, MaxUsers = 20 };
        var cheap = new SubscriptionPlan { Code = "mini", Name = "Mini", Price = 100, GraceDays = 5, MaxUsers = 1 };
        h.Db.SubscriptionPlans.AddRange(pro, cheap);
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromDays(2));
        var invoice = await h.Subscriptions.RequestPlanChangeAsync(1, pro.Id);
        Assert.NotNull(invoice);
        Assert.Equal(h.Monthly.Id, (await h.SubAsync()).PlanId);       // not yet
        await h.PayAsync(invoice!, txn: "UP");
        Assert.Equal(pro.Id, (await h.SubAsync()).PlanId);
        Assert.Equal(4000m, (await h.SubAsync()).Price);

        var downgrade = await h.Subscriptions.RequestPlanChangeAsync(1, cheap.Id);
        Assert.Null(downgrade);
        var sub = await h.SubAsync();
        Assert.Equal(pro.Id, sub.PlanId);
        Assert.Equal(cheap.Id, sub.PendingPlanId);

        h.Time.Now = sub.CurrentPeriodEnd!.Value.AddDays(-6);
        await h.RunJobAsync();
        var renewal = await h.InvoiceAsync();
        Assert.Equal("Mini", renewal.PlanName);
        Assert.Equal(100m, renewal.Total);
    }
}
