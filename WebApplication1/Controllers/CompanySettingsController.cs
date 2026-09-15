using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

[Authorize(Roles = Roles.AdminManagers)]
public class CompanySettingsController(ICompanySettingsService companySettings) : Controller
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
        if (model.LogoFile is { Length: > 0 })
        {
            if (model.LogoFile.Length > MaxLogoBytes)
            {
                ModelState.AddModelError(nameof(model.LogoFile), "The logo must be 1 MB or smaller.");
            }
            else if (!AllowedLogoTypes.Contains(model.LogoFile.ContentType))
            {
                ModelState.AddModelError(nameof(model.LogoFile), "The logo must be a PNG, JPEG, GIF or BMP image.");
            }
            else
            {
                using var buffer = new MemoryStream();
                await model.LogoFile.CopyToAsync(buffer);
                logo = buffer.ToArray();
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
                LogoMimeType = logo is null ? null : model.LogoFile!.ContentType
            },
            model.RemoveLogo && logo is null,
            User.Identity?.Name);

        TempData["Success"] = "Company settings updated.";
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous]
    public async Task<IActionResult> Logo()
    {
        var company = await companySettings.GetAsync();
        if (company.LogoImage is null) return NotFound();

        return File(company.LogoImage, company.LogoMimeType ?? "image/png");
    }
}
