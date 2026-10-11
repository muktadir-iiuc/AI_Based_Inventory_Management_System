using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services.Saas;

// ============================================================ dashboard metrics

public record RenewalRow(int TenantId, string TenantName, DateTime When, decimal Amount, string Currency, string PlanName);

public record PlatformDashboard(
    int TotalTenants, int ActiveTenants, int TrialTenants, int SuspendedTenants, int DisabledTenants, int TerminatedTenants,
    int ExpiredSubscriptions, int TenantsWithOverdueInvoices,
    decimal Mrr, decimal Arr, string Currency,
    int PaidInvoices, int UnpaidInvoices, decimal UnpaidAmount, decimal CollectedThisMonth,
    int FailedPayments30d, int PendingPayments,
    IReadOnlyList<RenewalRow> UpcomingRenewals, IReadOnlyList<RenewalRow> UpcomingTrialExpirations,
    IReadOnlyList<SaasAuditLog> RecentActivity);

public record RevenuePoint(string Period, decimal Collected, decimal Refunded, int Payments)
{
    public decimal Net => Collected - Refunded;
}

public record OutstandingRow(int TenantId, string TenantName, int Invoices, decimal Balance, DateTime OldestDue, int OverdueInvoices, string Currency);

public interface IPlatformMetricsService
{
    Task<PlatformDashboard> GetDashboardAsync();
    Task<IReadOnlyList<RevenuePoint>> GetRevenueAsync(int year, bool byMonth);
    Task<IReadOnlyList<OutstandingRow>> GetOutstandingAsync();
}

/// <summary>Every figure is computed from stored records at request time; nothing is cached or estimated.</summary>
public class PlatformMetricsService(ApplicationDbContext db, TimeProvider time) : IPlatformMetricsService
{
    private static readonly SubscriptionStatus[] Paying = [SubscriptionStatus.Active, SubscriptionStatus.PastDue, SubscriptionStatus.GracePeriod];

