using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.SqlServer.Dac;
using WebApplication1.Data;
using WebApplication1.Models.Common;

namespace WebApplication1.Services;

// Backups are taken by SQL Server itself (BACKUP DATABASE ... TO DISK), so the .bak file is written
// by the SQL Server service to a folder on the SQL Server machine. When that machine is also the
// web server the file can be downloaded/deleted from the app; when it is remote the app only
// records and reports it.
//
// Fallback: hosted SQL Servers often refuse BACKUP DATABASE to the app's login (error 262), and
// grant no way to change that. Then the app exports the database itself as a .bacpac (DacFx) to a
// folder on the web server — no SQL Server backup rights needed. Restore it in SSMS with
// "Import Data-tier Application". Unlike a .bak it isn't a point-in-time snapshot: rows written
// while the export runs may or may not be included.
public class DatabaseBackupService(ApplicationDbContext db, ILogger<DatabaseBackupService> logger, IWebHostEnvironment env) : IDatabaseBackupService
{
    // SQL Server error 262: "<permission> permission denied in database '<name>'".
    private const int PermissionDeniedError = 262;

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

            var baseName = $"{SafeFileName(databaseName)}_{DateTime.Now:yyyyMMdd_HHmmss}";
            var fileName = baseName + ".bak";
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
                // Native BACKUP DATABASE first; when SQL Server has nowhere to write one or refuses
                // this login the permission, a .bacpac export; failing that, a .sql restore script.
                string? nativeFailure = string.IsNullOrWhiteSpace(directory)
                    ? "SQL Server has no backup folder available"
                    : null;
                if (nativeFailure is null)
                {
                    try
                    {
                        await TakeNativeBackupAsync(backup, databaseName, configured, fileName);
                    }
                    catch (SqlException ex) when (ex.Number == PermissionDeniedError)
                    {
                        logger.LogWarning(ex, "SQL Server refused BACKUP DATABASE; exporting a .bacpac instead.");
                        nativeFailure = "This SQL Server login isn't allowed to run BACKUP DATABASE";
                    }
                }

                if (nativeFailure is not null)
                {
                    try
                    {
                        await ExportBacpacAsync(backup, databaseName, baseName, configured,
                            $"{nativeFailure}, so the app exported a .bacpac instead.");
                    }
                    catch (Exception ex)
                    {
                        // Typically the login also lacks VIEW DEFINITION, which DacFx needs. A plain
                        // script of schema + data needs only SELECT.
                        logger.LogWarning(ex, "The .bacpac export failed; writing a .sql restore script instead.");
                        var bacpacFailure = Truncate(ex.Message, 300);
                        try
                        {
                            await WriteSqlScriptAsync(backup, baseName, configured,
                                $"{nativeFailure}, and a .bacpac export failed too ({bacpacFailure}), so the app saved a .sql restore script instead.");
                        }
                        catch (Exception scriptEx)
                        {
                            throw new InvalidOperationException(
                                $"{nativeFailure}. The .bacpac export failed ({bacpacFailure}). The .sql script failed too ({scriptEx.Message}).", scriptEx);
                        }
                    }
                }

                backup.Status = BackupStatus.Succeeded;
                backup.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database backup {File} failed.", backup.FileName);
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

    // BACKUP DATABASE to the configured / SQL Server default folder (the caller has checked there is
    // one). Sets the size and any note on the row; the caller marks it succeeded.
    private async Task TakeNativeBackupAsync(DatabaseBackup backup, string databaseName, string? configured, string fileName)
    {
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
    }

