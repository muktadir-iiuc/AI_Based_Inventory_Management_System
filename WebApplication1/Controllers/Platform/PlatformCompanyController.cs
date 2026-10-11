using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Tenancy;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Controllers;

/// <summary>
/// Platform-side maintenance of each business's company profile (the same record the tenant's Admin edits under Company Settings):
/// platform record name, company name, short name, address, phone and logo. Writes go through the tenant's own settings service
/// inside an explicit tenant scope, so one business's profile can never end up on another's.
/// </summary>
[Route("Platform/Company")]
[Authorize(Roles = Roles.PlatformAdmin)]
public class PlatformCompanyController(
    ApplicationDbContext db,
    ITenantContext tenantContext,
    ICompanySettingsService companySettings,
    ISaasAudit audit) : PlatformControllerBase
{
    private const int MaxLogoBytes = 1024 * 1024;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var query = db.Tenants.AsNoTracking().Where(t => t.PurgedAt == null).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t => t.Name.Contains(term) || t.Code.Contains(term));
        }
        var paged = await query.OrderBy(t => t.Name).ToPagedAsync(page, PageSize);

        // Only the company-profile fields are read (name, short name, whether a logo exists) - nothing else from the business.
        var ids = paged.Items.Select(t => t.Id).ToList();
        Dictionary<int, (string Company, string Short, bool Logo)> profiles;
        using (tenantContext.BypassFilters())
        {
            profiles = (await db.CompanySettings.AsNoTracking()
                    .Where(c => ids.Contains(EF.Property<int>(c, ApplicationDbContext.TenantIdProperty)))
                    .Select(c => new { Tenant = EF.Property<int>(c, ApplicationDbContext.TenantIdProperty), c.CompanyName, c.ShortName, Logo = c.LogoImage != null })
                    .ToListAsync())
                .GroupBy(c => c.Tenant).ToDictionary(g => g.Key, g => (g.First().CompanyName, g.First().ShortName, g.First().Logo));
        }

        ViewData["Profiles"] = profiles;
        ViewData["Q"] = q;
        return View(paged);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null || tenant.PurgedAt is not null) return NotFound();

        using (tenantContext.UseTenant(id))
        {
            var company = await companySettings.GetAsync();
            var hasRow = await db.CompanySettings.AnyAsync();
            return View(new PlatformCompanyViewModel
            {
                TenantId = id, TenantCode = tenant.Code, TenantName = tenant.Name,
                CompanyName = hasRow ? company.CompanyName : tenant.Name,
                ShortName = hasRow ? company.ShortName : (tenant.Name.Length > 50 ? tenant.Name[..50] : tenant.Name),
                Address = company.Address ?? tenant.Address, Phone = company.Phone ?? tenant.ContactPhone,
                HasLogo = company.LogoImage is not null
            });
        }
    }

    [HttpPost("{id:int}")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, PlatformCompanyViewModel model)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null || tenant.PurgedAt is not null) return NotFound();
        model.TenantId = id;
        model.TenantCode = tenant.Code;

        byte[]? logo = null;
        string? logoType = null;
        if (model.LogoFile is { Length: > 0 })
        {
            if (model.LogoFile.Length > MaxLogoBytes)
            {
                ModelState.AddModelError(nameof(model.LogoFile), "The logo must be 1 MB or smaller.");
            }
            else
            {
                using var buffer = new MemoryStream();
                await model.LogoFile.CopyToAsync(buffer);
                logo = buffer.ToArray();
                logoType = ImageSignature.Detect(logo);   // the file's own bytes decide, not the browser's claim
                if (logoType is null)
                {
                    ModelState.AddModelError(nameof(model.LogoFile), "The logo must be a PNG, JPEG, GIF or BMP image.");
                    logo = null;
                }
            }
        }

        using (tenantContext.UseTenant(id))
        {
            model.HasLogo = (await companySettings.GetAsync()).LogoImage is not null;
            if (!ModelState.IsValid) return View(model);

            await companySettings.UpdateAsync(
                new CompanySetting
                {
                    CompanyName = model.CompanyName.Trim(),
                    ShortName = model.ShortName.Trim(),
                    Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim(),
                    Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
                    LogoImage = logo,
                    LogoMimeType = logoType
                },
                model.RemoveLogo && logo is null,
                Actor);
        }

        var renamed = !string.Equals(tenant.Name, model.TenantName.Trim(), StringComparison.Ordinal);
        tenant.Name = model.TenantName.Trim();
        tenant.UpdatedAt = DateTime.UtcNow;
        audit.Add("tenant.company-updated", nameof(Tenant), id.ToString(), id,
            details: $"company \"{model.CompanyName.Trim()}\"{(renamed ? ", platform name changed" : "")}{(logo is not null ? ", logo replaced" : model.RemoveLogo ? ", logo removed" : "")}");
        await db.SaveChangesAsync();

        TempData["Success"] = $"Company profile of {tenant.Name} saved.";
        return RedirectToAction(nameof(Edit), new { id });
    }
}