    public async Task<PlatformDashboard> GetDashboardAsync()
    {
        var now = time.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var tenants = await db.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name, t.Status }).ToListAsync();
        var subs = await db.TenantSubscriptions.AsNoTracking().Include(s => s.Plan).ToListAsync();
        var subByTenant = subs.ToDictionary(s => s.TenantId);
        var live = tenants.Where(t => t.Status is not (TenantStatus.Terminated)).ToList();

        // MRR: what paying subscriptions bill per month, net of discount, before tax. Only the platform's main currency is summed.
        var currency = await db.PlatformSettings.AsNoTracking().Where(p => p.Key == PlatformSettingKeys.DefaultCurrency).Select(p => p.Value).FirstOrDefaultAsync()
            ?? PlatformSettingKeys.Defaults[PlatformSettingKeys.DefaultCurrency];
        decimal mrr = 0;
        foreach (var s in subs.Where(s => Paying.Contains(s.Status) && s.Price > 0 && !s.CancelAtPeriodEnd && s.Currency == currency
                                          && live.Any(t => t.Id == s.TenantId && t.Status != TenantStatus.Disabled)))
        {
            var months = s.IntervalUnit == BillingIntervalUnit.Month ? s.IntervalCount : s.IntervalCount / 30.4375m;
            mrr += BillingMath.Round(s.Price * (1 - s.DiscountPercent / 100m)) / months;
        }
        mrr = BillingMath.Round(mrr);

        var openStatuses = new[] { InvoiceStatus.Pending, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue };
        var unpaid = await db.SaasInvoices.AsNoTracking().Where(i => openStatuses.Contains(i.Status)).Select(i => new { i.Total, i.AmountPaid }).ToListAsync();
        var collected = await db.SaasPayments.AsNoTracking()
            .Where(p => (p.Status == SaasPaymentStatus.Succeeded || p.Status == SaasPaymentStatus.Refunded) && p.ReceivedAt >= monthStart)
            .Select(p => new { p.Amount, p.RefundedAmount }).ToListAsync();
        var failedSince = now.AddDays(-30);

        string Name(int id) => tenants.FirstOrDefault(t => t.Id == id)?.Name ?? $"Tenant {id}";
        var renewals = subs.Where(s => Paying.Contains(s.Status) && s.AutoRenew && !s.CancelAtPeriodEnd && s.CurrentPeriodEnd >= now && s.CurrentPeriodEnd <= now.AddDays(7) && s.Price > 0)
            .OrderBy(s => s.CurrentPeriodEnd)
            .Select(s => new RenewalRow(s.TenantId, Name(s.TenantId), s.CurrentPeriodEnd!.Value, BillingMath.Compute(s.Price, s.DiscountPercent, s.TaxPercent).Total, s.Currency, s.Plan?.Name ?? "")).ToList();
        var trials = subs.Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt >= now && s.TrialEndsAt <= now.AddDays(7))
            .OrderBy(s => s.TrialEndsAt)
            .Select(s => new RenewalRow(s.TenantId, Name(s.TenantId), s.TrialEndsAt!.Value, 0, s.Currency, s.Plan?.Name ?? "")).ToList();

        return new PlatformDashboard(
            TotalTenants: tenants.Count,
            ActiveTenants: live.Count(t => t.Status == TenantStatus.Active && subByTenant.TryGetValue(t.Id, out var s) && Paying.Contains(s.Status)),
            TrialTenants: live.Count(t => subByTenant.TryGetValue(t.Id, out var s) && s.Status == SubscriptionStatus.Trialing),
            SuspendedTenants: tenants.Count(t => t.Status == TenantStatus.Suspended),
            DisabledTenants: tenants.Count(t => t.Status == TenantStatus.Disabled),
            TerminatedTenants: tenants.Count(t => t.Status == TenantStatus.Terminated),
            ExpiredSubscriptions: subs.Count(s => s.Status == SubscriptionStatus.Expired),
            TenantsWithOverdueInvoices: await db.SaasInvoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Overdue).Select(i => i.TenantId).Distinct().CountAsync(),
            Mrr: mrr, Arr: mrr * 12, Currency: currency,
            PaidInvoices: await db.SaasInvoices.CountAsync(i => i.Status == InvoiceStatus.Paid),
            UnpaidInvoices: unpaid.Count,
            UnpaidAmount: unpaid.Sum(i => i.Total - i.AmountPaid),
            CollectedThisMonth: collected.Sum(p => p.Amount - p.RefundedAmount),
            FailedPayments30d: await db.PaymentAttempts.CountAsync(a => (a.Status == PaymentAttemptStatus.Failed || a.Status == PaymentAttemptStatus.Cancelled || a.Status == PaymentAttemptStatus.Expired) && a.CreatedAt >= failedSince),
            PendingPayments: await db.PaymentAttempts.CountAsync(a => a.Status == PaymentAttemptStatus.Pending || a.Status == PaymentAttemptStatus.UnderReview || a.Status == PaymentAttemptStatus.NeedsReview)
                             + await db.SaasPayments.CountAsync(p => p.Status == SaasPaymentStatus.AwaitingConfirmation),
            UpcomingRenewals: renewals,
            UpcomingTrialExpirations: trials,
            RecentActivity: await db.SaasAuditLogs.AsNoTracking().OrderByDescending(a => a.Id).Take(12).ToListAsync());
    }

    public async Task<IReadOnlyList<RevenuePoint>> GetRevenueAsync(int year, bool byMonth)
    {
        var rows = await db.SaasPayments.AsNoTracking()
            .Where(p => (p.Status == SaasPaymentStatus.Succeeded || p.Status == SaasPaymentStatus.Refunded) && p.ReceivedAt.Year == year)
            .Select(p => new { p.ReceivedAt, p.Amount, p.RefundedAmount }).ToListAsync();

        if (byMonth)
        {
            return Enumerable.Range(1, 12).Select(m =>
            {
                var r = rows.Where(x => x.ReceivedAt.Month == m).ToList();
                return new RevenuePoint(new DateTime(year, m, 1).ToString("MMM yyyy"), r.Sum(x => x.Amount), r.Sum(x => x.RefundedAmount), r.Count);
            }).ToList();
        }
        return [new RevenuePoint(year.ToString(), rows.Sum(x => x.Amount), rows.Sum(x => x.RefundedAmount), rows.Count)];
    }

    public async Task<IReadOnlyList<OutstandingRow>> GetOutstandingAsync()
    {
        var open = new[] { InvoiceStatus.Pending, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue };
        var invoices = await db.SaasInvoices.AsNoTracking().Include(i => i.Tenant).Where(i => open.Contains(i.Status)).ToListAsync();
        return invoices.GroupBy(i => new { i.TenantId, i.Currency })
            .Select(g => new OutstandingRow(g.Key.TenantId, g.First().Tenant?.Name ?? $"Tenant {g.Key.TenantId}", g.Count(), g.Sum(i => i.Total - i.AmountPaid),
                g.Min(i => i.DueDate), g.Count(i => i.Status == InvoiceStatus.Overdue), g.Key.Currency))
            .OrderByDescending(r => r.OverdueInvoices).ThenByDescending(r => r.Balance).ToList();
    }
}

