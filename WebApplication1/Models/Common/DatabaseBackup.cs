using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.Common;

public enum BackupFrequency
{
    Daily = 1,
    Weekly = 2
}

public enum BackupTrigger
{
    Manual = 1,
    Scheduled = 2
}

public enum BackupStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

// Single-row table with the automatic-backup schedule (same pattern as CompanySetting: only the
// first row is ever used). Times are in the web server's local time zone.
public class BackupSetting
{
    public int Id { get; set; }

    public bool IsEnabled { get; set; }

    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

    public TimeOnly TimeOfDay { get; set; } = new(2, 0);

    // Only used when Frequency is Weekly.
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;

    // How many successful backups to keep; older ones are deleted after each successful backup.
    public int RetentionCount { get; set; } = 14;

    // Folder the SQL Server service writes to (as seen by the SQL Server machine). Null/empty
    // means the instance's default backup folder.
    [StringLength(400)]
    public string? BackupDirectory { get; set; }

    // The schedule baseline (UTC): only occurrences after this run. Reset when the schedule is
    // enabled or changed so switching it on never fires a backup for an occurrence in the past.
    public DateTime? LastScheduledRunAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

// One row per backup attempt (manual or scheduled), successful or not.
public class DatabaseBackup
{
    public int Id { get; set; }

    [Required, StringLength(260)]
    public string FileName { get; set; } = string.Empty;

    // Full path as written by SQL Server; may not be reachable from the web server.
    [Required, StringLength(600)]
    public string FilePath { get; set; } = string.Empty;

    // Set when the app copied the backup to the configured folder because SQL Server itself
    // couldn't write there (see DatabaseBackupService); FilePath is then SQL Server's own folder.
    [StringLength(600)]
    public string? CopyPath { get; set; }

    public long? SizeBytes { get; set; }

    public BackupTrigger Trigger { get; set; }

    public BackupStatus Status { get; set; } = BackupStatus.Running;

    // The failure reason, or a note on a successful backup (e.g. it fell back to another folder).
    [StringLength(1000)]
    public string? ErrorMessage { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // The user who ran it, or "Scheduler" for automatic backups.
    [StringLength(256)]
    public string? CreatedBy { get; set; }
}
