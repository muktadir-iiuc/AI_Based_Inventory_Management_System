using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.Saas;

/// <summary>Append-only record of SaaS-level actions. Never holds secrets, tokens, OTPs or card data.</summary>
public class SaasAuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? ActorUserId { get; set; }
    [StringLength(256)] public string? ActorName { get; set; }
    [Required, StringLength(80)] public string Action { get; set; } = string.Empty;
    [StringLength(60)] public string? EntityType { get; set; }
    [StringLength(60)] public string? EntityId { get; set; }
    public int? TenantId { get; set; }
    [StringLength(20)] public string Result { get; set; } = "Success";
    [StringLength(1000)] public string? Details { get; set; }
    [StringLength(60)] public string? IpAddress { get; set; }
}

/// <summary>Queued billing notification: shown in the tenant's billing portal (InApp) and/or emailed by the scheduler.</summary>
public class SaasNotification
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
    [Required, StringLength(60)] public string Type { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Subject { get; set; } = string.Empty;
    [StringLength(2000)] public string Body { get; set; } = string.Empty;
    [StringLength(256)] public string? ToEmail { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    [StringLength(500)] public string? LastError { get; set; }

    /// <summary>Makes queuing idempotent: the same event for the same tenant is only stored once.</summary>
    [Required, StringLength(200)] public string DedupeKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>Platform-wide key/value configuration (defaults for trials, grace, invoicing, reminders...).</summary>
public class PlatformSetting
{
    [Key, StringLength(80)] public string Key { get; set; } = string.Empty;
    [StringLength(1000)] public string Value { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    [StringLength(256)] public string? UpdatedBy { get; set; }
}

public static class PlatformSettingKeys
{
    public const string PlatformName = "platform.name";
    public const string PlatformAddress = "platform.address";
    public const string PlatformPhone = "platform.phone";
    /// <summary>Public root URL used to build the links given to tenants, e.g. https://app.example.com</summary>
    public const string PlatformPublicUrl = "platform.public-url";
    public const string DefaultCurrency = "billing.currency";
    public const string DefaultTrialDays = "billing.default-trial-days";
    public const string DefaultGraceDays = "billing.default-grace-days";
    public const string InvoicePrefix = "billing.invoice-prefix";
    public const string InvoiceDueDays = "billing.invoice-due-days";
    public const string RenewalLeadDays = "billing.renewal-lead-days";
    public const string ReminderDays = "billing.reminder-days-before-due";
    public const string DefaultTaxPercent = "billing.default-tax-percent";
    public const string RetentionDays = "tenant.retention-days";
    public const string TermsUrl = "legal.terms-url";
    public const string PrivacyUrl = "legal.privacy-url";
    public const string CancellationPolicy = "legal.cancellation-policy";
    public const string SuspensionPolicy = "billing.suspension-policy";
    public const string ManualPaymentInstructions = "payment.manual-instructions";
    public const string AttemptExpiryMinutes = "payment.attempt-expiry-minutes";
    public const string RegistrationEnabled = "registration.enabled";
    public const string RegistrationPlanCode = "registration.plan-code";

    public static readonly Dictionary<string, string> Defaults = new()
    {
        [PlatformName] = "I'M Cloud",
        [PlatformAddress] = "",
        [PlatformPhone] = "",
        [PlatformPublicUrl] = "",
        [DefaultCurrency] = "BDT",
        [DefaultTrialDays] = "14",
        [DefaultGraceDays] = "7",
        [InvoicePrefix] = "SINV",
        [InvoiceDueDays] = "7",
        [RenewalLeadDays] = "7",
        [ReminderDays] = "7,3,1",
        [DefaultTaxPercent] = "0",
        [RetentionDays] = "90",
        [TermsUrl] = "",
        [PrivacyUrl] = "",
        [CancellationPolicy] = "end-of-term",
        [SuspensionPolicy] = "billing-only",
        [ManualPaymentInstructions] = "Bank transfer: pay to the account details provided by the platform operator, then submit the transfer reference below. Cash: hand over to our representative and ask for a receipt.",
        [AttemptExpiryMinutes] = "120",
        [RegistrationEnabled] = "true",
        [RegistrationPlanCode] = "trial"
    };
}
