using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services.Saas;

public record LifecycleReport(int TrialsExpired, int RenewalInvoices, int Reminders, int Overdue, int Suspended, int Cancelled, int Errors);

public interface ISubscriptionLifecycleService
{
    /// <summary>
    /// One idempotent pass over every tenant: trial expiry, renewal invoicing, reminders, overdue detection,
    /// grace expiry and automatic suspension. Every step is driven by stored state and the clock, so running it
    /// twice - or after a restart - does the same work only once.
    /// </summary>
    Task<LifecycleReport> RunAsync(CancellationToken cancellationToken = default);
}

public class SubscriptionLifecycleService(
    ApplicationDbContext db,
    ITenantContext tenantContext,
    IInvoiceService invoices,
    ISubscriptionService subscriptions,
    IPlatformSettings settings,
    ISaasNotifier notifier,
    ISaasAudit audit,
    TimeProvider time,
    ILogger<SubscriptionLifecycleService> logger) : ISubscriptionLifecycleService
{
    public async Task<LifecycleReport> RunAsync(CancellationToken ct = default)
    {
        using var _ = tenantContext.BypassFilters();
        var errors = 0;
        int trials = 0, renewals = 0, reminders = 0, overdue = 0, suspended = 0, cancelled = 0;

        async Task Step(string name, Func<Task> work)
        {
            try { await work(); }
            catch (Exception ex)
            {
                errors++;
                logger.LogError(ex, "Subscription lifecycle step '{Step}' failed; it will be retried on the next run.", name);
                db.ChangeTracker.Clear();
            }
        }

        await Step("trial-expiry", async () => trials = await ExpireTrialsAsync(ct));
        await Step("cancel-at-term-end", async () => cancelled = await EndCancelledTermsAsync(ct));
        await Step("renewal-invoices", async () => renewals = await GenerateRenewalInvoicesAsync(ct));
        await Step("reminders", async () => reminders = await QueueRemindersAsync(ct));
        await Step("overdue", async () => overdue = await MarkOverdueAsync(ct));
        await Step("grace-and-suspension", async () => suspended = await ApplyGraceAndSuspensionAsync(ct));

        return new LifecycleReport(trials, renewals, reminders, overdue, suspended, cancelled, errors);
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------ trials

    private async Task<int> ExpireTrialsAsync(CancellationToken ct)
    {
        var now = Now;
        var count = 0;

        // Heads-up 3 days and 1 day before the end.
        var ending = await db.TenantSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt > now && s.TrialEndsAt <= now.AddDays(3))
            .ToListAsync(ct);
        foreach (var s in ending)
        {
            var daysLeft = (int)Math.Ceiling((s.TrialEndsAt!.Value - now).TotalDays);
            var bucket = daysLeft <= 1 ? 1 : 3;
            await notifier.QueueAsync(s.TenantId, "trial-ending", "Your free trial is ending soon",
                $"Your trial ends on {s.TrialEndsAt:dd MMM yyyy}. Choose a plan to keep using the system.", $"trial-ending:{s.Id}:{bucket}d");
        }
        await db.SaveChangesAsync(ct);

        var expired = await db.TenantSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt <= now)
            .ToListAsync(ct);
        foreach (var s in expired)
        {
            s.Status = SubscriptionStatus.Expired;
            s.UpdatedAt = now;
            db.SubscriptionHistories.Add(new SubscriptionHistory
            {
                TenantId = s.TenantId, SubscriptionId = s.Id, Event = "subscription.trial-expired",
                FromStatus = SubscriptionStatus.Trialing, ToStatus = SubscriptionStatus.Expired, PlanId = s.PlanId, Actor = "system", CreatedAt = now
            });
            audit.Add("subscription.trial-expired", nameof(TenantSubscription), s.Id.ToString(), s.TenantId);
            await notifier.QueueAsync(s.TenantId, "trial-expired", "Your free trial has ended",
                "Your trial has ended. Choose a plan and pay to restore access to the system.", $"trial-expired:{s.Id}");
            count++;
        }
        await db.SaveChangesAsync(ct);
        foreach (var s in expired) subscriptions.InvalidateAccess(s.TenantId);
        return count;
    }

    // ------------------------------------------------------------ cancellations at end of term

    private async Task<int> EndCancelledTermsAsync(CancellationToken ct)
    {
        var now = Now;
        var due = await db.TenantSubscriptions
            .Where(s => s.CancelAtPeriodEnd && s.Status != SubscriptionStatus.Cancelled && s.CurrentPeriodEnd <= now)
            .ToListAsync(ct);
        foreach (var s in due)
        {
            var from = s.Status;
            s.Status = SubscriptionStatus.Cancelled;
            s.CancelAtPeriodEnd = false;
            s.UpdatedAt = now;
            db.SubscriptionHistories.Add(new SubscriptionHistory
            {
                TenantId = s.TenantId, SubscriptionId = s.Id, Event = "subscription.term-ended",
                FromStatus = from, ToStatus = SubscriptionStatus.Cancelled, PlanId = s.PlanId, Actor = "system", CreatedAt = now
            });
            audit.Add("subscription.term-ended", nameof(TenantSubscription), s.Id.ToString(), s.TenantId);
            await notifier.QueueAsync(s.TenantId, "subscription-cancelled", "Your subscription has ended",
                "Your subscription ended as requested. Your data is kept; choose a plan to resume.", $"term-ended:{s.Id}");
        }
        await db.SaveChangesAsync(ct);
        foreach (var s in due) subscriptions.InvalidateAccess(s.TenantId);
        return due.Count;
    }

    // ------------------------------------------------------------ renewals

    private async Task<int> GenerateRenewalInvoicesAsync(CancellationToken ct)
    {
        var now = Now;
        var leadDays = await settings.GetIntAsync(PlatformSettingKeys.RenewalLeadDays, 7);

        var candidates = await db.TenantSubscriptions.Include(s => s.Plan)
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.GracePeriod)
                        && s.AutoRenew && !s.CancelAtPeriodEnd && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd <= now.AddDays(leadDays) && s.Price > 0)
            .ToListAsync(ct);

        var created = 0;
        foreach (var sub in candidates)
        {
            var tenantStatus = await db.Tenants.Where(t => t.Id == sub.TenantId).Select(t => t.Status).FirstAsync(ct);
            if (tenantStatus is TenantStatus.Disabled or TenantStatus.Terminated)
            {
                continue;
            }

            // Bill the next term from where the paid term ends, on the pending (downgraded) plan if one was chosen.
            var periodStart = sub.CurrentPeriodEnd!.Value;
            var exists = await db.SaasInvoices.AnyAsync(i => i.SubscriptionId == sub.Id && i.PeriodStart == periodStart && i.Status != InvoiceStatus.Cancelled, ct);
            if (exists)
            {
                continue;
            }

            BillingTerms terms;
            if (sub.PendingPlanId is int pendingId && await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == pendingId, ct) is { } pending)
            {
                terms = BillingTerms.FromPlan(pending);
            }
            else
            {
                terms = BillingTerms.FromSubscription(sub, sub.Plan?.Name ?? "Subscription");
            }

            await db.SaveChangesAsync(ct);
            // Renewal invoices are due when the paid term ends (never in the past if we are already late).
            await invoices.CreateForPeriodAsync(sub, terms, InvoicePurpose.Renewal, periodStart, dueDate: periodStart > now ? periodStart : now, actor: "system");
            created++;
        }
        return created;
    }

    // ------------------------------------------------------------ reminders

    private async Task<int> QueueRemindersAsync(CancellationToken ct)
    {
        var now = Now;
        var days = (await settings.GetAsync(PlatformSettingKeys.ReminderDays))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => int.TryParse(d, out var n) ? n : -1).Where(n => n > 0).OrderBy(n => n).ToList();
        if (days.Count == 0) return 0;

        var open = await db.SaasInvoices
            .Where(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate > now && i.DueDate <= now.AddDays(days.Max()))
            .ToListAsync(ct);

        var count = 0;
        foreach (var invoice in open)
        {
            var remaining = (invoice.DueDate - now).TotalDays;
            // The smallest configured threshold we have already crossed.
            var threshold = days.FirstOrDefault(d => remaining <= d);
            if (threshold == 0) continue;

            var key = $"due-{threshold}d";
            var sent = (invoice.RemindersSent ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (sent.Contains(key)) continue;

            await notifier.QueueAsync(invoice.TenantId, "payment-due",
                $"Invoice {invoice.InvoiceNumber} is due soon",
                $"Invoice {invoice.InvoiceNumber} ({invoice.Balance:0.00} {invoice.Currency}) is due on {invoice.DueDate:dd MMM yyyy}.",
                $"due-reminder:{invoice.Id}:{threshold}");
            invoice.RemindersSent = string.Join(',', sent.Append(key));
            count++;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    // ------------------------------------------------------------ overdue

    private async Task<int> MarkOverdueAsync(CancellationToken ct)
    {
        var now = Now;
        var late = await db.SaasInvoices
            .Where(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate <= now)
            .ToListAsync(ct);

        foreach (var invoice in late)
        {
            invoice.Status = InvoiceStatus.Overdue;
            audit.Add("invoice.overdue", nameof(SaasInvoice), invoice.Id.ToString(), invoice.TenantId, details: invoice.InvoiceNumber);
            await notifier.QueueAsync(invoice.TenantId, "invoice-overdue", $"Invoice {invoice.InvoiceNumber} is overdue",
                $"Invoice {invoice.InvoiceNumber} ({invoice.Balance:0.00} {invoice.Currency}) was due on {invoice.DueDate:dd MMM yyyy}. Pay now to avoid suspension.",
                $"overdue:{invoice.Id}");

            // Only a paying subscription falls behind; trials/expired/cancelled terms are already restricted.
            var sub = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId, ct);
            if (sub is { Status: SubscriptionStatus.Active })
            {
                sub.Status = SubscriptionStatus.PastDue;
                sub.UpdatedAt = now;
                db.SubscriptionHistories.Add(new SubscriptionHistory
                {
                    TenantId = sub.TenantId, SubscriptionId = sub.Id, Event = "subscription.past-due",
                    FromStatus = SubscriptionStatus.Active, ToStatus = SubscriptionStatus.PastDue, PlanId = sub.PlanId,
                    Note = invoice.InvoiceNumber, Actor = "system", CreatedAt = now
                });
            }
            if (sub is { Status: SubscriptionStatus.PastDue or SubscriptionStatus.GracePeriod })
            {
                var graceEnd = invoice.DueDate.AddDays(sub.GraceDays);
                sub.GraceEndsAt = sub.GraceEndsAt is { } existing && existing < graceEnd ? existing : graceEnd;
            }
        }
        await db.SaveChangesAsync(ct);
        foreach (var tenantId in late.Select(i => i.TenantId).Distinct()) subscriptions.InvalidateAccess(tenantId);
        return late.Count;
    }

    // ------------------------------------------------------------ grace period and suspension

    private async Task<int> ApplyGraceAndSuspensionAsync(CancellationToken ct)
    {
        var now = Now;
        var suspendedCount = 0;

        var behind = await db.TenantSubscriptions
            .Where(s => s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.GracePeriod)
            .ToListAsync(ct);

        foreach (var sub in behind)
        {
            if (sub.GraceEndsAt is null)
            {
                continue;
            }

            if (sub.GraceEndsAt > now)
            {
                if (sub.Status == SubscriptionStatus.PastDue && sub.GraceDays > 0)
                {
                    sub.Status = SubscriptionStatus.GracePeriod;
                    sub.UpdatedAt = now;
                    db.SubscriptionHistories.Add(new SubscriptionHistory
                    {
                        TenantId = sub.TenantId, SubscriptionId = sub.Id, Event = "subscription.grace-period",
                        FromStatus = SubscriptionStatus.PastDue, ToStatus = SubscriptionStatus.GracePeriod, PlanId = sub.PlanId,
                        Note = $"until {sub.GraceEndsAt:yyyy-MM-dd}", Actor = "system", CreatedAt = now
                    });
                    await notifier.QueueAsync(sub.TenantId, "grace-period", "Grace period started",
                        $"Your invoice is overdue. Access stays on until {sub.GraceEndsAt:dd MMM yyyy}; pay before then to avoid suspension.", $"grace-start:{sub.Id}:{sub.GraceEndsAt:yyyyMMdd}");
                }
                continue;
            }

            // Grace over and still unpaid: suspend. Data, invoices and history are untouched; billing stays reachable.
            var tenant = await db.Tenants.FirstAsync(t => t.Id == sub.TenantId, ct);
            var from = sub.Status;
            sub.Status = SubscriptionStatus.Suspended;
            sub.SuspendedAt = now;
            sub.UpdatedAt = now;
            tenant.SuspendedForNonPayment = true;
            if (tenant.Status == TenantStatus.Active) tenant.Status = TenantStatus.Suspended;
            tenant.UpdatedAt = now;

            db.SubscriptionHistories.Add(new SubscriptionHistory
            {
                TenantId = sub.TenantId, SubscriptionId = sub.Id, Event = "subscription.suspended",
                FromStatus = from, ToStatus = SubscriptionStatus.Suspended, PlanId = sub.PlanId, Note = "non-payment after grace period", Actor = "system", CreatedAt = now
            });
            audit.Add("subscription.suspended", nameof(TenantSubscription), sub.Id.ToString(), sub.TenantId, details: "non-payment after grace period");
            await notifier.QueueAsync(sub.TenantId, "subscription-suspended", "Your subscription is suspended",
                "Your subscription was suspended because an invoice is still unpaid. Pay the outstanding invoice to restore access. Your data is safe.",
                $"suspended:{sub.Id}:{sub.GraceEndsAt:yyyyMMdd}");
            suspendedCount++;
        }

        await db.SaveChangesAsync(ct);
        foreach (var sub in behind) subscriptions.InvalidateAccess(sub.TenantId);
        return suspendedCount;
    }
}
