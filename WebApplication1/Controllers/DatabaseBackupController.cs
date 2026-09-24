using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

// A database backup contains everything (users, password hashes, every transaction), so the whole
// controller is limited to Admin and Manager.
[Authorize(Roles = Roles.AdminManagers)]
public class DatabaseBackupController(ApplicationDbContext db, IDatabaseBackupService backups) : Controller
{
    public async Task<IActionResult> Index()
    {
        var setting = await backups.GetSettingsAsync();
        var list = await db.DatabaseBackups.AsNoTracking()
            .OrderByDescending(b => b.StartedAt).ThenByDescending(b => b.Id)
            .Take(200).ToListAsync();

        return View(await BuildViewModelAsync(setting, list, new BackupSettingsViewModel
        {
            IsEnabled = setting.IsEnabled,
            Frequency = setting.Frequency,
            TimeOfDay = setting.TimeOfDay,
            WeeklyDay = setting.WeeklyDay,
            RetentionCount = setting.RetentionCount,
            BackupDirectory = setting.BackupDirectory
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings([Bind(Prefix = nameof(BackupIndexViewModel.Settings))] BackupSettingsViewModel model)
    {
        var directory = model.BackupDirectory?.Trim();
        if (!string.IsNullOrEmpty(directory))
        {
            if (!Path.IsPathRooted(directory) || directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || directory.Contains('*') || directory.Contains('?'))
            {
                ModelState.AddModelError($"{nameof(BackupIndexViewModel.Settings)}.{nameof(model.BackupDirectory)}", "Enter a full folder path such as D:\\Backups.");
            }
        }

        if (!ModelState.IsValid)
        {
            var setting = await backups.GetSettingsAsync();
            var list = await db.DatabaseBackups.AsNoTracking()
                .OrderByDescending(b => b.StartedAt).ThenByDescending(b => b.Id).Take(200).ToListAsync();
            return View(nameof(Index), await BuildViewModelAsync(setting, list, model));
        }

        await backups.SaveSettingsAsync(new BackupSetting
        {
            IsEnabled = model.IsEnabled,
            Frequency = model.Frequency,
            TimeOfDay = model.TimeOfDay,
            WeeklyDay = model.WeeklyDay,
            RetentionCount = model.RetentionCount,
            BackupDirectory = directory
        }, User.Identity?.Name);

        TempData["Success"] = model.IsEnabled ? "Backup schedule saved." : "Backup settings saved. Automatic backups are off.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow()
    {
        try
        {
            var result = await backups.RunBackupAsync(BackupTrigger.Manual, User.Identity?.Name);
            if (result.Status == BackupStatus.Succeeded)
            {
                TempData["Success"] = $"Backup created: {result.FileName}.";
            }
            else
            {
                TempData["Error"] = $"Backup failed: {result.ErrorMessage}";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Download(int id)
    {
        var backup = await db.DatabaseBackups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (backup is null || backup.Status != BackupStatus.Succeeded) return NotFound();

        // The SQL Server copy first, else the app-made copy in the configured folder.
        foreach (var path in new[] { backup.FilePath, backup.CopyPath })
        {
            if (string.IsNullOrEmpty(path) || !CanRead(path)) continue;
            try
            {
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return File(stream, "application/octet-stream", backup.FileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // try the next location
            }
        }

        TempData["Error"] = "This backup file isn't reachable from the web server. Copy it from the SQL Server machine's backup folder.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await backups.DeleteAsync(id);
        if (!result.Deleted)
        {
            TempData["Error"] = result.Message;
        }
        else
        {
            TempData["Success"] = result.FileRemoved
                ? "Backup deleted."
                : "Backup removed from the list. The file wasn't reachable from the web server, so delete it from the SQL Server machine's backup folder if it still exists.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<BackupIndexViewModel> BuildViewModelAsync(BackupSetting setting, List<DatabaseBackup> list, BackupSettingsViewModel form)
    {
        var vm = new BackupIndexViewModel
        {
            Settings = form,
            EffectiveDirectory = await backups.GetEffectiveDirectoryAsync(setting),
            DatabaseName = db.Database.GetDbConnection().Database,
            Backups = list,
            Downloadable = list.Where(b => b.Status == BackupStatus.Succeeded && (CanRead(b.FilePath) || (b.CopyPath is not null && CanRead(b.CopyPath)))).Select(b => b.Id).ToHashSet()
        };

        if (setting.IsEnabled)
        {
            vm.NextRunLocal = BackupSchedule.NextOccurrence(setting, DateTime.Now);
        }
        vm.LastScheduledRunLocal = list.Where(b => b.Trigger == BackupTrigger.Scheduled)
            .Select(b => (DateTime?)b.StartedAt.ToLocalTime()).FirstOrDefault();
        return vm;
    }

    private static bool CanRead(string path)
    {
        try
        {
            return System.IO.File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
