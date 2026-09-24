using WebApplication1.Models.Common;

namespace WebApplication1.Services;

public interface IDatabaseBackupService
{
    Task<BackupSetting> GetSettingsAsync();

    // Resets the schedule baseline when the schedule is switched on or changed.
    Task SaveSettingsAsync(BackupSetting values, string? updatedBy);

    // The folder backups go to: the configured one, else the SQL Server instance's default.
    Task<string?> GetEffectiveDirectoryAsync(BackupSetting? setting = null);

    // Runs a backup now and returns its history row. Failures are recorded on the row (Status =
    // Failed) rather than thrown; throws InvalidOperationException only if another backup is
    // already running.
    Task<DatabaseBackup> RunBackupAsync(BackupTrigger trigger, string? requestedBy);

    // Removes the history row and, when the web server can reach it, the file. Returns a
    // message if the file could not be removed (the row is then kept).
    Task<BackupDeleteResult> DeleteAsync(int id);

    // Marks backups left "Running" by a crashed/restarted app as failed.
    Task FailInterruptedAsync();
}

public record BackupDeleteResult(bool Deleted, bool FileRemoved, string? Message);

public static class BackupSchedule
{
    // The most recent scheduled moment at or before now (local time).
    public static DateTime LatestOccurrence(BackupSetting s, DateTime nowLocal)
    {
        var time = s.TimeOfDay.ToTimeSpan();
        if (s.Frequency == BackupFrequency.Daily)
        {
            var candidate = nowLocal.Date + time;
            return candidate > nowLocal ? candidate.AddDays(-1) : candidate;
        }

        var daysBack = ((int)nowLocal.DayOfWeek - (int)s.WeeklyDay + 7) % 7;
        var weekly = nowLocal.Date.AddDays(-daysBack) + time;
        return weekly > nowLocal ? weekly.AddDays(-7) : weekly;
    }

    // The next scheduled moment after now (local time).
    public static DateTime NextOccurrence(BackupSetting s, DateTime nowLocal)
    {
        var latest = LatestOccurrence(s, nowLocal);
        return latest.AddDays(s.Frequency == BackupFrequency.Daily ? 1 : 7);
    }
}
