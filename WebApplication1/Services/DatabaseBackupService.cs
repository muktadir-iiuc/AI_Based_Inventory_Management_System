using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Common;

namespace WebApplication1.Services;

// Backups are taken by SQL Server itself (BACKUP DATABASE ... TO DISK), so the .bak file is written
// by the SQL Server service to a folder on the SQL Server machine. When that machine is also the
// web server the file can be downloaded/deleted from the app; when it is remote the app only
// records and reports it.
public class DatabaseBackupService(ApplicationDbContext db, ILogger<DatabaseBackupService> logger) : IDatabaseBackupService
{
    // One backup at a time across the whole app (manual and scheduled share this).
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<BackupSetting> GetSettingsAsync()
    {
        return await db.BackupSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync()
            ?? new BackupSetting();
    }

    public async Task SaveSettingsAsync(BackupSetting values, string? updatedBy)
    {
        var setting = await db.BackupSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (setting is null)
        {
            setting = new BackupSetting();
            db.BackupSettings.Add(setting);
        }

        var scheduleChanged = values.IsEnabled != setting.IsEnabled
            || values.Frequency != setting.Frequency
            || values.TimeOfDay != setting.TimeOfDay
            || values.WeeklyDay != setting.WeeklyDay;

        setting.IsEnabled = values.IsEnabled;
        setting.Frequency = values.Frequency;
        setting.TimeOfDay = values.TimeOfDay;
        setting.WeeklyDay = values.WeeklyDay;
        setting.RetentionCount = values.RetentionCount;
        setting.BackupDirectory = string.IsNullOrWhiteSpace(values.BackupDirectory) ? null : values.BackupDirectory.Trim();
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedBy = updatedBy;

        if (scheduleChanged)
        {
            setting.LastScheduledRunAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public async Task<string?> GetEffectiveDirectoryAsync(BackupSetting? setting = null)
    {
        setting ??= await GetSettingsAsync();
        if (!string.IsNullOrWhiteSpace(setting.BackupDirectory))
        {
            return setting.BackupDirectory;
        }

        return await GetInstanceDefaultDirectoryAsync();
    }

    private async Task<string?> GetInstanceDefaultDirectoryAsync()
    {
        try
        {
            var defaults = await db.Database
                .SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512)) AS [Value]")
                .ToListAsync();
            return defaults.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the SQL Server default backup path.");
            return null;
        }
    }

