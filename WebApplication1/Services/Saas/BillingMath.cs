using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;

namespace WebApplication1.Services.Saas;

/// <summary>Money and date arithmetic for invoices. Server side only, decimals rounded to 2 places away from zero.</summary>
public static class BillingMath
{
    public record Amounts(decimal SubTotal, decimal Discount, decimal Tax, decimal Total);

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Discount comes off the price first; tax is charged on what is left.</summary>
    public static Amounts Compute(decimal price, decimal discountPercent, decimal taxPercent)
    {
        var sub = Round(Math.Max(price, 0));
        var discount = Round(sub * Math.Clamp(discountPercent, 0, 100) / 100m);
        var taxable = sub - discount;
        var tax = Round(taxable * Math.Clamp(taxPercent, 0, 100) / 100m);
        return new Amounts(sub, discount, tax, taxable + tax);
    }

    public static DateTime AddInterval(DateTime start, int count, BillingIntervalUnit unit) =>
        unit == BillingIntervalUnit.Day ? start.AddDays(count) : start.AddMonths(count);

    public static string DescribeInterval(int count, BillingIntervalUnit unit) => (count, unit) switch
    {
        (1, BillingIntervalUnit.Month) => "monthly",
        (12, BillingIntervalUnit.Month) => "yearly",
        (1, BillingIntervalUnit.Day) => "daily",
        (_, BillingIntervalUnit.Month) => $"every {count} months",
        _ => $"every {count} days"
    };
}

/// <summary>What the tenant is billed on: copied from a plan (new or changed plan) or from the running subscription (renewal).</summary>
public record BillingTerms(
    int PlanId, string PlanName, decimal Price, string Currency,
    int IntervalCount, BillingIntervalUnit IntervalUnit, decimal TaxPercent, decimal DiscountPercent, int GraceDays)
{
    public static BillingTerms FromPlan(SubscriptionPlan plan) => new(
        plan.Id, plan.Name, plan.Price, plan.Currency, plan.IntervalCount, plan.IntervalUnit,
        plan.TaxPercent, plan.DiscountPercent, plan.GraceDays);

    public static BillingTerms FromSubscription(TenantSubscription sub, string planName) => new(
        sub.PlanId, planName, sub.Price, sub.Currency, sub.IntervalCount, sub.IntervalUnit,
        sub.TaxPercent, sub.DiscountPercent, sub.GraceDays);
}

public record AccessDecision(AccessLevel Level, string Code, string Message)
{
    public bool IsFull => Level == AccessLevel.Full;
}

/// <summary>
/// Decides what a tenant may do from its current records alone, using the clock directly. Access
/// therefore never depends on a background job having run: if a trial or grace period has ended,
/// protected pages close even before the job updates the stored status.
/// </summary>
public static class AccessEvaluator
{
    public static AccessDecision Evaluate(Tenant? tenant, TenantSubscription? sub, DateTime utcNow)
    {
        if (tenant is null || tenant.DeletedAt is not null || tenant.Status is TenantStatus.Terminated or TenantStatus.Disabled)
        {
            return new(AccessLevel.Blocked, "tenant-disabled", "This business account is disabled. Contact support.");
        }

        if (tenant.IsAdminHold)
        {
            return new(AccessLevel.BillingOnly, "admin-hold",
                "Your account is suspended by the platform administrator" +
                (string.IsNullOrWhiteSpace(tenant.AdminHoldReason) ? "." : $": {tenant.AdminHoldReason}") +
                " Payment will not lift this suspension; contact support.");
        }

        if (sub is null)
        {
            return new(AccessLevel.BillingOnly, "no-subscription", "No subscription is active. Choose a plan to continue.");
        }

        switch (sub.Status)
        {
            case SubscriptionStatus.Disabled:
                return new(AccessLevel.Blocked, "subscription-disabled", "Your subscription is disabled. Contact support.");

            case SubscriptionStatus.Trialing:
                return sub.TrialEndsAt is { } trialEnd && trialEnd <= utcNow
                    ? new(AccessLevel.BillingOnly, "trial-expired", "Your free trial has ended. Choose a plan and pay to continue.")
                    : new(AccessLevel.Full, "trialing", "Free trial.");

            case SubscriptionStatus.Suspended:
                return new(AccessLevel.BillingOnly, "suspended-nonpayment", "Your subscription is suspended because an invoice is unpaid. Pay the outstanding invoice to restore access.");

            case SubscriptionStatus.Expired:
                return new(AccessLevel.BillingOnly, "expired", "Your subscription has expired. Pay the outstanding invoice or choose a plan to continue.");

            case SubscriptionStatus.Cancelled:
                return new(AccessLevel.BillingOnly, "cancelled", "Your subscription is cancelled. Choose a plan to resume.");

            case SubscriptionStatus.PastDue:
            case SubscriptionStatus.GracePeriod:
                return sub.GraceEndsAt is { } graceEnd && graceEnd <= utcNow
                    ? new(AccessLevel.BillingOnly, "grace-ended", "The grace period for your unpaid invoice has ended. Pay to restore access.")
                    : new(AccessLevel.Full, "past-due", "An invoice is overdue. Pay before the grace period ends to avoid suspension.");

            default:
                // Active: a paid period that ran out more than the grace period ago with no renewal paid.
                if (sub.CurrentPeriodEnd is { } end && end.AddDays(sub.GraceDays) <= utcNow)
                {
                    return new(AccessLevel.BillingOnly, "period-ended", "Your subscription period has ended and no renewal payment was received.");
                }
                return new(AccessLevel.Full, "active", "Active.");
        }
    }
}
