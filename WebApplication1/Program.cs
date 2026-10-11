using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Authorization;
using WebApplication1.Data;
using WebApplication1.Hubs;
using WebApplication1.Middleware;
using WebApplication1.Models.Identity;
using WebApplication1.Services;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Saas.Payments;
using System.Threading.RateLimiting;
using WebApplication1.Services.Tenancy;

// Every `.ToString("C")` call in the views renders through this culture, so switching the
// symbol here (rather than editing every view) changes currency display across the whole app.
var currencyCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
currencyCulture.NumberFormat.CurrencySymbol = "৳";
CultureInfo.DefaultThreadCurrentCulture = currencyCulture;
CultureInfo.DefaultThreadCurrentUICulture = currencyCulture;

// The RDLC PDF renderer (AspNetCore.Reporting) looks up legacy Windows code pages that
// .NET's built-in encodings no longer ship; this registers the ones it needs at startup.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var mvcBuilder = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    // Lets .cshtml edits take effect on the next request instead of requiring a full
    // rebuild + app restart — views are precompiled into the assembly otherwise.
    mvcBuilder.AddRazorRuntimeCompilation();
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Identity re-checks a signed-in user's security stamp only every 30 minutes by default. A platform admin's password reset or
// "sign out everywhere" must bite quickly, so re-check every minute.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    // HttpOnly is the default. Outside development the auth cookie is only ever sent over HTTPS.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Anything under /Platform sends a signed-out visitor to the platform console sign-in, not the business sign-in page.
    options.Events.OnRedirectToLogin = context =>
    {
        var target = context.Request.Path.StartsWithSegments("/Platform", StringComparison.OrdinalIgnoreCase)
            ? "/Platform/Login?ReturnUrl=" + Uri.EscapeDataString(context.Request.Path + context.Request.QueryString)
            : context.RedirectUri;
        context.Response.Redirect(target);
        return Task.CompletedTask;
    };
});

// Behind a reverse proxy / load balancer the real client address and scheme arrive in X-Forwarded-* headers. They are honoured
// ONLY from proxies listed in Hosting:KnownProxies (comma separated IPs); otherwise any client could spoof its address and
// dodge the rate limits. Without the setting nothing is trusted.
var knownProxies = (builder.Configuration["Hosting:KnownProxies"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (knownProxies.Length > 0)
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in knownProxies)
        {
            options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
        }
    });
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// SaaS billing: plans, subscriptions, invoices, payments, lifecycle automation.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPlatformSettings, PlatformSettingsService>();
builder.Services.AddScoped<ISaasAudit, SaasAuditService>();
builder.Services.AddScoped<ISaasNotifier, SaasNotifier>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IBillingNoticeService, BillingNoticeService>();
builder.Services.AddScoped<ISaasInvoicePdfService, SaasInvoicePdfService>();
builder.Services.AddScoped<ITenantPortalService, TenantPortalService>();
builder.Services.AddScoped<IPlatformBranding, PlatformBrandingService>();
builder.Services.AddScoped<IDataAgreementService, DataAgreementService>();
builder.Services.AddScoped<IDataExportService, DataExportService>();
builder.Services.AddScoped<IPlatformMetricsService, PlatformMetricsService>();
builder.Services.AddScoped<ITenantRetentionService, TenantRetentionService>();
builder.Services.AddScoped<IBillingPaymentService, BillingPaymentService>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<ISubscriptionLifecycleService, SubscriptionLifecycleService>();
builder.Services.AddScoped<ISaasNotificationDispatcher, SaasNotificationDispatcher>();
builder.Services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
builder.Services.AddHostedService<BillingSchedulerService>();

// Payment gateways. Credentials come only from configuration (user-secrets / environment variables).
builder.Services.Configure<SslCommerzOptions>(builder.Configuration.GetSection(SslCommerzOptions.Section));
// The SSLCOMMERZ query/validation endpoints take the store password in the URL: never let the HTTP client log request URLs.
builder.Services.AddHttpClient(SslCommerzProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(25)).RemoveAllLoggers();
builder.Services.AddScoped<IPaymentProvider, SslCommerzProvider>();
builder.Services.AddScoped<IPaymentProviderRegistry, PaymentProviderRegistry>();
builder.Services.AddScoped<IPaymentGatewayService, PaymentGatewayService>();
builder.Services.AddScoped<IRefundService, RefundService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Gateway callbacks/IPNs: generous but bounded per source address.
    options.AddPolicy("payment-callback", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    // Starting a payment / submitting a manual payment: per signed-in user.
    options.AddPolicy("payment-start", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // Self-service sign-up: a handful per address per hour.
    options.AddPolicy("register", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1) }));
    // Password guessing: per client address.
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:LoginPerFiveMinutes", 15), Window = TimeSpan.FromMinutes(5) }));
});
builder.Services.AddMemoryCache();
builder.Services.AddSignalR();

// Lets [ValidateAntiForgeryToken] accept the token via this header on JSON fetch() calls
// (the chatbot widget and Petty Cash page both send it this way) instead of only form fields.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

builder.Services.AddScoped<ICompanySettingsService, CompanySettingsService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IFifoAllocationService, FifoAllocationService>();
builder.Services.AddScoped<IAccountingService, AccountingService>();
builder.Services.AddScoped<IPartyPaymentService, PartyPaymentService>();
builder.Services.AddScoped<IForecastService, ForecastService>();
builder.Services.AddScoped<IActivityNotifier, ActivityNotifier>();
builder.Services.AddScoped<IChatbotService, ChatbotService>();
builder.Services.AddScoped<IProductPriceService, ProductPriceService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
// Password-reset links expire after 1 hour (Identity default is 1 day).
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();
builder.Services.AddHostedService<BackupSchedulerService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission.Key, policy => policy.Requirements.Add(new PermissionRequirement(permission.Key)));
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (knownProxies.Length > 0)
{
    app.UseForwardedHeaders();
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<UserActivityMiddleware>();
app.UseMiddleware<TenantAccessMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();

// Liveness/readiness for load balancers and uptime monitors: anonymous, reveals nothing but ok / unavailable.
app.MapGet("/health", async (WebApplication1.Data.ApplicationDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok("ok") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<NotificationsHub>("/hubs/notifications");

await DbInitializer.SeedAsync(app.Services);

app.Run();
