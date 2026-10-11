using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services.Saas;

public interface ISaasNotificationDispatcher
{
    Task<int> DispatchPendingEmailsAsync(CancellationToken cancellationToken = default);
}

public class SaasNotificationDispatcher(ApplicationDbContext db, ITenantContext tenantContext, IEmailService email, TimeProvider time) : ISaasNotificationDispatcher
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;

    public async Task<int> DispatchPendingEmailsAsync(CancellationToken ct = default)
    {
        using var _ = tenantContext.BypassFilters();
        var pending = await db.SaasNotifications
            .Where(n => n.Channel == NotificationChannel.Email && n.Status == NotificationStatus.Pending && n.Attempts < MaxAttempts)
            .OrderBy(n => n.Id).Take(BatchSize).ToListAsync(ct);

        foreach (var n in pending)
        {
            n.Attempts++;
            try
            {
                // IEmailService logs and swallows delivery failures, so "Sent" means handed to the mail service.
                await email.SendAsync(n.ToEmail!, n.Subject, n.Body);
                n.Status = NotificationStatus.Sent;
                n.SentAt = time.GetUtcNow().UtcDateTime;
            }
            catch (Exception ex)
            {
                n.LastError = ex.Message.Length > 450 ? ex.Message[..450] : ex.Message;
                if (n.Attempts >= MaxAttempts) n.Status = NotificationStatus.Failed;
            }
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}

/// <summary>
/// Runs the billing lifecycle on a timer. State lives in the database, so a restart simply resumes: the next
/// pass finds whatever is still due. A SQL Server application lock keeps two app instances from running a pass
/// at the same time. Nothing here depends on a user request.
/// </summary>
public class BillingSchedulerService(IServiceScopeFactory scopeFactory, ILogger<BillingSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string LockName = "ims-saas-billing-scheduler";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Billing scheduler pass failed; retrying at the next interval.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var relational = db.Database.IsRelational();
        if (relational)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        try
        {
            if (relational && !await TryAcquireLockAsync(db, ct))
            {
                return; // another instance is running the pass
            }

            var report = await scope.ServiceProvider.GetRequiredService<ISubscriptionLifecycleService>().RunAsync(ct);
            // Payments whose callback never arrived, and refunds still in progress at the provider.
            var reconciled = 0;
            try
            {
                reconciled = await scope.ServiceProvider.GetRequiredService<Payments.IPaymentGatewayService>().ReconcileAsync(ct);
                reconciled += await scope.ServiceProvider.GetRequiredService<Payments.IRefundService>().RefreshPendingAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Payment reconciliation failed; retrying at the next interval.");
            }
            var emails = await scope.ServiceProvider.GetRequiredService<ISaasNotificationDispatcher>().DispatchPendingEmailsAsync(ct);

            if (report.Errors > 0 || report.TrialsExpired + report.RenewalInvoices + report.Overdue + report.Suspended + report.Cancelled + report.Reminders + emails + reconciled > 0)
            {
                logger.LogInformation(
                    "Billing pass: {Trials} trials expired, {Renewals} renewal invoices, {Reminders} reminders, {Overdue} overdue, {Suspended} suspended, {Cancelled} cancelled, {Emails} emails, {Errors} errors.",
                    report.TrialsExpired, report.RenewalInvoices, report.Reminders, report.Overdue, report.Suspended, report.Cancelled, emails, report.Errors);
            }
        }
        finally
        {
            if (relational)
            {
                await ReleaseLockAsync(db);
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task<bool> TryAcquireLockAsync(ApplicationDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
        {
            return true;
        }

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@res";
        parameter.Value = LockName;
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        return result >= 0;
    }

    private static async Task ReleaseLockAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Session';";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@res";
            parameter.Value = LockName;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // The lock also disappears when the connection closes.
        }
    }
}
