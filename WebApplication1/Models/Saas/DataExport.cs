using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.Saas;

public enum ExportFormat { Csv = 1, Json = 2, Xlsx = 3 }

public enum ExportReason { CancelPlan = 1, MoveToAnotherSystem = 2, Backup = 3, Other = 4 }

public enum ExportStatus { Requested = 1, Ready = 2, Rejected = 3, Failed = 4, Expired = 5 }

/// <summary>A tenant's request for a copy of its own business data (e.g. when it ends its plan or moves to another system),
/// and the file the platform generated for it. Platform-level table: scoped by its explicit TenantId.</summary>
public class DataExportRequest
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public ExportFormat Format { get; set; } = ExportFormat.Csv;
    public ExportReason Reason { get; set; } = ExportReason.Other;
    [StringLength(1000)] public string? Notes { get; set; }
    public ExportStatus Status { get; set; } = ExportStatus.Requested;

    [StringLength(450)] public string? RequestedByUserId { get; set; }
    [StringLength(256)] public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [StringLength(256)] public string? ProcessedBy { get; set; }
    public DateTime? ProcessedAt { get; set; }
    [StringLength(500)] public string? RejectReason { get; set; }

    /// <summary>File name inside App_Data/Exports/{TenantId}/ (never a path).</summary>
    [StringLength(200)] public string? FileName { get; set; }
    public long FileSize { get; set; }
    public int TableCount { get; set; }
    public long RowCount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? DownloadedAt { get; set; }
    [StringLength(500)] public string? Error { get; set; }

    /// <summary>Version of the data agreement in force when the data was handed over.</summary>
    [StringLength(40)] public string? AgreementVersion { get; set; }
}

/// <summary>A tenant's acceptance of one version of the data agreement (kept as evidence; never edited).</summary>
public class DataAgreementAcceptance
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? SubscriptionId { get; set; }
    [Required, StringLength(40)] public string Version { get; set; } = string.Empty;
    /// <summary>Where it was accepted: Registration, ChoosePlan, Portal, Platform.</summary>
    [Required, StringLength(30)] public string Context { get; set; } = string.Empty;
    [StringLength(450)] public string? AcceptedByUserId { get; set; }
    [StringLength(256)] public string? AcceptedBy { get; set; }
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    [StringLength(60)] public string? IpAddress { get; set; }
}
