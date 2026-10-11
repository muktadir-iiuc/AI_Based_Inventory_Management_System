using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;

namespace WebApplication1.Services.Saas;

public interface ITenantPortalService
{
    /// <summary>
    /// The tenant a sign-in page belongs to: the tenant whose own portal URL has this host, else the tenant with this code
    /// (<c>/t/{code}</c>), else none (the shared main sign-in page). Disabled/terminated/purged tenants never resolve.
    /// </summary>
    Task<Tenant?> ResolveAsync(string? host, string? code);

    /// <summary>Links a platform admin can hand to a tenant. <paramref name="requestBase"/> is the current request's scheme + host.</summary>
    Task<TenantLinks> GetLinksAsync(Tenant tenant, string requestBase);

    /// <summary>Sets or clears the tenant's own portal URL (e.g. https://shop.example.com). Returns an error message or null.</summary>
    Task<string?> SetPortalUrlAsync(int tenantId, string? url, string? actor = null);
}

public record TenantLinks(string TenantSignIn, string MainSignIn, string? CustomPortal, string BaseUrl, bool BaseUrlConfigured);

public class TenantPortalService(ApplicationDbContext db, IPlatformSettings settings, ISaasAudit audit) : ITenantPortalService
{
    public async Task<Tenant?> ResolveAsync(string? host, string? code)
    {
        Tenant? tenant = null;
        if (!string.IsNullOrWhiteSpace(host))
        {
            var h = host.Trim().ToLowerInvariant();
            tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.PortalHost == h);
        }
        if (tenant is null && !string.IsNullOrWhiteSpace(code))
        {
            var c = code.Trim().ToLowerInvariant();
            tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Code == c);
        }
        return tenant is { Status: not (TenantStatus.Disabled or TenantStatus.Terminated), PurgedAt: null } ? tenant : null;
    }

    public async Task<TenantLinks> GetLinksAsync(Tenant tenant, string requestBase)
    {
        var configured = (await settings.GetAsync(PlatformSettingKeys.PlatformPublicUrl)).Trim().TrimEnd('/');
        var baseUrl = configured.Length > 0 ? configured : requestBase.TrimEnd('/');
        return new TenantLinks(
            TenantSignIn: $"{baseUrl}/t/{tenant.Code}",
            MainSignIn: $"{baseUrl}/Account/Login",
            CustomPortal: tenant.PortalUrl,
            BaseUrl: baseUrl,
            BaseUrlConfigured: configured.Length > 0);
    }

    public async Task<string?> SetPortalUrlAsync(int tenantId, string? url, string? actor = null)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return "Tenant not found.";

        string? normalized = null, host = null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                return "Enter a full web address such as https://shop.example.com";
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || (uri.AbsolutePath.Length > 1))
                return "Use only the address root (no path, query or login details).";
            if (uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.'))
                return "Use a real domain name (for example shop.example.com).";

            host = uri.Host.ToLowerInvariant();
            normalized = $"{uri.Scheme}://{uri.Authority.ToLowerInvariant()}";

            // The shared main address must keep working for everyone.
            var main = (await settings.GetAsync(PlatformSettingKeys.PlatformPublicUrl)).Trim();
            if (Uri.TryCreate(main, UriKind.Absolute, out var mainUri) && string.Equals(mainUri.Host, host, StringComparison.OrdinalIgnoreCase))
                return "That is the platform's own address; give the tenant a different one.";
            if (await db.Tenants.AnyAsync(t => t.PortalHost == host && t.Id != tenantId))
                return "Another tenant already uses that address.";
        }

        var before = tenant.PortalUrl;
        tenant.PortalUrl = normalized;
        tenant.PortalHost = host;
        tenant.UpdatedAt = DateTime.UtcNow;
        audit.Add("tenant.portal-url-changed", nameof(Tenant), tenantId.ToString(), tenantId, details: $"{before ?? "(none)"} -> {normalized ?? "(none)"}");
        await db.SaveChangesAsync();
        return null;
    }
}