    public async Task<DatabaseBackup> RunBackupAsync(BackupTrigger trigger, string? requestedBy)
    {
        if (!await Gate.WaitAsync(0))
        {
            throw new InvalidOperationException("Another backup is already running. Try again when it finishes.");
        }

        try
        {
            var databaseName = db.Database.GetDbConnection().Database;
            var setting = await GetSettingsAsync();
            var configured = string.IsNullOrWhiteSpace(setting.BackupDirectory) ? null : setting.BackupDirectory.Trim();
            var directory = configured ?? await GetInstanceDefaultDirectoryAsync();

            var fileName = $"{SafeFileName(databaseName)}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            var backup = new DatabaseBackup
            {
                FileName = fileName,
                FilePath = string.IsNullOrWhiteSpace(directory) ? fileName : Path.Combine(directory, fileName),
                Trigger = trigger,
                Status = BackupStatus.Running,
                CreatedBy = requestedBy
            };
            db.DatabaseBackups.Add(backup);
            await db.SaveChangesAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new InvalidOperationException("No backup folder is configured and SQL Server has no default backup folder. Set one in the backup settings.");
                }

                // A large database can take a long while; the default 30 s command timeout would cut it off.
                db.Database.SetCommandTimeout(TimeSpan.FromHours(2));

                var quotedName = "[" + databaseName.Replace("]", "]]") + "]";
                string? copyTarget = null;
                string? note = null;

                try
                {
                    await WriteBackupAsync(quotedName, databaseName, backup.FilePath);
                }
                catch (Exception ex) when (configured is not null && ex.Message.Contains("Cannot open backup device", StringComparison.OrdinalIgnoreCase))
                {
                    // The chosen folder is written by the SQL Server *service account*, which often has no
                    // rights on folders like a user's Desktop. Rather than fail, take the backup into SQL
                    // Server's own (always writable) backup folder and let the app copy it over afterwards.
                    var fallbackDirectory = await GetInstanceDefaultDirectoryAsync();
                    if (string.IsNullOrWhiteSpace(fallbackDirectory))
                    {
                        throw;
                    }

                    logger.LogWarning(ex, "SQL Server could not write to {Folder}; using {Fallback} and copying.", configured, fallbackDirectory);
                    backup.FilePath = Path.Combine(fallbackDirectory, fileName);
                    copyTarget = Path.Combine(configured, fileName);
                    note = $"SQL Server's service account can't write to {configured}, so the backup was saved to {fallbackDirectory}.";
                    await WriteBackupAsync(quotedName, databaseName, backup.FilePath);
                }

                backup.SizeBytes = await GetSizeAsync(backup.FilePath);

                if (copyTarget is not null)
                {
                    try
                    {
                        Directory.CreateDirectory(configured!);
                        File.Copy(backup.FilePath, copyTarget, overwrite: true);
                        backup.CopyPath = copyTarget;
                        note += $" A copy was placed in {configured}.";
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not copy backup to {Folder}.", configured);
                        note += $" Copying it to {configured} also failed ({ex.Message}); the file is in the SQL Server folder.";
                    }
                }

                backup.ErrorMessage = note is null ? null : Truncate(note, 1000);
                backup.Status = BackupStatus.Succeeded;
                backup.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database backup {File} failed.", fileName);
                backup.Status = BackupStatus.Failed;
                backup.ErrorMessage = Truncate(ex.Message, 1000);
                backup.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            if (backup.Status == BackupStatus.Succeeded)
            {
                await ApplyRetentionAsync();
            }

            return backup;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<BackupDeleteResult> DeleteAsync(int id)
    {
        var backup = await db.DatabaseBackups.FindAsync(id);
        if (backup is null)
        {
            return new BackupDeleteResult(false, false, "That backup no longer exists.");
        }

        if (backup.Status == BackupStatus.Running)
        {
            return new BackupDeleteResult(false, false, "That backup is still running.");
        }

        var fileRemoved = false;
        try
        {
            foreach (var path in new[] { backup.FilePath, backup.CopyPath })
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                    fileRemoved = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BackupDeleteResult(false, false, $"The backup file could not be deleted from disk: {ex.Message}");
        }

        db.DatabaseBackups.Remove(backup);
        await db.SaveChangesAsync();
        return new BackupDeleteResult(true, fileRemoved, null);
    }

    public async Task FailInterruptedAsync()
    {
        var stale = await db.DatabaseBackups.Where(b => b.Status == BackupStatus.Running).ToListAsync();
        foreach (var backup in stale)
        {
            backup.Status = BackupStatus.Failed;
            backup.ErrorMessage = "Interrupted: the application stopped while this backup was running.";
            backup.CompletedAt = DateTime.UtcNow;
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    // Keeps the newest RetentionCount successful backups; failures are never pruned (they are the
    // audit trail) and a file that cannot be deleted is left for the next run.
    private async Task ApplyRetentionAsync()
    {
        var setting = await GetSettingsAsync();
        if (setting.RetentionCount <= 0)
        {
            return;
        }

        var expired = await db.DatabaseBackups
            .Where(b => b.Status == BackupStatus.Succeeded)
            .OrderByDescending(b => b.StartedAt).ThenByDescending(b => b.Id)
            .Skip(setting.RetentionCount)
            .Select(b => b.Id)
            .ToListAsync();

        foreach (var id in expired)
        {
            var result = await DeleteAsync(id);
            if (!result.Deleted)
            {
                logger.LogWarning("Backup retention could not remove backup {Id}: {Message}", id, result.Message);
            }
        }
    }

    private async Task WriteBackupAsync(string quotedName, string databaseName, string path)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"BACKUP DATABASE {quotedName} TO DISK = @p0 WITH INIT, CHECKSUM, NAME = @p1",
            new Microsoft.Data.SqlClient.SqlParameter("p0", path),
            new Microsoft.Data.SqlClient.SqlParameter("p1", $"{databaseName} full backup"));

        // Confirms the file just written is readable and its checksums are intact.
        await db.Database.ExecuteSqlRawAsync(
            "RESTORE VERIFYONLY FROM DISK = @p0 WITH CHECKSUM",
            new Microsoft.Data.SqlClient.SqlParameter("p0", path));
    }

    private async Task<long?> GetSizeAsync(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // fall through to msdb
        }

        try
        {
            // The file is on another machine: ask SQL Server how big the backup it just wrote is.
            var sizes = await db.Database.SqlQueryRaw<long>(
                @"SELECT TOP 1 CAST(bs.backup_size AS bigint) AS [Value]
                  FROM msdb.dbo.backupset bs
                  JOIN msdb.dbo.backupmediafamily m ON m.media_set_id = bs.media_set_id
                  WHERE m.physical_device_name = {0}
                  ORDER BY bs.backup_finish_date DESC", path).ToListAsync();
            return sizes.Count > 0 ? sizes[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
