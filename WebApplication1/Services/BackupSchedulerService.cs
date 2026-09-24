using WebApplication1.Data;
using WebApplication1.Models.Common;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Services;

// Checks once a minute whether an automatic backup is due. "Due" means the most recent scheduled
// moment is later than the schedule baseline (BackupSetting.LastScheduledRunAt), so a moment missed
// while the app was down is caught up once on the next tick rather than skipped.
public class BackupSchedulerService(IServiceScopeFactory scopeFactory, ILogger<BackupSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup (migrations/seed) finish before touching the database.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>().FailInterruptedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not clean up interrupted backups.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled backup check failed.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task TickAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();

        var setting = await backups.GetSettingsAsync();
        if (!setting.IsEnabled)
        {
            return;
        }

        var due = BackupSchedule.LatestOccurrence(setting, DateTime.Now);
        var baseline = setting.LastScheduledRunAt?.ToLocalTime();
        if (baseline is not null && due <= baseline)
        {
            return;
        }

        // Record the attempt first so a failing backup retries at the next occurrence instead of
        // every minute.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tracked = await db.BackupSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (tracked is null)
        {
            return;
        }
        tracked.LastScheduledRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            var result = await backups.RunBackupAsync(BackupTrigger.Scheduled, "Scheduler");
            if (result.Status == BackupStatus.Failed)
            {
                logger.LogWarning("Scheduled backup failed: {Error}", result.ErrorMessage);
            }
        }
        catch (InvalidOperationException ex)
        {
            // A manual backup was running at that moment; the next occurrence will try again.
            logger.LogWarning(ex, "Scheduled backup skipped.");
        }
    }
}
