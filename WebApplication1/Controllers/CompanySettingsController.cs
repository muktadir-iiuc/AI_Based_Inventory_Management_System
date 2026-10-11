using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Controllers;

[Authorize(Roles = Roles.Admin)]
public class CompanySettingsController(ICompanySettingsService companySettings, ITenantPortalService portal, ITenantContext tenantContext) : Controller
{
    private const int MaxLogoBytes = 1024 * 1024;
    private static readonly string[] AllowedLogoTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp"];

    public async Task<IActionResult> Index()
    {
        var company = await companySettings.GetAsync();
        return View(new CompanySettingsViewModel
        {
            CompanyName = company.CompanyName,
            ShortName = company.ShortName,
            Address = company.Address,
            Phone = company.Phone,
            HasLogo = company.LogoImage is not null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CompanySettingsViewModel model)
    {
        var current = await companySettings.GetAsync();
        model.HasLogo = current.LogoImage is not null;

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
                var bytes = buffer.ToArray();
                logoType = ImageSignature.Detect(bytes);   // the file's own bytes decide, not the browser's claim
                if (logoType is null)
                {
                    ModelState.AddModelError(nameof(model.LogoFile), "The logo must be a PNG, JPEG, GIF or BMP image.");
                }
                else
                {
                    logo = bytes;
                }
            }
        }

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
            User.Identity?.Name);

        TempData["Success"] = "Company settings updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// A signed-in user always gets their own tenant's logo. An anonymous visitor (a tenant's sign-in page) may ask for a tenant's logo
    /// by its code or domain; no other tenant data is exposed and there is no fallback to someone else's logo.
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Logo(string? tenant = null)
    {
        Models.Common.CompanySetting? company;
        if (tenantContext.TenantId > 0)
        {
            company = await companySettings.GetAsync();
        }
        else
        {
            var resolved = await portal.ResolveAsync(Request.Host.Host, tenant);
            if (resolved is null) return NotFound();
            using (tenantContext.UseTenant(resolved.Id))
            {
                company = await companySettings.GetAsync();
            }
        }

        if (company?.LogoImage is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=60";
        return File(company.LogoImage, company.LogoMimeType ?? "image/png");
    }
}
