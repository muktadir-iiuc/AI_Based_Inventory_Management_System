using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models.Saas;

namespace WebApplication1.Services.Saas;

public record BillingNotice(string Level, string Message);

public interface IBillingNoticeService
{
    /// <summary>The single most important message to show a tenant on every page (trial ending, overdue invoice...), or null.</summary>
    Task<BillingNotice?> GetAsync(int tenantId);
}

public class BillingNoticeService(ApplicationDbContext db, ISubscriptionService subscriptions, IMemoryCache cache, TimeProvider time) : IBillingNoticeService
{
    public async Task<BillingNotice?> GetAsync(int tenantId)
    {
        if (tenantId <= 0) return null;
        var key = $"saas-notice:{tenantId}";
        if (cache.TryGetValue(key, out BillingNotice? cached)) return cached;

        var notice = await BuildAsync(tenantId);
        cache.Set(key, notice, TimeSpan.FromSeconds(60));
        return notice;
    }

    private async Task<BillingNotice?> BuildAsync(int tenantId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var access = await subscriptions.GetAccessAsync(tenantId);
        if (!access.IsFull)
        {
            return new BillingNotice("danger", access.Message);
        }

        var sub = await db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId);
        if (sub is null) return null;

        if (sub.Status == SubscriptionStatus.Trialing && sub.TrialEndsAt is { } end)
        {
            var days = Math.Max(0, (int)Math.Ceiling((end - now).TotalDays));
            return new BillingNotice(days <= 3 ? "warning" : "info", $"Free trial: {days} day{(days == 1 ? "" : "s")} left. Choose a plan to keep your access.");
        }

        var overdue = await db.SaasInvoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && (i.Status == InvoiceStatus.Overdue))
            .OrderBy(i => i.DueDate).FirstOrDefaultAsync();
        if (overdue is not null)
        {
            var until = sub.GraceEndsAt is { } g ? $" Access will be suspended on {g:dd MMM yyyy} if it stays unpaid." : string.Empty;
            return new BillingNotice("warning", $"Invoice {overdue.InvoiceNumber} is overdue ({overdue.Total - overdue.AmountPaid:0.00} {overdue.Currency}).{until}");
        }

        var soon = await db.SaasInvoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate <= now.AddDays(7))
            .OrderBy(i => i.DueDate).FirstOrDefaultAsync();
        if (soon is not null)
        {
            return new BillingNotice("info", $"Invoice {soon.InvoiceNumber} ({soon.Total - soon.AmountPaid:0.00} {soon.Currency}) is due on {soon.DueDate:dd MMM yyyy}.");
        }

        if (sub.CancelAtPeriodEnd && sub.CurrentPeriodEnd is { } term)
        {
            return new BillingNotice("info", $"Your subscription ends on {term:dd MMM yyyy} and will not renew.");
        }
        return null;
    }
}
