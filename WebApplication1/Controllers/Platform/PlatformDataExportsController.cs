using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

public class DataExportRow
{
    public DataExportRequest Request { get; set; } = null!;
    public string TenantName { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string SubscriptionStatus { get; set; } = string.Empty;
    public string? AgreementVersion { get; set; }
}

public class DataExportListViewModel
{
    public PagedResult<DataExportRow> Page { get; set; } = null!;
    public string? Status { get; set; }
    public int? TenantId { get; set; }
    public List<Tenant> Tenants { get; set; } = [];
    public bool IsAdmin { get; set; }
    public int PendingCount { get; set; }
}

/// <summary>
/// Platform side of tenant data requests: see who asked for their data (cancelling a plan, moving to another system), generate the export
/// in the format they chose (CSV, JSON or Excel) and hand it over. Generating and downloading business data is Platform Administrator only.
/// </summary>
[Route("Platform/DataExports")]
public class PlatformDataExportsController(
    ApplicationDbContext db,
    IDataExportService exporter,
    ISaasAudit audit) : PlatformControllerBase
{
    private const int ExpiryDays = 14;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int? tenantId, int page = 1)
    {
        var query = db.DataExportRequests.AsNoTracking().AsQueryable();
        if (Enum.TryParse<ExportStatus>(status, out var st) && st != ExportStatus.Expired) query = query.Where(r => r.Status == st);
        if (tenantId is int tid) query = query.Where(r => r.TenantId == tid);
        // Open requests first, then newest.
        query = query.OrderBy(r => r.Status == ExportStatus.Requested ? 0 : 1).ThenByDescending(r => r.RequestedAt);

        var paged = await query.ToPagedAsync(page, PageSize);
        var ids = paged.Items.Select(r => r.TenantId).Distinct().ToList();
        var tenants = await db.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var subs = await db.TenantSubscriptions.AsNoTracking().Where(s => ids.Contains(s.TenantId)).ToDictionaryAsync(s => s.TenantId, s => s.Status.ToString());
        var agreements = await db.DataAgreementAcceptances.AsNoTracking().Where(a => ids.Contains(a.TenantId))
            .GroupBy(a => a.TenantId).Select(g => new { g.Key, Version = g.OrderByDescending(a => a.AcceptedAt).Select(a => a.Version).First() })
            .ToDictionaryAsync(x => x.Key, x => x.Version);

        var now = DateTime.UtcNow;
        var rows = new PagedResult<DataExportRow>
        {
            Page = paged.Page, PageSize = paged.PageSize, Total = paged.Total,
            Items = paged.Items.Select(r =>
            {
                if (r.Status == ExportStatus.Ready && r.ExpiresAt < now) r.Status = ExportStatus.Expired;
                return new DataExportRow
                {
                    Request = r,
                    TenantName = tenants.GetValueOrDefault(r.TenantId)?.Name ?? "?",
                    TenantCode = tenants.GetValueOrDefault(r.TenantId)?.Code ?? "?",
                    SubscriptionStatus = subs.GetValueOrDefault(r.TenantId) ?? "-",
                    AgreementVersion = agreements.GetValueOrDefault(r.TenantId)
                };
            }).ToList()
        };

        return View(new DataExportListViewModel
        {
            Page = rows,
            Status = status,
            TenantId = tenantId,
            Tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(),
            IsAdmin = IsPlatformAdmin,
            PendingCount = await db.DataExportRequests.CountAsync(r => r.Status == ExportStatus.Requested)
        });
    }

    /// <summary>A request taken on behalf of a tenant (by phone or email).</summary>
    [HttpPost("Create"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Create(int tenantId, ExportFormat format, ExportReason reason, string? notes)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId) || !Enum.IsDefined(format) || !Enum.IsDefined(reason))
        {
            TempData["Error"] = "Choose a business and a format.";
            return RedirectToAction(nameof(Index));
        }
        db.DataExportRequests.Add(new DataExportRequest
        {
            TenantId = tenantId, Format = format, Reason = reason,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()[..Math.Min(notes.Trim().Length, 1000)],
            RequestedBy = Actor + " (on behalf)"
        });
        audit.Add("DataExport.Requested", "DataExportRequest", null, tenantId, details: $"{format}, {reason}, on behalf by platform");
        await db.SaveChangesAsync();
        TempData["Success"] = "Request recorded. Generate it from the list.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Generate/{id:int}"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Generate(int id, ExportFormat format)
    {
        var request = await db.DataExportRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.Status == ExportStatus.Rejected || !Enum.IsDefined(format))
        {
            TempData["Error"] = "This request cannot be generated.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var result = await exporter.GenerateAsync(request.TenantId, format, HttpContext.RequestAborted);
            // A regenerated export replaces the previous file of this request.
            if (request.FileName is not null)
            {
                var old = Path.Combine(exporter.FolderFor(request.TenantId), Path.GetFileName(request.FileName));
                try { if (System.IO.File.Exists(old)) System.IO.File.Delete(old); } catch { /* best effort */ }
            }
            request.Format = format;
            request.Status = ExportStatus.Ready;
            request.FileName = result.FileName;
            request.FileSize = result.Size;
            request.TableCount = result.Tables;
            request.RowCount = result.Rows;
            request.ProcessedBy = Actor;
            request.ProcessedAt = DateTime.UtcNow;
            request.ExpiresAt = DateTime.UtcNow.AddDays(ExpiryDays);
            request.AgreementVersion = DataAgreement.Version;
            request.Error = null;
            audit.Add("DataExport.Generated", "DataExportRequest", id.ToString(), request.TenantId, details: $"{format}, {result.Tables} tables, {result.Rows} rows");
            await db.SaveChangesAsync();
            TempData["Success"] = $"Export ready: {result.Tables} tables, {result.Rows:N0} rows. It stays available to the tenant for {ExpiryDays} days.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            request.Status = ExportStatus.Failed;
            request.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            request.ProcessedBy = Actor;
            request.ProcessedAt = DateTime.UtcNow;
            audit.Add("DataExport.Generated", "DataExportRequest", id.ToString(), request.TenantId, "Failed", request.Error);
            await db.SaveChangesAsync();
            TempData["Error"] = "The export failed: " + request.Error;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Reject/{id:int}"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var request = await db.DataExportRequests.FirstOrDefaultAsync(r => r.Id == id && r.Status == ExportStatus.Requested);
        if (request is null) return NotFound();
        request.Status = ExportStatus.Rejected;
        request.RejectReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
        request.ProcessedBy = Actor;
        request.ProcessedAt = DateTime.UtcNow;
        audit.Add("DataExport.Rejected", "DataExportRequest", id.ToString(), request.TenantId, details: request.RejectReason);
        await db.SaveChangesAsync();
        TempData["Success"] = "Request rejected.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Download/{id:int}"), Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Download(int id)
    {
        var request = await db.DataExportRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request?.FileName is null || request.Status != ExportStatus.Ready) return NotFound();
        var path = Path.Combine(exporter.FolderFor(request.TenantId), Path.GetFileName(request.FileName));
        if (!System.IO.File.Exists(path)) return NotFound();
        audit.Add("DataExport.DownloadedByPlatform", "DataExportRequest", id.ToString(), request.TenantId, details: request.FileName);
        await db.SaveChangesAsync();
        return PhysicalFile(path, exporter.ContentType(request.Format), request.FileName);
    }
}
