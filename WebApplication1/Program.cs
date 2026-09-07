using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Authorization;
using WebApplication1.Data;
using WebApplication1.Hubs;
using WebApplication1.Models.Identity;
using WebApplication1.Services;

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
builder.Services.AddControllersWithViews();

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

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddMemoryCache();
builder.Services.AddSignalR();

builder.Services.AddScoped<ICompanySettingsService, CompanySettingsService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IAccountingService, AccountingService>();
builder.Services.AddScoped<IForecastService, ForecastService>();
builder.Services.AddScoped<IActivityNotifier, ActivityNotifier>();
builder.Services.AddScoped<IChatbotService, ChatbotService>();
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
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<NotificationsHub>("/hubs/notifications");

await DbInitializer.SeedAsync(app.Services);

app.Run();