// ============================================================ retention / permanent deletion

public interface ITenantRetentionService
{
    /// <summary>Earliest date the business data of a terminated tenant may be permanently erased.</summary>
    Task<DateTime?> GetPurgeEligibleOnAsync(int tenantId);

    /// <summary>
    /// Irreversibly erases a terminated tenant's business data and users. Invoices, payments, refunds, subscription history and the audit
    /// log are kept as financial records. Refuses unless the tenant is terminated, past the retention period, not purged yet, and the caller
    /// typed the tenant code.
    /// </summary>
    Task PurgeAsync(int tenantId, string confirmationCode, string? actor = null);
}

public class TenantRetentionService(ApplicationDbContext db, ITenantContext tenantContext, IPlatformSettings settings, ISaasAudit audit, TimeProvider time) : ITenantRetentionService
{
    public async Task<DateTime?> GetPurgeEligibleOnAsync(int tenantId)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant?.DeletedAt is null) return null;
        return tenant.DeletedAt.Value.AddDays(await settings.GetIntAsync(PlatformSettingKeys.RetentionDays, 90));
    }

    public async Task PurgeAsync(int tenantId, string confirmationCode, string? actor = null)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId) ?? throw new InvalidOperationException("Tenant not found.");
        if (tenant.Status != TenantStatus.Terminated || tenant.DeletedAt is null) throw new InvalidOperationException("Only a terminated tenant can be purged.");
        if (tenant.PurgedAt is not null) throw new InvalidOperationException("This tenant was already purged.");
        if (!string.Equals(confirmationCode?.Trim(), tenant.Code, StringComparison.Ordinal)) throw new InvalidOperationException("The confirmation code does not match the tenant code.");
        var eligible = await GetPurgeEligibleOnAsync(tenantId);
        if (eligible is null || eligible > time.GetUtcNow().UtcDateTime)
        {
            throw new InvalidOperationException($"The retention period has not ended. Data can be erased from {eligible:dd MMM yyyy}.");
        }

        using var _ = tenantContext.BypassFilters();
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;

        // Children first, in foreign-key order taken from the model itself.
        foreach (var entity in DeleteOrder())
        {
            var table = entity.GetTableName();
            if (table is null) continue;
            var schema = entity.GetSchema();
            var qualified = schema is null ? $"[{table}]" : $"[{schema}].[{table}]";
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM {qualified} WHERE [TenantId] = {{0}}", tenantId);
        }
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUsers] WHERE [TenantId] = {0}", tenantId);

        tenant.PurgedAt = time.GetUtcNow().UtcDateTime;
        tenant.OwnerUserId = null;
        audit.Add("tenant.purged", nameof(Tenant), tenantId.ToString(), tenantId, details: $"{tenant.Code}: business data and users erased permanently");
        await db.SaveChangesAsync();
        if (tx is not null) await tx.CommitAsync();
    }

    /// <summary>Tenant-owned tables ordered so a table is deleted before the tables it references.</summary>
    internal IReadOnlyList<IEntityType> DeleteOrder()
    {
        var types = db.Model.GetEntityTypes().Where(t => ApplicationDbContext.IsTenantEntity(t.ClrType)).ToList();
        var ordered = new List<IEntityType>();
        var state = new Dictionary<IEntityType, int>();   // 1 = visiting, 2 = done

        void Visit(IEntityType t)
        {
            if (state.TryGetValue(t, out var s))
            {
                if (s == 1) throw new InvalidOperationException($"Circular foreign keys at {t.Name}.");
                return;
            }
            state[t] = 1;
            // Anything that references t must be deleted before t.
            foreach (var referencing in types.Where(o => o != t && o.GetForeignKeys().Any(fk => fk.PrincipalEntityType == t)))
            {
                Visit(referencing);
            }
            state[t] = 2;
            ordered.Add(t);
        }

        foreach (var t in types) Visit(t);
        return ordered;
    }
}
