using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

public class TenantAccessPanelTests
{
    private sealed class NoTenant : ITenantContext
    {
        public int TenantId => 0;
        public bool IsFilterBypassed => true;
        public IDisposable UseTenant(int t) => new Noop();
        public IDisposable BypassFilters() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private static async Task<(ApplicationDbContext Db, TenantPortalService Portal)> NewAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new NoTenant());
        db.Tenants.AddRange(
            new Tenant { Id = 1, Name = "Acme Tyres", Code = "acme" },
            new Tenant { Id = 2, Name = "Beta Traders", Code = "beta" },
            new Tenant { Id = 3, Name = "Gone Ltd", Code = "gone", Status = TenantStatus.Terminated },
            new Tenant { Id = 4, Name = "Off Ltd", Code = "off", Status = TenantStatus.Disabled });
        await db.SaveChangesAsync();
        var settings = new PlatformSettingsService(db, new MemoryCache(new MemoryCacheOptions()));
        return (db, new TenantPortalService(db, settings, new SaasAuditService(db, new HttpContextAccessor())));
    }

    // ------------------------------------------------------------ passwords

    [Fact]
    public void Generated_passwords_meet_the_application_password_rules_and_differ_each_time()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var p = PasswordGenerator.Create();
            Assert.True(p.Length >= 14);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => !char.IsLetterOrDigit(c));
            Assert.DoesNotContain(p, c => "0O1lI".Contains(c));
            seen.Add(p);
        }
        Assert.Equal(200, seen.Count);
    }

    // ------------------------------------------------------------ portal URL

    [Fact]
    public async Task Portal_url_is_normalised_unique_and_clearable()
    {
        var (db, portal) = await NewAsync();

        Assert.Null(await portal.SetPortalUrlAsync(1, "HTTPS://Shop.Example.com/ "));
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal("https://shop.example.com", t.PortalUrl);
        Assert.Equal("shop.example.com", t.PortalHost);

        Assert.Contains("already uses", await portal.SetPortalUrlAsync(2, "https://shop.example.com"));
        Assert.Null(await portal.SetPortalUrlAsync(1, "https://shop.example.com"));            // same tenant re-saving is fine

        Assert.Null(await portal.SetPortalUrlAsync(1, ""));
        Assert.Null((await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == 1)).PortalHost);
        Assert.Contains(await db.SaasAuditLogs.ToListAsync(), l => l.Action == "tenant.portal-url-changed" && l.TenantId == 1);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://shop.example.com")]
    [InlineData("https://user:pw@shop.example.com")]
    [InlineData("https://shop.example.com/some/path")]
    [InlineData("https://shop.example.com/?x=1")]
    [InlineData("https://localhost")]
    [InlineData("https://192.168.1.10")]
    public async Task Unsafe_or_malformed_portal_urls_are_refused(string url)
    {
        var (db, portal) = await NewAsync();
        Assert.NotNull(await portal.SetPortalUrlAsync(1, url));
        Assert.Null((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == 1)).PortalUrl);
    }

    [Fact]
    public async Task A_tenant_cannot_take_over_the_platforms_own_address()
    {
        var (db, portal) = await NewAsync();
        db.PlatformSettings.Add(new PlatformSetting { Key = PlatformSettingKeys.PlatformPublicUrl, Value = "https://app.example.com" });
        await db.SaveChangesAsync();
        // settings are cached for 30 s; build a fresh service
        var fresh = new TenantPortalService(db, new PlatformSettingsService(db, new MemoryCache(new MemoryCacheOptions())), new SaasAuditService(db, new HttpContextAccessor()));

        Assert.Contains("platform's own address", await fresh.SetPortalUrlAsync(1, "https://app.example.com"));
    }

    // ------------------------------------------------------------ resolving which tenant a sign-in page belongs to

    [Fact]
    public async Task Sign_in_page_resolves_by_the_tenants_own_domain_first_then_by_code()
    {
        var (_, portal) = await NewAsync();
        await portal.SetPortalUrlAsync(1, "https://shop.example.com");

        Assert.Equal("acme", (await portal.ResolveAsync("shop.example.com", null))!.Code);
        Assert.Equal("acme", (await portal.ResolveAsync("SHOP.example.com", "beta"))!.Code);     // the domain wins over a code in the query
        Assert.Equal("beta", (await portal.ResolveAsync("app.example.com", "BETA"))!.Code);
        Assert.Null(await portal.ResolveAsync("app.example.com", null));                         // the shared main page: any user
        Assert.Null(await portal.ResolveAsync("app.example.com", "no-such-code"));
    }

    [Fact]
    public async Task Disabled_and_terminated_tenants_do_not_resolve()
    {
        var (_, portal) = await NewAsync();
        Assert.Null(await portal.ResolveAsync(null, "gone"));
        Assert.Null(await portal.ResolveAsync(null, "off"));
    }

    [Fact]
    public async Task Links_use_the_configured_public_url_else_the_request_address()
    {
        var (db, portal) = await NewAsync();
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == 1);

        var fallback = await portal.GetLinksAsync(tenant, "https://localhost:7127/");
        Assert.Equal("https://localhost:7127/t/acme", fallback.TenantSignIn);
        Assert.Equal("https://localhost:7127/Account/Login", fallback.MainSignIn);
        Assert.False(fallback.BaseUrlConfigured);

        db.PlatformSettings.Add(new PlatformSetting { Key = PlatformSettingKeys.PlatformPublicUrl, Value = "https://app.example.com/" });
        await db.SaveChangesAsync();
        var configured = await new TenantPortalService(db, new PlatformSettingsService(db, new MemoryCache(new MemoryCacheOptions())), new SaasAuditService(db, new HttpContextAccessor())).GetLinksAsync(tenant, "https://localhost:7127");
        Assert.Equal("https://app.example.com/t/acme", configured.TenantSignIn);
        Assert.True(configured.BaseUrlConfigured);
    }

    // ------------------------------------------------------------ authorization of the credential actions

    [Fact]
    public void Credential_and_url_actions_are_platform_admin_only()
    {
        var actions = new[] { "ResetPassword", "ToggleUser", "UnlockUser", "SignOutUser", "ChangeUserEmail", "PortalUrl", "AddAdmin", "Owner" };
        foreach (var name in actions)
        {
            var method = typeof(PlatformTenantsController).GetMethod(name);
            Assert.NotNull(method);
            Assert.Contains(method!.GetCustomAttributes<AuthorizeAttribute>(), a => a.Roles == Roles.PlatformAdmin);
            Assert.NotEmpty(method.GetCustomAttributes<HttpPostAttribute>());
            Assert.NotEmpty(method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
        }
    }

    [Fact]
    public void The_tenant_landing_page_is_anonymous_but_everything_in_platform_is_not()
    {
        Assert.NotEmpty(typeof(TenantPortalController).GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Empty(typeof(PlatformTenantsController).GetCustomAttributes<AllowAnonymousAttribute>(true));
    }
}
