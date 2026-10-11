using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;

namespace WebApplication1.Services.Saas;

public enum StartMode
{
    /// <summary>Free trial first (needs a plan with TrialDays), invoice when it ends.</summary>
    Trial,
    /// <summary>No access until the first invoice is paid.</summary>
    InvoiceFirst,
    /// <summary>Active immediately with no invoice (comped / negotiated contract / existing business).</summary>
    ActivateNow
}

public record PlanLimits(int? MaxUsers, int? MaxProducts, int? MaxWarehouses, int? MaxStorageMb);

public interface ISubscriptionService
{
    Task<TenantSubscription> StartAsync(int tenantId, int planId, StartMode mode, string? actor = null);
    Task<AccessDecision> GetAccessAsync(int tenantId);
    void InvalidateAccess(int tenantId);
    Task<IReadOnlySet<string>> GetFeaturesAsync(int tenantId);
    Task<PlanLimits> GetLimitsAsync(int tenantId);

    /// <summary>Administrative suspension (fraud, abuse, security). Payments do NOT lift it.</summary>
    Task SuspendAsync(int tenantId, string reason, string? actor = null);
    /// <summary>Clears the administrative hold. A suspension for non-payment stays until the invoice is paid.</summary>
    Task ReactivateAsync(int tenantId, string? actor = null);
    Task DisableAsync(int tenantId, string reason, string? actor = null);
    Task EnableAsync(int tenantId, string? actor = null);

    /// <summary>Stops renewals. Immediate cancellation ends access to the product now; otherwise it runs to the end of the paid term.</summary>
    Task CancelAsync(int tenantId, bool immediate, string? actor = null);
    Task ResumeAsync(int tenantId, string? actor = null);

    /// <summary>
    /// Platform admin: switch plan right away without invoicing. Tenant: <see cref="RequestPlanChangeAsync"/>.
    /// </summary>
    Task AssignPlanNowAsync(int tenantId, int planId, string? actor = null);

    /// <summary>
    /// Tenant-initiated change. An upgrade (or any change from trial/expired) creates an invoice and takes
    /// effect when it is paid. A downgrade is scheduled for the next renewal. Returns the invoice, if any.
    /// </summary>
    Task<SaasInvoice?> RequestPlanChangeAsync(int tenantId, int planId, string? actor = null);

    // ---- platform administration of one tenant's terms
    Task SetLimitOverridesAsync(int tenantId, int? users, int? products, int? warehouses, int? storageMb, string? actor = null);
    /// <summary>enabled = true/false forces the feature on/off for this tenant; null removes the override (back to the plan).</summary>
    Task SetFeatureOverrideAsync(int tenantId, string featureKey, bool? enabled, string? actor = null);
    /// <summary>Negotiated / enterprise terms for this tenant only; the plan itself is untouched.</summary>
    Task SetCustomTermsAsync(int tenantId, decimal price, int intervalCount, BillingIntervalUnit unit, int graceDays, decimal taxPercent, decimal discountPercent, bool autoRenew, string? actor = null);
    /// <summary>Sets when the free trial ends. Re-opens a lapsed trial that was never paid.</summary>
    Task SetTrialEndAsync(int tenantId, DateTime trialEndsUtc, string? actor = null);
    /// <summary>Soft-deletes the tenant: access ends, data is kept until the retention period passes (see TenantRetentionService).</summary>
    Task TerminateAsync(int tenantId, string reason, string? actor = null);
}

