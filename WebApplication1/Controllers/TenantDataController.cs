using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

public record TenantDataViewModel(
    DataAgreementAcceptance? Latest,
    bool AcceptedCurrent,
    int RetentionDays,
    IReadOnlyList<DataExportRequest> Requests,
    bool HasOpenRequest);

/// <summary>
/// The tenant's own "Data &amp; agreement" page: accept the data agreement and ask the platform for a copy of the business data
/// (when ending a plan, moving to another system, or just for a backup). Every query is bound to the signed-in TenantId claim.
/// Reachable while the subscription is suspended or expired - a business that is leaving must still be able to take its data.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[Route("Billing/Data")]
public class TenantDataController(
    ApplicationDbContext db,
    IDataAgreementService agreement,
    IDataExportService exporter,
    IPlatformSettings settings,
    ISaasAudit audit) : Controller
{
    private int TenantId => User.GetTenantId();
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var requests = await db.DataExportRequests.AsNoTracking().Where(r => r.TenantId == TenantId)
            .OrderByDescending(r => r.RequestedAt).Take(50).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var r in requests.Where(r => r.Status == ExportStatus.Ready && r.ExpiresAt < now)) r.Status = ExportStatus.Expired;

        return View(new TenantDataViewModel(
            await agreement.LatestAsync(TenantId),
            await agreement.HasAcceptedCurrentAsync(TenantId),
            await settings.GetIntAsync(PlatformSettingKeys.RetentionDays, 90),
            requests,
            requests.Any(r => r.Status == ExportStatus.Requested)));
    }

    [HttpPost("Accept"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(bool agree)
    {
        if (!agree)
        {
            TempData["Error"] = "Tick the box to accept the data agreement.";
            return RedirectToAction(nameof(Index));
        }
        var subId = await db.TenantSubscriptions.Where(s => s.TenantId == TenantId).Select(s => (int?)s.Id).FirstOrDefaultAsync();
        await agreement.AcceptAsync(TenantId, "Portal", UserId, User.Identity?.Name, subId);
        audit.Add("DataAgreement.Accepted", "Tenant", TenantId.ToString(), TenantId, details: $"version {DataAgreement.Version}");
        await db.SaveChangesAsync();
        TempData["Success"] = "Data agreement accepted. Thank you.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Request"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Request(ExportFormat format, ExportReason reason, string? notes)
    {
        if (!Enum.IsDefined(format) || !Enum.IsDefined(reason)) return BadRequest();
        if (await db.DataExportRequests.AnyAsync(r => r.TenantId == TenantId && r.Status == ExportStatus.Requested))
        {
            TempData["Error"] = "You already have a data request waiting. We will prepare it as soon as possible.";
            return RedirectToAction(nameof(Index));
        }

        var request = new DataExportRequest
        {
            TenantId = TenantId,
            Format = format,
            Reason = reason,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()[..Math.Min(notes.Trim().Length, 1000)],
            RequestedByUserId = UserId,
            RequestedBy = User.Identity?.Name
        };
        db.DataExportRequests.Add(request);
        audit.Add("DataExport.Requested", "DataExportRequest", null, TenantId, details: $"{format}, {reason}");
        await db.SaveChangesAsync();
        TempData["Success"] = "Your data request was sent. You will find the download here once the platform team has prepared it.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Download/{id:int}")]
    public async Task<IActionResult> Download(int id)
    {
        var request = await db.DataExportRequests.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == TenantId);
        if (request is null) return NotFound();
        if (request.Status != ExportStatus.Ready || request.FileName is null || request.ExpiresAt < DateTime.UtcNow)
        {
            TempData["Error"] = "This download is not available (not ready yet, or expired). Please request your data again.";
            return RedirectToAction(nameof(Index));
        }

        var path = Path.Combine(exporter.FolderFor(TenantId), Path.GetFileName(request.FileName));
        if (!System.IO.File.Exists(path))
        {
            TempData["Error"] = "The file is no longer available. Please request your data again.";
            return RedirectToAction(nameof(Index));
        }

        request.DownloadedAt ??= DateTime.UtcNow;
        audit.Add("DataExport.Downloaded", "DataExportRequest", request.Id.ToString(), TenantId, details: request.FileName);
        await db.SaveChangesAsync();
        return PhysicalFile(path, exporter.ContentType(request.Format), request.FileName);
    }
}

/// <summary>The data agreement text, readable by anyone (linked from sign-up, plan selection and the platform).</summary>
[AllowAnonymous]
public class DataAgreementController(IPlatformSettings settings) : Controller
{
    [HttpGet("/DataAgreement")]
    public async Task<IActionResult> Index()
    {
        ViewData["RetentionDays"] = await settings.GetIntAsync(PlatformSettingKeys.RetentionDays, 90);
        return View();
    }
}
