using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;

namespace WebApplication1.Services.Saas;

/// <summary>The data agreement every subscriber accepts: who owns the data, how it is handled, how to get a copy and what happens after the plan ends.
/// Bump <see cref="Version"/> when the wording changes; tenants are then asked to accept again.</summary>
public static class DataAgreement
{
    public const string Version = "2026-10";

    public static IReadOnlyList<(string Heading, string Body)> Sections(int retentionDays) =>
    [
        ("1. Your data belongs to you", "All business data you enter or generate in the service (products, stock, customers, suppliers, invoices, payments, accounts and the business records of your users) is yours. We do not claim ownership of it and we do not sell it."),
        ("2. How we use it", "We process your data only to provide, secure, support and bill the service, and as required by law. Platform staff access it only to operate the service or at your request, and such access is logged."),
        ("3. Isolation and security", "Each business's data is kept separate from every other business. We protect it with access control, encrypted connections, audit logging and regular backups. You are responsible for keeping your users' passwords safe and for the lawful content of the data you enter."),
        ("4. Getting a copy of your data", "You can ask for a complete copy of every table that holds your data, in CSV, JSON or Excel (XLSX) format, at any time - in particular when you end your plan or move to another system. Ask from Billing > Data & agreement. The platform operator prepares the export, and the download stays available for a limited time."),
        ("5. When your plan ends", $"If you cancel, let the plan expire or are suspended, your data is kept, but access is limited. After a plan has ended, your data stays recoverable for {retentionDays} days. Request your export before this period ends; afterwards the data may be permanently erased and cannot be restored."),
        ("6. Moving to another provider", "Exported files are plain tables so another system can import them. Passwords and security tokens are never exported."),
        ("7. Changes", "If this agreement changes, we ask you to accept the new version in your billing portal. Your acceptance of each version, with the date and user, is recorded with your subscription.")
    ];
}

public interface IDataAgreementService
{
    Task<DataAgreementAcceptance?> LatestAsync(int tenantId);
    Task<bool> HasAcceptedCurrentAsync(int tenantId);

    /// <summary>Stages an acceptance of the current version (idempotent per tenant+version) on the shared context; the caller saves.</summary>
    Task AcceptAsync(int tenantId, string context, string? userId, string? name, int? subscriptionId = null);
}

public class DataAgreementService(ApplicationDbContext db, IHttpContextAccessor accessor) : IDataAgreementService
{
    public Task<DataAgreementAcceptance?> LatestAsync(int tenantId) =>
        db.DataAgreementAcceptances.AsNoTracking().Where(a => a.TenantId == tenantId).OrderByDescending(a => a.AcceptedAt).FirstOrDefaultAsync();

    public Task<bool> HasAcceptedCurrentAsync(int tenantId) =>
        db.DataAgreementAcceptances.AnyAsync(a => a.TenantId == tenantId && a.Version == DataAgreement.Version);

    public async Task AcceptAsync(int tenantId, string context, string? userId, string? name, int? subscriptionId = null)
    {
        if (await HasAcceptedCurrentAsync(tenantId)) return;
        db.DataAgreementAcceptances.Add(new DataAgreementAcceptance
        {
            TenantId = tenantId,
            SubscriptionId = subscriptionId,
            Version = DataAgreement.Version,
            Context = context,
            AcceptedByUserId = userId,
            AcceptedBy = name,
            IpAddress = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
    }
}