    // .bacpac and .sql backups are written by this app (not SQL Server), so they go to a folder the
    // web server can write: the configured backup folder when the app can use it, else
    // App_Data/Backups under the app (not web-served — only wwwroot is).
    private string AppBackupDirectory(string? configured)
    {
        var directory = configured is not null && CanWriteTo(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, "App_Data", "Backups");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private async Task WriteSqlScriptAsync(DatabaseBackup backup, string baseName, string? configured, string reason)
    {
        var directory = AppBackupDirectory(configured);
        backup.FileName = baseName + ".sql";
        backup.FilePath = Path.Combine(directory, backup.FileName);
        backup.CopyPath = null;
        await db.SaveChangesAsync();

        try
        {
            await SqlScriptBackupWriter.WriteAsync(db, backup.FilePath);
        }
        catch
        {
            TryDelete(backup.FilePath);
            throw;
        }

        backup.SizeBytes = new FileInfo(backup.FilePath).Length;
        var note = reason + " Restore it by creating an empty database and running the script against it (SSMS, or sqlcmd -i for a large file).";
        if (configured is not null && directory != configured)
        {
            note += $" The app can't write to {configured}, so it was saved in {directory}.";
        }
        backup.ErrorMessage = Truncate(note, 1000);
    }

    private async Task ExportBacpacAsync(DatabaseBackup backup, string databaseName, string baseName, string? configured, string reason)
    {
        var directory = AppBackupDirectory(configured);

        backup.FileName = baseName + ".bacpac";
        backup.FilePath = Path.Combine(directory, backup.FileName);
        backup.CopyPath = null;
        await db.SaveChangesAsync();

        var services = new DacServices(db.Database.GetConnectionString());
        var messages = new List<string>();
        services.Message += (_, e) =>
        {
            if (e.Message.MessageType != DacMessageType.Message)
            {
                messages.Add(e.Message.ToString());
            }
        };

        // DacFx stages table data in a temp folder — by default the system one, which a hosted app
        // often can't write. Keep it next to the export, in a folder we know is writable.
        var workDirectory = Path.Combine(directory, $".export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        var skippedValidation = false;
        try
        {
            try
            {
                await ExportAsync(verify: true);
            }
            catch (DacServicesException ex)
            {
                // Schema validation is the usual reason an export is refused on a hosted server (e.g. a
                // database user tied to a server login the host manages). The data is still exportable:
                // retry without validating — importing the file validates it again anyway.
                logger.LogWarning(ex, "Validated .bacpac export failed: {Details}; retrying without validation.", Describe(ex, messages));
                messages.Clear();
                TryDelete(backup.FilePath);
                skippedValidation = true;
                await ExportAsync(verify: false);
            }
        }
        catch (Exception ex)
        {
            // Don't leave a half-written export behind for someone to restore from.
            TryDelete(backup.FilePath);
            var details = Describe(ex, messages);
            logger.LogError(ex, ".bacpac export failed: {Details}", details);
            throw new InvalidOperationException(details, ex);
        }
        finally
        {
            try { Directory.Delete(workDirectory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        backup.SizeBytes = new FileInfo(backup.FilePath).Length;
        var note = reason + " Restore it in SQL Server Management Studio with Databases → Import Data-tier Application.";
        if (skippedValidation)
        {
            note += " (DacFx schema validation was skipped to allow the export.)";
        }
        if (configured is not null && directory != configured)
        {
            note += $" The app can't write to {configured}, so it was saved in {directory}.";
        }
        backup.ErrorMessage = Truncate(note, 1000);

        // DacFx is synchronous and can run for minutes on a big database.
        Task ExportAsync(bool verify) => Task.Run(() => services.ExportBacpac(backup.FilePath, databaseName, new DacExportOptions
        {
            VerifyExtraction = verify,
            TempDirectoryForTableData = workDirectory,
            CommandTimeout = 7200
        }, null, null));
    }

    // DacFx's own message ("Could not extract package from specified database.") says nothing; the
    // reasons are in its error/warning messages and inner exceptions.
    private static string Describe(Exception ex, IEnumerable<string> messages)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            parts.Add(e.Message);
            if (e is DacServicesException dac)
            {
                parts.AddRange(dac.Messages.Select(m => m.ToString()));
            }
        }
        parts.AddRange(messages);
        return string.Join(" | ", parts.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct());
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static bool CanWriteTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
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
