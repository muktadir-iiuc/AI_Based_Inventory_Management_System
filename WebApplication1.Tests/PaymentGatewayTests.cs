using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Saas.Payments;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

public class PaymentGatewayTests
{
    // ======================================================== SSLCOMMERZ provider (HTTP stubbed)

    private sealed class StubHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString() + (request.Content is null ? "" : "\n" + request.Content.ReadAsStringAsync(cancellationToken).Result));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static SslCommerzProvider Ssl(StubHandler handler, bool configured = true) => new(
        new StubFactory(handler),
        Options.Create(new SslCommerzOptions { StoreId = configured ? "teststore" : null, StorePassword = configured ? "testpass" : null, Sandbox = true }),
        NullLogger<SslCommerzProvider>.Instance);

    private static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static Dictionary<string, string> SignedIpn(string password = "testpass", Action<Dictionary<string, string>>? tamper = null)
    {
        var f = new Dictionary<string, string>
        {
            ["status"] = "VALID", ["tran_id"] = "PAABC", ["val_id"] = "VAL1", ["amount"] = "945.00", ["currency"] = "BDT",
            ["bank_tran_id"] = "BANK1", ["store_id"] = "teststore", ["tran_date"] = "2026-01-01 10:00:00", ["risk_level"] = "0"
        };
        f["verify_key"] = string.Join(",", f.Keys.Where(k => k != "verify_key").OrderBy(k => k, StringComparer.Ordinal));
        // The documented algorithm: listed fields + store_passwd=md5(password), sorted by key, key=value joined by '&', md5.
        var data = f["verify_key"].Split(',').ToDictionary(k => k, k => f[k]);
        data["store_passwd"] = Md5(password);
        f["verify_sign"] = Md5(string.Join("&", data.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")));
        tamper?.Invoke(f);
        return f;
    }

    [Fact]
    public void Ipn_signature_is_accepted_only_when_untampered_and_made_with_the_store_password()
    {
        Assert.True(SslCommerzProvider.IsSignatureValid(SignedIpn(), "testpass"));
        Assert.False(SslCommerzProvider.IsSignatureValid(SignedIpn(tamper: f => f["amount"] = "1.00"), "testpass"));
        Assert.False(SslCommerzProvider.IsSignatureValid(SignedIpn(password: "other"), "testpass"));
        Assert.False(SslCommerzProvider.IsSignatureValid(SignedIpn(tamper: f => f.Remove("verify_sign")), "testpass"));
    }

    [Fact]
    public void Parsing_a_callback_reports_signature_state()
    {
        var provider = Ssl(new StubHandler(_ => "{}"));
        Assert.True(provider.ParseCallback(SignedIpn()).SignatureValid);
        Assert.False(provider.ParseCallback(SignedIpn(tamper: f => f["val_id"] = "EVIL")).SignatureValid);
        Assert.Null(provider.ParseCallback(new Dictionary<string, string> { ["tran_id"] = "PAABC", ["status"] = "CANCELLED" }).SignatureValid);
    }

    [Fact]
    public async Task Initiation_posts_the_documented_fields_and_returns_the_gateway_url()
    {
        var handler = new StubHandler(_ => """{"status":"SUCCESS","sessionkey":"SESS1","GatewayPageURL":"https://sandbox.sslcommerz.com/gw/abc"}""");
        var result = await Ssl(handler).InitiateAsync(new ProviderInitRequest("PAREF", 945m, "BDT", "SINV-1", "Acme", "a@b.test", null, null, null,
            "https://x/s", "https://x/f", "https://x/c", "https://x/i"));

        Assert.True(result.Succeeded);
        Assert.Equal("https://sandbox.sslcommerz.com/gw/abc", result.RedirectUrl);
        Assert.Equal("SESS1", result.SessionKey);
        var sent = handler.Requests.Single();
        Assert.StartsWith("https://sandbox.sslcommerz.com/gwprocess/v4/api.php", sent);
        foreach (var expected in new[] { "store_id=teststore", "total_amount=945.00", "currency=BDT", "tran_id=PAREF", "success_url=", "fail_url=", "cancel_url=", "ipn_url=", "shipping_method=NO", "product_profile=non-physical-goods" })
        {
            Assert.Contains(expected, sent);
        }
    }

    [Theory]
    [InlineData(5.0, "BDT")]
    [InlineData(600000.0, "BDT")]
    [InlineData(100.0, "USD")]
    public async Task Initiation_refuses_amounts_or_currencies_the_gateway_cannot_take_without_calling_it(double amount, string currency)
    {
        var handler = new StubHandler(_ => "{}");
        var result = await Ssl(handler).InitiateAsync(new ProviderInitRequest("R", (decimal)amount, currency, "I", "n", null, null, null, null, "s", "f", "c", "i"));
        Assert.False(result.Succeeded);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Gateway_failure_reason_is_surfaced()
    {
        var handler = new StubHandler(_ => """{"status":"FAILED","failedreason":"Invalid Information"}""");
        var result = await Ssl(handler).InitiateAsync(new ProviderInitRequest("R", 100m, "BDT", "I", "n", null, null, null, null, "s", "f", "c", "i"));
        Assert.False(result.Succeeded);
        Assert.Equal("Invalid Information", result.Error);
    }

    [Fact]
    public async Task Validation_by_val_id_maps_valid_risky_and_unknown_responses()
    {
        string Body(string status, string risk = "0") =>
            $$"""{"status":"{{status}}","tran_id":"PAABC","val_id":"VAL1","amount":"945.00","currency":"BDT","currency_type":"BDT","currency_amount":"945.00","bank_tran_id":"BANK1","card_type":"BKASH-BKash","risk_level":"{{risk}}","APIConnect":"DONE"}""";

        var paid = await Ssl(new StubHandler(_ => Body("VALID"))).VerifyAsync("PAABC", "VAL1", null);
        Assert.Equal(VerificationStatus.Paid, paid.Status);
        Assert.Equal(945.00m, paid.Amount);
        Assert.Equal("BANK1", paid.TransactionId);
        Assert.Equal("BKASH-BKash", paid.Method);

        Assert.Equal(VerificationStatus.Paid, (await Ssl(new StubHandler(_ => Body("VALIDATED"))).VerifyAsync("PAABC", "VAL1", null)).Status);
        Assert.Equal(VerificationStatus.PaidHeld, (await Ssl(new StubHandler(_ => Body("VALID", risk: "1"))).VerifyAsync("PAABC", "VAL1", null)).Status);
        Assert.Equal(VerificationStatus.NotFound, (await Ssl(new StubHandler(_ => """{"status":"INVALID_TRANSACTION"}""")).VerifyAsync("PAABC", "NOPE", null)).Status);
        Assert.Equal(VerificationStatus.Failed, (await Ssl(new StubHandler(_ => Body("FAILED"))).VerifyAsync("PAABC", "VAL1", null)).Status);
    }

    [Fact]
    public async Task Query_by_tran_id_prefers_a_paid_element_and_uses_the_documented_endpoint()
    {
        var handler = new StubHandler(_ => """
            {"APIConnect":"DONE","no_of_trans_found":3,"element":[
              {"status":"FAILED","tran_id":"PAABC","val_id":"","amount":"945.00","currency":"BDT","error":"declined"},
              {"status":"VALIDATED","tran_id":"PAABC","val_id":"VAL9","amount":"945.00","currency":"BDT","bank_tran_id":"B9","risk_level":"0"},
              {"status":"PENDING","tran_id":"PAABC","val_id":"","amount":"945.00","currency":"BDT"}]}
            """);
        var result = await Ssl(handler).VerifyAsync("PAABC", null, null);

        Assert.Equal(VerificationStatus.Paid, result.Status);
        Assert.Equal("B9", result.TransactionId);
        Assert.Contains("merchantTransIDvalidationAPI.php?tran_id=PAABC", handler.Requests.Single());
    }

    [Fact]
    public async Task Query_with_no_elements_is_not_found_and_an_unreachable_gateway_is_an_error_not_a_verdict()
    {
        Assert.Equal(VerificationStatus.NotFound,
            (await Ssl(new StubHandler(_ => """{"APIConnect":"DONE","no_of_trans_found":0,"element":[]}""")).VerifyAsync("R", null, null)).Status);
        Assert.Equal(VerificationStatus.Error,
            (await Ssl(new StubHandler(_ => """{"APIConnect":"FAILED"}""")).VerifyAsync("R", null, null)).Status);
        Assert.Equal(VerificationStatus.Error,
            (await Ssl(new StubHandler(_ => "<html>not json</html>")).VerifyAsync("R", null, null)).Status);
    }

    [Fact]
    public async Task Unconfigured_provider_is_never_usable()
    {
        var provider = Ssl(new StubHandler(_ => "{}"), configured: false);
        Assert.False(provider.IsConfigured);
        Assert.Equal(VerificationStatus.Error, (await provider.VerifyAsync("R", "V", null)).Status);
    }

    [Fact]
    public async Task Refund_uses_the_documented_parameters_and_maps_responses()
    {
        var handler = new StubHandler(_ => """{"APIConnect":"DONE","status":"success","refund_ref_id":"RR1","bank_tran_id":"B1"}""");
        var ok = await Ssl(handler).RefundAsync(new ProviderRefundRequest("B1", "RF1", 100m, "customer request", "RCT-1"));
        Assert.True(ok.Accepted);
        Assert.Equal("RR1", ok.ProviderRefundRef);
        var url = handler.Requests.Single();
        foreach (var p in new[] { "bank_tran_id=B1", "refund_trans_id=RF1", "refund_amount=100.00", "refund_remarks=customer request" }) Assert.Contains(p, url);

        var failed = await Ssl(new StubHandler(_ => """{"APIConnect":"DONE","status":"failed","errorReason":"already refunded"}""")).RefundAsync(new ProviderRefundRequest("B1", "RF2", 1m, "x", null));
        Assert.False(failed.Accepted);
        Assert.Equal("already refunded", failed.Error);

        Assert.Equal(ProviderRefundState.Refunded, await Ssl(new StubHandler(_ => """{"APIConnect":"DONE","status":"refunded"}""")).GetRefundStateAsync("RR1"));
    }

    // ======================================================== gateway orchestration (fake provider)

    private sealed class FakeProvider : IPaymentProvider
    {
        public string Code => "fake";
        public string DisplayName => "Fake gateway";
        public bool IsConfigured { get; set; } = true;
        public decimal MinimumAmount => 10m;

        public ProviderInitResult InitResult { get; set; } = new(true, "https://gateway.test/pay", "SESS", null);
        public ProviderVerification? Verification { get; set; }
        public Func<string, ProviderVerification>? VerifyFor { get; set; }
        public ProviderRefundResult RefundResult { get; set; } = new(true, "RR-1", false, null);
        public ProviderRefundState RefundState { get; set; } = ProviderRefundState.Processing;
        public int VerifyCalls;
        public ProviderRefundRequest? LastRefund;

        public Task<ProviderInitResult> InitiateAsync(ProviderInitRequest request, CancellationToken ct = default) => Task.FromResult(InitResult);

        public ProviderCallback ParseCallback(IReadOnlyDictionary<string, string> f) =>
            new(f.GetValueOrDefault("tran_id"), f.GetValueOrDefault("status"), f.GetValueOrDefault("val_id"),
                f.TryGetValue("sig", out var s) ? s == "good" : null);

        public Task<ProviderVerification> VerifyAsync(string reference, string? valId, string? sessionKey, CancellationToken ct = default)
        {
            Interlocked.Increment(ref VerifyCalls);
            return Task.FromResult(VerifyFor?.Invoke(reference) ?? Verification ?? new ProviderVerification(VerificationStatus.NotFound, reference, null, null, 0, "BDT", null, null));
        }

        public Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken ct = default) { LastRefund = request; return Task.FromResult(RefundResult); }
        public Task<ProviderRefundState> GetRefundStateAsync(string providerRefundRef, CancellationToken ct = default) => Task.FromResult(RefundState);
    }

    private sealed class FixedTenant : ITenantContext
    {
        public int TenantId => 1;
        public bool IsFilterBypassed => true;
        public IDisposable UseTenant(int tenantId) => new Noop();
        public IDisposable BypassFilters() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTime Now = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }

    private sealed class Gw
    {
        public ApplicationDbContext Db = null!;
        public FakeProvider Provider = new();
        public Clock Time = new();
        public IPaymentGatewayService Gateway = null!;
        public IRefundService Refunds = null!;
        public ISubscriptionService Subscriptions = null!;
        public SaasInvoice Invoice = null!;

        public static async Task<Gw> CreateAsync()
        {
            var g = new Gw();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var tenant = new FixedTenant();
            g.Db = new ApplicationDbContext(options, tenant);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = new PlatformSettingsService(g.Db, cache);
            var audit = new SaasAuditService(g.Db, new HttpContextAccessor());
            var notifier = new SaasNotifier(g.Db);
            var invoices = new InvoiceService(g.Db, settings, notifier, audit, g.Time);
            var subs = new SubscriptionService(g.Db, invoices, settings, notifier, audit, cache, g.Time);
            g.Subscriptions = subs;
            var billing = new BillingPaymentService(g.Db, subs, notifier, audit, g.Time);
            var registry = new PaymentProviderRegistry([g.Provider]);
            g.Gateway = new PaymentGatewayService(g.Db, tenant, registry, billing, settings, notifier, audit, g.Time, NullLogger<PaymentGatewayService>.Instance);
            g.Refunds = new RefundService(g.Db, tenant, registry, notifier, audit, g.Time);

            g.Db.Tenants.AddRange(new Tenant { Id = 1, Name = "Acme", Code = "acme", ContactEmail = "a@acme.test" }, new Tenant { Id = 2, Name = "Other", Code = "other" });
            g.Db.PaymentProviders.Add(new PaymentProviderConfig { Code = "fake", DisplayName = "Fake", IsEnabled = true });
            var plan = new SubscriptionPlan { Code = "basic", Name = "Basic", Price = 1000, TaxPercent = 5, DiscountPercent = 10, GraceDays = 5 };
            g.Db.SubscriptionPlans.Add(plan);
            await g.Db.SaveChangesAsync();
            await subs.StartAsync(1, plan.Id, StartMode.InvoiceFirst);
            g.Invoice = await g.Db.SaasInvoices.FirstAsync();   // total 945.00
            return g;
        }

        public async Task<PaymentAttempt> StartAsync(int tenantId = 1)
        {
            var r = await Gateway.StartAsync(tenantId, Invoice.Id, "fake", "https://app.test", "owner@acme.test", "127.0.0.1");
            Assert.True(r.Succeeded, r.Error);
            return await Db.PaymentAttempts.AsNoTracking().FirstAsync(a => a.Reference == r.AttemptReference);
        }

        public ProviderVerification Paid(PaymentAttempt a, decimal? amount = null, string currency = "BDT", string txn = "BANK-1") =>
            new(VerificationStatus.Paid, a.Reference, "VAL-1", txn, amount ?? a.Amount, currency, "BKASH-BKash", null);

        public Task<CallbackResult> Callback(string source, string reference, string status = "VALID", Dictionary<string, string>? extra = null)
        {
            var f = new Dictionary<string, string> { ["tran_id"] = reference, ["status"] = status, ["val_id"] = "VAL-1" };
            foreach (var kv in extra ?? []) f[kv.Key] = kv.Value;
            return Gateway.HandleCallbackAsync("fake", source, f);
        }

        public Task<int> PaymentCount() => Db.SaasPayments.CountAsync();
        public async Task<SaasInvoice> InvoiceNow() => await Db.SaasInvoices.AsNoTracking().FirstAsync();
    }

    [Fact]
    public async Task Starting_a_payment_charges_the_outstanding_balance_never_a_client_amount()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();

        Assert.Equal(945.00m, attempt.Amount);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal("SESS", attempt.SessionKey);
        Assert.StartsWith("PA", attempt.Reference);
        Assert.True(attempt.Reference.Length <= 30);
    }

    [Fact]
    public async Task A_tenant_cannot_pay_or_even_see_another_tenants_invoice()
    {
        var g = await Gw.CreateAsync();
        var r = await g.Gateway.StartAsync(2, g.Invoice.Id, "fake", "https://app.test", "x", null);
        Assert.False(r.Succeeded);
        Assert.Equal(0, await g.Db.PaymentAttempts.CountAsync());
    }

    [Fact]
    public async Task Unavailable_provider_or_unpayable_invoice_is_refused()
    {
        var g = await Gw.CreateAsync();
        g.Provider.IsConfigured = false;
        Assert.False((await g.Gateway.StartAsync(1, g.Invoice.Id, "fake", "https://app.test", null, null)).Succeeded);
        g.Provider.IsConfigured = true;
        Assert.False((await g.Gateway.StartAsync(1, g.Invoice.Id, "does-not-exist", "https://app.test", null, null)).Succeeded);

        g.Db.PaymentProviders.First().IsEnabled = false;
        await g.Db.SaveChangesAsync();
        Assert.False((await g.Gateway.StartAsync(1, g.Invoice.Id, "fake", "https://app.test", null, null)).Succeeded);
    }

    [Fact]
    public async Task Gateway_initiation_failure_closes_the_attempt()
    {
        var g = await Gw.CreateAsync();
        g.Provider.InitResult = new(false, null, null, "Invalid Information");
        var r = await g.Gateway.StartAsync(1, g.Invoice.Id, "fake", "https://app.test", null, null);

        Assert.False(r.Succeeded);
        Assert.Equal("Invalid Information", r.Error);
        Assert.Equal(PaymentAttemptStatus.Failed, (await g.Db.PaymentAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Verified_ipn_applies_the_payment_activates_the_subscription_and_a_replay_changes_nothing()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = g.Paid(attempt);

        var first = await g.Callback("ipn", attempt.Reference);
        Assert.Equal("applied", first.Outcome);
        Assert.Equal(1, await g.PaymentCount());
        Assert.Equal(InvoiceStatus.Paid, (await g.InvoiceNow()).Status);
        Assert.Equal(SubscriptionStatus.Active, (await g.Db.TenantSubscriptions.AsNoTracking().FirstAsync()).Status);
        var periodEnd = (await g.Db.TenantSubscriptions.AsNoTracking().FirstAsync()).CurrentPeriodEnd;

        // The browser redirect and a second IPN arrive after: nothing new.
        var redirect = await g.Callback("success", attempt.Reference);
        var replay = await g.Callback("ipn", attempt.Reference);
        Assert.Equal("already-processed", redirect.Outcome);
        Assert.Equal("already-processed", replay.Outcome);
        Assert.Equal(1, await g.PaymentCount());
        Assert.Equal(periodEnd, (await g.Db.TenantSubscriptions.AsNoTracking().FirstAsync()).CurrentPeriodEnd);

        var payment = await g.Db.SaasPayments.AsNoTracking().SingleAsync();
        Assert.Equal("fake", payment.Provider);
        Assert.Equal("BANK-1", payment.ProviderTransactionId);
        Assert.Equal(attempt.Reference, payment.IdempotencyKey);
        Assert.Equal(PaymentAttemptStatus.Succeeded, (await g.Db.PaymentAttempts.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_success_redirect_alone_never_activates_anything_when_the_provider_does_not_confirm()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = new ProviderVerification(VerificationStatus.NotFound, attempt.Reference, null, null, 0, "BDT", null, null);

        var result = await g.Callback("success", attempt.Reference, status: "VALID");   // forged "VALID"

        Assert.Equal("not-paid-yet", result.Outcome);
        Assert.Equal(0, await g.PaymentCount());
        Assert.Equal(SubscriptionStatus.Expired, (await g.Db.TenantSubscriptions.AsNoTracking().FirstAsync()).Status);
        Assert.Equal(PaymentAttemptStatus.Pending, (await g.Db.PaymentAttempts.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Callback_with_a_bad_signature_is_rejected_without_asking_or_paying()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = g.Paid(attempt);

        var result = await g.Callback("ipn", attempt.Reference, extra: new() { ["sig"] = "bad" });

        Assert.Equal("bad-signature", result.Outcome);
        Assert.Equal(0, g.Provider.VerifyCalls);
        Assert.Equal(0, await g.PaymentCount());
        Assert.Contains(await g.Db.SaasAuditLogs.ToListAsync(), a => a.Action == "payment.callback-bad-signature");
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("reference")]
    public async Task Provider_confirmation_that_does_not_match_our_attempt_is_not_applied(string what)
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        var v = g.Paid(attempt);
        g.Provider.Verification = what switch
        {
            "amount" => v with { Amount = 1m },
            "currency" => v with { Currency = "USD" },
            _ => v with { Reference = "SOMEONE-ELSES" }
        };

        var result = await g.Callback("ipn", attempt.Reference);

        Assert.Equal("amount-or-reference-mismatch", result.Outcome);
        Assert.Equal(0, await g.PaymentCount());
        Assert.Equal(PaymentAttemptStatus.NeedsReview, (await g.Db.PaymentAttempts.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0m, (await g.InvoiceNow()).AmountPaid);
    }

    [Fact]
    public async Task Callbacks_for_unknown_references_or_without_one_are_recorded_and_ignored()
    {
        var g = await Gw.CreateAsync();
        Assert.Equal("unknown-reference", (await g.Callback("ipn", "PA-NOT-OURS")).Outcome);
        Assert.Equal("missing-reference", (await g.Gateway.HandleCallbackAsync("fake", "ipn", new Dictionary<string, string> { ["status"] = "VALID" })).Outcome);
        Assert.Equal("unknown-provider", (await g.Gateway.HandleCallbackAsync("nope", "ipn", new Dictionary<string, string>())).Outcome);
        Assert.Equal(0, await g.PaymentCount());
        Assert.True(await g.Db.PaymentWebhookEvents.CountAsync() >= 2);
    }

    [Fact]
    public async Task Cancel_and_failure_close_the_attempt_notify_and_leave_the_invoice_unpaid()
    {
        var g = await Gw.CreateAsync();
        var cancelled = await g.StartAsync();
        var failed = await g.StartAsync();

        await g.Callback("cancel", cancelled.Reference, status: "CANCELLED");
        await g.Callback("fail", failed.Reference, status: "FAILED");

        var attempts = await g.Db.PaymentAttempts.AsNoTracking().ToDictionaryAsync(a => a.Reference, a => a.Status);
        Assert.Equal(PaymentAttemptStatus.Cancelled, attempts[cancelled.Reference]);
        Assert.Equal(PaymentAttemptStatus.Failed, attempts[failed.Reference]);
        Assert.Equal(0, await g.PaymentCount());
        Assert.Equal(2, await g.Db.SaasNotifications.CountAsync(n => n.Type == "payment-failed" && n.Channel == NotificationChannel.InApp));
    }

    [Fact]
    public async Task Provider_unreachable_leaves_the_attempt_pending_and_the_reconcile_job_finishes_it_later()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = new ProviderVerification(VerificationStatus.Error, null, null, null, 0, "BDT", null, "down");

        Assert.Equal("verification-unavailable", (await g.Callback("ipn", attempt.Reference)).Outcome);
        Assert.Equal(0, await g.PaymentCount());

        g.Time.Now = g.Time.Now.AddMinutes(10);
        g.Provider.Verification = g.Paid(attempt);
        Assert.Equal(1, await g.Gateway.ReconcileAsync());

        Assert.Equal(1, await g.PaymentCount());
        Assert.Equal(SubscriptionStatus.Active, (await g.Db.TenantSubscriptions.AsNoTracking().FirstAsync()).Status);
        Assert.Equal(0, await g.Gateway.ReconcileAsync());   // idempotent
        Assert.Equal(1, await g.PaymentCount());
    }

    [Fact]
    public async Task Reconcile_expires_abandoned_attempts_only_when_the_provider_has_nothing()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = new ProviderVerification(VerificationStatus.NotFound, attempt.Reference, null, null, 0, "BDT", null, null);
        g.Time.Now = g.Time.Now.AddHours(3);

        await g.Gateway.ReconcileAsync();

        Assert.Equal(PaymentAttemptStatus.Expired, (await g.Db.PaymentAttempts.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Second_successful_payment_for_an_already_paid_invoice_is_flagged_for_review_not_applied()
    {
        var g = await Gw.CreateAsync();
        var first = await g.StartAsync();
        var second = await g.StartAsync();       // customer opened two gateway tabs
        g.Provider.VerifyFor = reference => reference == first.Reference ? g.Paid(first, txn: "BANK-1") : g.Paid(second, txn: "BANK-2");

        await g.Callback("ipn", first.Reference);
        var result = await g.Callback("ipn", second.Reference);

        Assert.Equal("needs-review", result.Outcome);
        Assert.Equal(1, await g.PaymentCount());
        Assert.Equal(PaymentAttemptStatus.NeedsReview, (await g.Db.PaymentAttempts.AsNoTracking().FirstAsync(a => a.Reference == second.Reference)).Status);
    }

    [Fact]
    public async Task Risky_payment_is_held_until_an_admin_releases_it_after_re_verification()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = g.Paid(attempt) with { Status = VerificationStatus.PaidHeld };

        Assert.Equal("held-for-review", (await g.Callback("ipn", attempt.Reference)).Outcome);
        Assert.Equal(0, await g.PaymentCount());

        var stored = await g.Db.PaymentAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentAttemptStatus.UnderReview, stored.Status);

        var released = await g.Gateway.ReleaseHeldAsync(stored.Id, "platform@saas.test");
        Assert.Equal(PaymentOutcome.Applied, released.Outcome);
        Assert.Equal(1, await g.PaymentCount());
        Assert.Equal(PaymentAttemptStatus.Succeeded, (await g.Db.PaymentAttempts.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_log_keeps_one_row_per_distinct_event_and_never_stores_secrets()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = g.Paid(attempt);
        var extra = new Dictionary<string, string> { ["store_passwd"] = "TOPSECRET", ["card_no"] = "4111XXXXXXXX1111", ["verify_sign"] = "abc" };

        await g.Callback("ipn", attempt.Reference, extra: extra);
        await g.Callback("ipn", attempt.Reference, extra: extra);

        var evt = await g.Db.PaymentWebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal(2, evt.ReceiveCount);
        Assert.DoesNotContain("TOPSECRET", evt.Payload);
        Assert.DoesNotContain("4111", evt.Payload);
        Assert.DoesNotContain("verify_sign", evt.Payload);
    }

    // ======================================================== refunds

    private static async Task<(Gw g, SaasPayment payment)> PaidAsync()
    {
        var g = await Gw.CreateAsync();
        var attempt = await g.StartAsync();
        g.Provider.Verification = g.Paid(attempt);
        await g.Callback("ipn", attempt.Reference);
        return (g, await g.Db.SaasPayments.AsNoTracking().SingleAsync());
    }

    [Fact]
    public async Task Gateway_refund_goes_through_the_provider_and_tracks_the_refundable_amount()
    {
        var (g, payment) = await PaidAsync();

        var partial = await g.Refunds.RefundAsync(payment.Id, 100m, "goodwill", "platform@saas.test");
        Assert.True(partial.Succeeded);
        Assert.Equal(RefundStatus.Pending, partial.Refund!.Status);
        Assert.Equal("BANK-1", g.Provider.LastRefund!.ProviderTransactionId);
        Assert.Equal(100m, (await g.Db.SaasPayments.AsNoTracking().SingleAsync()).RefundedAmount);
        Assert.Equal(SaasPaymentStatus.Succeeded, (await g.Db.SaasPayments.AsNoTracking().SingleAsync()).Status);

        Assert.False((await g.Refunds.RefundAsync(payment.Id, 900m, "too much")).Succeeded);       // only 845 left
        Assert.False((await g.Refunds.RefundAsync(payment.Id, 10m, "  ")).Succeeded);               // reason required

        var rest = await g.Refunds.RefundAsync(payment.Id, 845m, "cancelled contract");
        Assert.True(rest.Succeeded);
        Assert.Equal(SaasPaymentStatus.Refunded, (await g.Db.SaasPayments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(InvoiceStatus.Refunded, (await g.InvoiceNow()).Status);

        g.Provider.RefundState = ProviderRefundState.Refunded;
        Assert.Equal(2, await g.Refunds.RefreshPendingAsync());
        Assert.All(await g.Db.SaasRefunds.AsNoTracking().ToListAsync(), r => Assert.Equal(RefundStatus.Succeeded, r.Status));
    }

    [Fact]
    public async Task Provider_rejection_creates_no_refund_and_a_cancelled_one_gives_the_amount_back()
    {
        var (g, payment) = await PaidAsync();
        g.Provider.RefundResult = new(false, null, false, "not allowed");
        var rejected = await g.Refunds.RefundAsync(payment.Id, 50m, "x");
        Assert.False(rejected.Succeeded);
        Assert.Equal(0, await g.Db.SaasRefunds.CountAsync());
        Assert.Equal(0m, (await g.Db.SaasPayments.AsNoTracking().SingleAsync()).RefundedAmount);

        g.Provider.RefundResult = new(true, "RR-9", false, null);
        await g.Refunds.RefundAsync(payment.Id, 945m, "full");
        Assert.Equal(InvoiceStatus.Refunded, (await g.InvoiceNow()).Status);

        g.Provider.RefundState = ProviderRefundState.Cancelled;
        await g.Refunds.RefreshPendingAsync();

        Assert.Equal(0m, (await g.Db.SaasPayments.AsNoTracking().SingleAsync()).RefundedAmount);
        Assert.Equal(InvoiceStatus.Paid, (await g.InvoiceNow()).Status);
    }
}
