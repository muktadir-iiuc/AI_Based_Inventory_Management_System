using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models.Saas;

namespace WebApplication1.Services.Saas;

// ---------------------------------------------------------------- platform settings

public interface IPlatformSettings
{
    Task<string> GetAsync(string key);
    Task<int> GetIntAsync(string key, int fallback);
    Task<decimal> GetDecimalAsync(string key, decimal fallback);
    Task SetAsync(string key, string value, string? actor);
    Task<Dictionary<string, string>> GetAllAsync();
}

public class PlatformSettingsService(ApplicationDbContext db, IMemoryCache cache) : IPlatformSettings
{
    private const string CacheKey = "platform-settings";

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<string, string>? cached) && cached is not null)
        {
            return cached;
        }

        var stored = await db.PlatformSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        var merged = new Dictionary<string, string>(PlatformSettingKeys.Defaults);
        foreach (var (key, value) in stored)
        {
            merged[key] = value;
        }
        cache.Set(CacheKey, merged, TimeSpan.FromSeconds(30));
        return merged;
    }

    public async Task<string> GetAsync(string key) =>
        (await GetAllAsync()).TryGetValue(key, out var value) ? value : string.Empty;

    public async Task<int> GetIntAsync(string key, int fallback) =>
        int.TryParse(await GetAsync(key), out var value) ? value : fallback;

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback) =>
        decimal.TryParse(await GetAsync(key), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public async Task SetAsync(string key, string value, string? actor)
    {
        if (!PlatformSettingKeys.Defaults.ContainsKey(key))
        {
            throw new ArgumentException($"Unknown setting '{key}'.", nameof(key));
        }

        var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            row = new PlatformSetting { Key = key };
            db.PlatformSettings.Add(row);
        }
        row.Value = value;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
    }
}

// ---------------------------------------------------------------- audit

public interface ISaasAudit
{
    /// <summary>Name of whoever is acting: the signed-in user's email, or "system" for jobs and callbacks.</summary>
    string Actor { get; }

    /// <summary>Stages an audit row on the shared DbContext so it commits (or rolls back) with the change it describes.</summary>
    void Add(string action, string? entityType = null, string? entityId = null, int? tenantId = null,
        string result = "Success", string? details = null);

    Task LogAsync(string action, string? entityType = null, string? entityId = null, int? tenantId = null,
        string result = "Success", string? details = null);
}

public class SaasAuditService(ApplicationDbContext db, IHttpContextAccessor accessor) : ISaasAudit
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string Actor => User?.Identity?.IsAuthenticated == true
        ? (User.FindFirstValue(ClaimTypes.Email) ?? User.Identity.Name ?? "unknown")
        : "system";

    public void Add(string action, string? entityType = null, string? entityId = null, int? tenantId = null,
        string result = "Success", string? details = null)
    {
        db.SaasAuditLogs.Add(new SaasAuditLog
        {
            ActorUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier),
            ActorName = Actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            TenantId = tenantId,
            Result = result,
            // Details are free text written by our own code; callers must never pass secrets, tokens or card data.
            Details = details is { Length: > 1000 } ? details[..1000] : details,
            IpAddress = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
    }

    public async Task LogAsync(string action, string? entityType = null, string? entityId = null, int? tenantId = null,
        string result = "Success", string? details = null)
    {
        Add(action, entityType, entityId, tenantId, result, details);
        await db.SaveChangesAsync();
    }
}

// ---------------------------------------------------------------- notifications

public interface ISaasNotifier
{
    /// <summary>
    /// Queues an in-app notice and, when the tenant has a billing email, an email. Safe to call repeatedly
    /// with the same <paramref name="dedupeKey"/>: the event is only stored once. Staged on the shared
    /// DbContext - the caller saves.
    /// </summary>
    Task QueueAsync(int tenantId, string type, string subject, string body, string dedupeKey);
}

public class SaasNotifier(ApplicationDbContext db) : ISaasNotifier
{
    public async Task QueueAsync(int tenantId, string type, string subject, string body, string dedupeKey)
    {
        var staged = db.ChangeTracker.Entries<SaasNotification>()
            .Any(e => e.Entity.TenantId == tenantId && e.Entity.DedupeKey == dedupeKey);
        if (staged || await db.SaasNotifications.AnyAsync(n => n.TenantId == tenantId && n.DedupeKey == dedupeKey))
        {
            return;
        }

        db.SaasNotifications.Add(new SaasNotification
        {
            TenantId = tenantId, Channel = NotificationChannel.InApp, Type = type, Subject = subject,
            Body = body, DedupeKey = dedupeKey, Status = NotificationStatus.Sent, SentAt = DateTime.UtcNow
        });

        var email = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.BillingEmail ?? t.ContactEmail).FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(email))
        {
            db.SaasNotifications.Add(new SaasNotification
            {
                TenantId = tenantId, Channel = NotificationChannel.Email, Type = type, Subject = subject,
                Body = body, ToEmail = email, DedupeKey = dedupeKey
            });
        }
    }
}