public class SubscriptionService(
    ApplicationDbContext db,
    IInvoiceService invoices,
    IPlatformSettings settings,
    ISaasNotifier notifier,
    ISaasAudit audit,
    IMemoryCache cache,
    TimeProvider time) : ISubscriptionService
{
    private static string AccessKey(int tenantId) => $"saas-access:{tenantId}";
    private static TimeSpan AccessTtl => TimeSpan.FromSeconds(20);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public void InvalidateAccess(int tenantId)
    {
        cache.Remove(AccessKey(tenantId));
        cache.Remove(FeatureKey(tenantId));
    }

    private static string FeatureKey(int tenantId) => $"saas-features:{tenantId}";

    public async Task<AccessDecision> GetAccessAsync(int tenantId)
    {
        if (cache.TryGetValue(AccessKey(tenantId), out (AccessDecision Decision, DateTime Expires)? hit) && hit is { } h)
        {
            // Re-evaluate cheaply against the clock: a cached "Full" must not outlive a trial/grace deadline.
            if (h.Decision.IsFull && h.Expires > Now) return h.Decision;
            if (!h.Decision.IsFull) return h.Decision;
        }

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        var sub = await db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId);
        var decision = AccessEvaluator.Evaluate(tenant, sub, Now);

        // A Full decision is only trusted until the next deadline that could flip it.
        var deadline = new[] { sub?.TrialEndsAt, sub?.GraceEndsAt, sub?.CurrentPeriodEnd?.AddDays(sub.GraceDays) }
            .Where(d => d is not null && d > Now).Select(d => d!.Value).DefaultIfEmpty(DateTime.MaxValue).Min();
        var expires = new[] { Now.Add(AccessTtl), deadline }.Min();
        cache.Set(AccessKey(tenantId), (decision, expires), AccessTtl);
        return decision;
    }

    public async Task<IReadOnlySet<string>> GetFeaturesAsync(int tenantId)
    {
        if (cache.TryGetValue(FeatureKey(tenantId), out HashSet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var sub = await db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId);
        var features = sub is null
            ? new HashSet<string>()
            : (await db.PlanFeatures.AsNoTracking().Where(f => f.PlanId == sub.PlanId).Select(f => f.FeatureKey).ToListAsync()).ToHashSet();

        foreach (var o in await db.TenantFeatureOverrides.AsNoTracking().Where(o => o.TenantId == tenantId).ToListAsync())
        {
            if (o.IsEnabled) features.Add(o.FeatureKey); else features.Remove(o.FeatureKey);
        }
        cache.Set(FeatureKey(tenantId), features, AccessTtl);
        return features;
    }

    public async Task<PlanLimits> GetLimitsAsync(int tenantId)
    {
        var sub = await db.TenantSubscriptions.AsNoTracking().Include(s => s.Plan).FirstOrDefaultAsync(s => s.TenantId == tenantId);
        if (sub?.Plan is null)
        {
            return new PlanLimits(0, 0, 0, 0);
        }
        return new PlanLimits(
            sub.MaxUsersOverride ?? sub.Plan.MaxUsers,
            sub.MaxProductsOverride ?? sub.Plan.MaxProducts,
            sub.MaxWarehousesOverride ?? sub.Plan.MaxWarehouses,
            sub.MaxStorageMbOverride ?? sub.Plan.MaxStorageMb);
    }

    public async Task<TenantSubscription> StartAsync(int tenantId, int planId, StartMode mode, string? actor = null)
    {
        if (await db.TenantSubscriptions.AnyAsync(s => s.TenantId == tenantId))
        {
            throw new InvalidOperationException("Tenant already has a subscription.");
        }

        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId)
            ?? throw new InvalidOperationException("Plan not found.");
        var now = Now;
        var sub = new TenantSubscription
        {
            TenantId = tenantId, PlanId = plan.Id, StartedAt = now, CreatedAt = now,
            AutoRenew = plan.AutoRenew
        };
        CopyTerms(sub, plan);

        switch (mode)
        {
            case StartMode.Trial when plan.TrialDays > 0:
                sub.Status = SubscriptionStatus.Trialing;
                sub.TrialEndsAt = now.AddDays(plan.TrialDays);
                break;
            case StartMode.ActivateNow:
                sub.Status = SubscriptionStatus.Active;
                if (plan.Price > 0)
                {
                    sub.CurrentPeriodStart = now;
                    sub.CurrentPeriodEnd = BillingMath.AddInterval(now, plan.IntervalCount, plan.IntervalUnit);
                }
                else
                {
                    sub.AutoRenew = false; // nothing to bill
                }
                break;
            default:
                sub.Status = SubscriptionStatus.Expired; // no access until the first invoice is paid
                break;
        }

        db.TenantSubscriptions.Add(sub);
        await db.SaveChangesAsync();

        AddHistory(sub, "subscription.started", null, sub.Status, plan.Id, $"{mode} on plan {plan.Code}", actor);
        audit.Add("subscription.started", nameof(TenantSubscription), sub.Id.ToString(), tenantId, details: $"plan {plan.Code}, {mode}, status {sub.Status}");

        if (sub.Status == SubscriptionStatus.Trialing)
        {
            await notifier.QueueAsync(tenantId, "trial-started", "Your free trial has started",
                $"Your {plan.Name} trial runs until {sub.TrialEndsAt:dd MMM yyyy}.", $"trial-started:{sub.Id}");
        }
        await db.SaveChangesAsync();

        if (mode == StartMode.InvoiceFirst && plan.Price > 0)
        {
            await invoices.CreateForPeriodAsync(sub, BillingTerms.FromPlan(plan), InvoicePurpose.NewSubscription, now, actor: actor);
        }

        InvalidateAccess(tenantId);
        return sub;
    }

    public async Task SuspendAsync(int tenantId, string reason, string? actor = null)
    {
        var tenant = await LoadTenantAsync(tenantId);
        tenant.IsAdminHold = true;
        tenant.AdminHoldReason = reason;
        if (tenant.Status == TenantStatus.Active) tenant.Status = TenantStatus.Suspended;
        tenant.UpdatedAt = Now;
        audit.Add("tenant.suspended", nameof(Tenant), tenantId.ToString(), tenantId, details: reason);
        await notifier.QueueAsync(tenantId, "tenant-suspended", "Your account has been suspended",
            "Your account was suspended by the platform administrator. Contact support for details.", $"tenant-suspended:{tenantId}:{Now:yyyyMMddHHmm}");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task ReactivateAsync(int tenantId, string? actor = null)
    {
        var tenant = await LoadTenantAsync(tenantId);
        tenant.IsAdminHold = false;
        tenant.AdminHoldReason = null;
        // A tenant that is also behind on payment stays suspended until it pays.
        if (tenant.Status == TenantStatus.Suspended && !tenant.SuspendedForNonPayment)
        {
            tenant.Status = TenantStatus.Active;
        }
        tenant.UpdatedAt = Now;
        audit.Add("tenant.reactivated", nameof(Tenant), tenantId.ToString(), tenantId,
            details: tenant.SuspendedForNonPayment ? "hold cleared; still suspended for non-payment" : null);
        await notifier.QueueAsync(tenantId, "tenant-reactivated", "Your account suspension was lifted",
            tenant.SuspendedForNonPayment ? "The administrative hold was lifted. An unpaid invoice still restricts access until it is paid."
                                           : "Your account is active again.", $"tenant-reactivated:{tenantId}:{Now:yyyyMMddHHmm}");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task DisableAsync(int tenantId, string reason, string? actor = null)
    {
        var tenant = await LoadTenantAsync(tenantId);
        tenant.Status = TenantStatus.Disabled;
        tenant.UpdatedAt = Now;
        audit.Add("tenant.disabled", nameof(Tenant), tenantId.ToString(), tenantId, details: reason);
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task EnableAsync(int tenantId, string? actor = null)
    {
        var tenant = await LoadTenantAsync(tenantId);
        if (tenant.Status == TenantStatus.Terminated || tenant.DeletedAt is not null)
        {
            throw new InvalidOperationException("A terminated tenant cannot be enabled.");
        }
        tenant.Status = tenant.IsAdminHold || tenant.SuspendedForNonPayment ? TenantStatus.Suspended : TenantStatus.Active;
        tenant.UpdatedAt = Now;
        audit.Add("tenant.enabled", nameof(Tenant), tenantId.ToString(), tenantId);
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task CancelAsync(int tenantId, bool immediate, string? actor = null)
    {
        var sub = await LoadSubscriptionAsync(tenantId);
        var from = sub.Status;
        var now = Now;
        sub.CancelledAt = now;
        sub.AutoRenew = false;

        if (immediate || sub.CurrentPeriodEnd is null || sub.CurrentPeriodEnd <= now || sub.Status == SubscriptionStatus.Trialing)
        {
            sub.Status = SubscriptionStatus.Cancelled;
            sub.CancelAtPeriodEnd = false;
        }
        else
        {
            sub.CancelAtPeriodEnd = true; // stays Active until the paid term ends; the lifecycle job then cancels it
        }
        sub.UpdatedAt = now;

        await CancelOpenRenewalInvoicesAsync(sub);
        AddHistory(sub, "subscription.cancelled", from, sub.Status, sub.PlanId, immediate ? "immediate" : "end of term", actor);
        audit.Add("subscription.cancelled", nameof(TenantSubscription), sub.Id.ToString(), tenantId, details: immediate ? "immediate" : "end of term");
        await notifier.QueueAsync(tenantId, "subscription-cancelled", "Subscription cancelled",
            sub.CancelAtPeriodEnd ? $"Your subscription will end on {sub.CurrentPeriodEnd:dd MMM yyyy} and will not renew."
                                  : "Your subscription has been cancelled.", $"subscription-cancelled:{sub.Id}:{now:yyyyMMddHHmm}");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task ResumeAsync(int tenantId, string? actor = null)
    {
        var sub = await LoadSubscriptionAsync(tenantId);
        if (sub.Status == SubscriptionStatus.Cancelled)
        {
            throw new InvalidOperationException("The subscription already ended. Choose a plan to start a new term.");
        }
        sub.CancelAtPeriodEnd = false;
        sub.CancelledAt = null;
        sub.AutoRenew = true;
        sub.UpdatedAt = Now;
        AddHistory(sub, "subscription.resumed", sub.Status, sub.Status, sub.PlanId, null, actor);
        audit.Add("subscription.resumed", nameof(TenantSubscription), sub.Id.ToString(), tenantId);
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task AssignPlanNowAsync(int tenantId, int planId, string? actor = null)
    {
        var sub = await LoadSubscriptionAsync(tenantId);
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId) ?? throw new InvalidOperationException("Plan not found.");
        var from = sub.PlanId;
        sub.PlanId = plan.Id;
        CopyTerms(sub, plan);
        sub.AutoRenew = plan.AutoRenew;
        sub.PendingPlanId = null;
        sub.UpdatedAt = Now;
        AddHistory(sub, "subscription.plan-assigned", sub.Status, sub.Status, plan.Id, $"plan {from} -> {plan.Id} by platform admin", actor);
        audit.Add("subscription.plan-assigned", nameof(TenantSubscription), sub.Id.ToString(), tenantId, details: $"plan {plan.Code}");
        await notifier.QueueAsync(tenantId, "plan-changed", "Your plan was changed", $"Your plan is now {plan.Name}.", $"plan-changed:{sub.Id}:{plan.Id}:{Now:yyyyMMddHHmm}");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task<SaasInvoice?> RequestPlanChangeAsync(int tenantId, int planId, string? actor = null)
    {
        var sub = await LoadSubscriptionAsync(tenantId);
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId) ?? throw new InvalidOperationException("Plan not found.");
        var now = Now;
        if (!plan.IsPublic || !plan.IsAvailable(now))
        {
            throw new InvalidOperationException("That plan is not available.");
        }
        if (plan.Id == sub.PlanId && sub.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.GracePeriod)
        {
            throw new InvalidOperationException("You are already on this plan.");
        }

        var currentlyPaid = sub.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.GracePeriod && sub.CurrentPeriodEnd > now;
        var isDowngrade = currentlyPaid && plan.Price < sub.Price;

        if (isDowngrade)
        {
            sub.PendingPlanId = plan.Id;
            sub.UpdatedAt = now;
            AddHistory(sub, "subscription.downgrade-scheduled", sub.Status, sub.Status, plan.Id, $"effective {sub.CurrentPeriodEnd:yyyy-MM-dd}", actor);
            audit.Add("subscription.downgrade-scheduled", nameof(TenantSubscription), sub.Id.ToString(), tenantId, details: $"to {plan.Code}");
            await notifier.QueueAsync(tenantId, "plan-changed", "Plan change scheduled",
                $"You will move to {plan.Name} on {sub.CurrentPeriodEnd:dd MMM yyyy}.", $"plan-scheduled:{sub.Id}:{plan.Id}:{now:yyyyMMddHHmm}");
            await db.SaveChangesAsync();
            return null;
        }

        // Upgrade, or starting/resuming a paid term: invoice now, effective when paid. No proration of the unused remainder.
        if (plan.Price <= 0)
        {
            throw new InvalidOperationException("Free plans are assigned by the platform administrator.");
        }

        sub.PendingPlanId = null;
        // Cancelled/expired subscriptions that restart need a fresh invoice for a period starting now.
        var purpose = sub.Status is SubscriptionStatus.Trialing or SubscriptionStatus.Expired or SubscriptionStatus.Cancelled
            ? InvoicePurpose.NewSubscription : InvoicePurpose.PlanChange;
        // Reuse an unpaid invoice for the same plan instead of stacking new ones.
        var open = await db.SaasInvoices.Include(i => i.Items).FirstOrDefaultAsync(i =>
            i.SubscriptionId == sub.Id && i.PlanId == plan.Id && i.Purpose != InvoicePurpose.Renewal &&
            i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Refunded);
        if (open is not null)
        {
            return open;
        }

        await db.SaveChangesAsync();
        var invoice = await invoices.CreateForPeriodAsync(sub, BillingTerms.FromPlan(plan), purpose, now, actor: actor);
        AddHistory(sub, "subscription.plan-change-requested", sub.Status, sub.Status, plan.Id, $"invoice {invoice.InvoiceNumber}", actor);
        audit.Add("subscription.plan-change-requested", nameof(TenantSubscription), sub.Id.ToString(), tenantId, details: $"to {plan.Code}, invoice {invoice.InvoiceNumber}");
        await db.SaveChangesAsync();
        return invoice;
    }

    public async Task SetLimitOverridesAsync(int tenantId, int? users, int? products, int? warehouses, int? storageMb, string? actor = null)
    {
        if (new[] { users, products, warehouses, storageMb }.Any(v => v is < 0))
        {
            throw new InvalidOperationException("Limits cannot be negative.");
        }
        var sub = await LoadSubscriptionAsync(tenantId);
        sub.MaxUsersOverride = users;
        sub.MaxProductsOverride = products;
        sub.MaxWarehousesOverride = warehouses;
        sub.MaxStorageMbOverride = storageMb;
        sub.UpdatedAt = Now;
        audit.Add("subscription.limits-overridden", nameof(TenantSubscription), sub.Id.ToString(), tenantId,
            details: $"users {users?.ToString() ?? "plan"}, products {products?.ToString() ?? "plan"}, warehouses {warehouses?.ToString() ?? "plan"}, storage {storageMb?.ToString() ?? "plan"}");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task SetFeatureOverrideAsync(int tenantId, string featureKey, bool? enabled, string? actor = null)
    {
        if (!Features.IsKnown(featureKey)) throw new InvalidOperationException("Unknown feature.");
        await LoadSubscriptionAsync(tenantId);
        var row = await db.TenantFeatureOverrides.FirstOrDefaultAsync(o => o.TenantId == tenantId && o.FeatureKey == featureKey);
        if (enabled is null)
        {
            if (row is not null) db.TenantFeatureOverrides.Remove(row);
        }
        else if (row is null)
        {
            db.TenantFeatureOverrides.Add(new TenantFeatureOverride { TenantId = tenantId, FeatureKey = featureKey, IsEnabled = enabled.Value });
        }
        else
        {
            row.IsEnabled = enabled.Value;
        }
        audit.Add("subscription.feature-overridden", nameof(TenantSubscription), null, tenantId, details: $"{featureKey} = {(enabled is null ? "plan default" : enabled.ToString())}");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task SetCustomTermsAsync(int tenantId, decimal price, int intervalCount, BillingIntervalUnit unit, int graceDays, decimal taxPercent, decimal discountPercent, bool autoRenew, string? actor = null)
    {
        if (price < 0 || intervalCount < 1 || graceDays < 0 || taxPercent is < 0 or > 100 || discountPercent is < 0 or > 100)
        {
            throw new InvalidOperationException("Invalid billing terms.");
        }
        var sub = await LoadSubscriptionAsync(tenantId);
        var before = $"{sub.Price:0.00} every {sub.IntervalCount} {sub.IntervalUnit}";
        sub.Price = BillingMath.Round(price);
        sub.IntervalCount = intervalCount;
        sub.IntervalUnit = unit;
        sub.GraceDays = graceDays;
        sub.TaxPercent = taxPercent;
        sub.DiscountPercent = discountPercent;
        sub.AutoRenew = autoRenew;
        sub.UpdatedAt = Now;
        AddHistory(sub, "subscription.terms-changed", sub.Status, sub.Status, sub.PlanId, $"{before} -> {sub.Price:0.00} every {intervalCount} {unit}", actor);
        audit.Add("subscription.terms-changed", nameof(TenantSubscription), sub.Id.ToString(), tenantId, details: $"{before} -> {sub.Price:0.00} {sub.Currency} every {intervalCount} {unit}, grace {graceDays}d, tax {taxPercent}%, discount {discountPercent}%");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task SetTrialEndAsync(int tenantId, DateTime trialEndsUtc, string? actor = null)
    {
        var sub = await LoadSubscriptionAsync(tenantId);
        if (sub.CurrentPeriodEnd is not null)
        {
            throw new InvalidOperationException("This subscription already has a paid term; a trial can only be set before the first payment.");
        }
        var from = sub.Status;
        sub.TrialEndsAt = DateTime.SpecifyKind(trialEndsUtc, DateTimeKind.Utc);
        if (sub.TrialEndsAt > Now && sub.Status is SubscriptionStatus.Expired or SubscriptionStatus.Trialing)
        {
            sub.Status = SubscriptionStatus.Trialing;
        }
        else if (sub.TrialEndsAt <= Now && sub.Status == SubscriptionStatus.Trialing)
        {
            sub.Status = SubscriptionStatus.Expired;
        }
        sub.UpdatedAt = Now;
        AddHistory(sub, "subscription.trial-changed", from, sub.Status, sub.PlanId, $"trial ends {sub.TrialEndsAt:yyyy-MM-dd HH:mm}", actor);
        audit.Add("subscription.trial-changed", nameof(TenantSubscription), sub.Id.ToString(), tenantId, details: $"trial ends {sub.TrialEndsAt:yyyy-MM-dd HH:mm} UTC");
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    public async Task TerminateAsync(int tenantId, string reason, string? actor = null)
    {
        var tenant = await LoadTenantAsync(tenantId);
        if (tenant.Status == TenantStatus.Terminated) return;
        var now = Now;
        tenant.Status = TenantStatus.Terminated;
        tenant.DeletedAt = now;
        tenant.UpdatedAt = now;
        var sub = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId);
        if (sub is not null)
        {
            var from = sub.Status;
            sub.Status = SubscriptionStatus.Cancelled;
            sub.AutoRenew = false;
            sub.CancelAtPeriodEnd = false;
            sub.CancelledAt = now;
            AddHistory(sub, "tenant.terminated", from, sub.Status, sub.PlanId, reason, actor);
            await CancelOpenRenewalInvoicesAsync(sub);
        }
        audit.Add("tenant.terminated", nameof(Tenant), tenantId.ToString(), tenantId, details: reason);
        await db.SaveChangesAsync();
        InvalidateAccess(tenantId);
    }

    // ------------------------------------------------------------------ helpers

    internal static void CopyTerms(TenantSubscription sub, SubscriptionPlan plan)
    {
        sub.Price = plan.Price;
        sub.Currency = plan.Currency;
        sub.IntervalCount = plan.IntervalCount;
        sub.IntervalUnit = plan.IntervalUnit;
        sub.GraceDays = plan.GraceDays;
        sub.TaxPercent = plan.TaxPercent;
        sub.DiscountPercent = plan.DiscountPercent;
    }

    private void AddHistory(TenantSubscription sub, string ev, SubscriptionStatus? from, SubscriptionStatus? to, int? planId, string? note, string? actor) =>
        db.SubscriptionHistories.Add(new SubscriptionHistory
        {
            TenantId = sub.TenantId, SubscriptionId = sub.Id == 0 ? null : sub.Id, Event = ev, FromStatus = from, ToStatus = to,
            PlanId = planId, Note = note, Actor = actor ?? audit.Actor, CreatedAt = Now
        });

    private async Task<Tenant> LoadTenantAsync(int tenantId) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId) ?? throw new InvalidOperationException("Tenant not found.");

    private async Task<TenantSubscription> LoadSubscriptionAsync(int tenantId) =>
        await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId) ?? throw new InvalidOperationException("Tenant has no subscription.");

    private async Task CancelOpenRenewalInvoicesAsync(TenantSubscription sub)
    {
        var open = await db.SaasInvoices.Where(i => i.SubscriptionId == sub.Id && i.Purpose == InvoicePurpose.Renewal
            && i.AmountPaid == 0 && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue)).ToListAsync();
        foreach (var invoice in open)
        {
            invoice.Status = InvoiceStatus.Cancelled;
            invoice.CancelledAt = Now;
        }
    }
}
