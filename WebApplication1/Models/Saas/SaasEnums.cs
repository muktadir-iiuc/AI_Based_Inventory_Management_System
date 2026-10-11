namespace WebApplication1.Models.Saas;

public enum PlanStatus { Draft = 0, Active = 1, Disabled = 2, Archived = 3 }

public enum BillingIntervalUnit { Day = 1, Month = 2 }

public enum SubscriptionStatus
{
    Trialing = 1,
    Active = 2,
    PastDue = 3,
    GracePeriod = 4,
    Suspended = 5,
    Disabled = 6,
    Cancelled = 7,
    Expired = 8
}

public enum InvoiceStatus { Draft = 0, Pending = 1, PartiallyPaid = 2, Paid = 3, Overdue = 4, Cancelled = 5, Refunded = 6 }

public enum InvoicePurpose { NewSubscription = 1, Renewal = 2, PlanChange = 3, Manual = 4 }

public enum InvoiceItemType { Subscription = 1, Discount = 2, Tax = 3, Adjustment = 4 }

public enum SaasPaymentStatus { Pending = 0, Succeeded = 1, Failed = 2, Cancelled = 3, Refunded = 4, AwaitingConfirmation = 5 }

public enum NotificationChannel { InApp = 1, Email = 2 }

public enum NotificationStatus { Pending = 0, Sent = 1, Failed = 2 }

/// <summary>What a tenant user may do right now. Computed from tenant + subscription by AccessEvaluator.</summary>
public enum AccessLevel
{
    /// <summary>Everything the plan includes.</summary>
    Full = 0,
    /// <summary>Only billing, account and logout pages (suspended, expired, cancelled, admin hold).</summary>
    BillingOnly = 1,
    /// <summary>No access at all (tenant disabled or terminated).</summary>
    Blocked = 2
}
