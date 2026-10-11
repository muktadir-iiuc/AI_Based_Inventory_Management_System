using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Saas.Payments;

namespace WebApplication1.Controllers;

[Route("Platform/Providers")]
[Authorize(Roles = Roles.PlatformAdmin)]
public class PlatformProvidersController(ApplicationDbContext db, IPaymentProviderRegistry registry, IPlatformSettings settings, ISaasAudit audit) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Configured"] = registry.All.ToDictionary(p => p.Code, p => p.IsConfigured);
        ViewData["Instructions"] = await settings.GetAsync(PlatformSettingKeys.ManualPaymentInstructions);
        return View(await db.PaymentProviders.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync());
    }

    [HttpPost("{id:int}/Toggle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var p = await db.PaymentProviders.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        p.IsEnabled = !p.IsEnabled;
        p.UpdatedAt = DateTime.UtcNow; p.UpdatedBy = Actor;
        audit.Add("provider.toggled", nameof(PaymentProviderConfig), p.Code, details: $"{p.Code} enabled = {p.IsEnabled}");
        await db.SaveChangesAsync();
        TempData["Success"] = $"{p.DisplayName} is now {(p.IsEnabled ? "enabled" : "disabled")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Instructions"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Instructions(string instructions)
    {
        await settings.SetAsync(PlatformSettingKeys.ManualPaymentInstructions, (instructions ?? "").Trim(), Actor);
        await audit.LogAsync("settings.updated", nameof(PlatformSetting), PlatformSettingKeys.ManualPaymentInstructions, details: "manual payment instructions edited");
        TempData["Success"] = "Payment instructions saved.";
        return RedirectToAction(nameof(Index));
    }
}

[Route("Platform/Settings")]
[Authorize(Roles = Roles.PlatformAdmin)]
public class PlatformSettingsController(IPlatformSettings settings, IPlatformBranding branding, ISaasAudit audit) : PlatformControllerBase
{
    private static readonly HashSet<string> IntKeys = [PlatformSettingKeys.DefaultTrialDays, PlatformSettingKeys.DefaultGraceDays, PlatformSettingKeys.InvoiceDueDays,
        PlatformSettingKeys.RenewalLeadDays, PlatformSettingKeys.RetentionDays, PlatformSettingKeys.AttemptExpiryMinutes];

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["HasLogo"] = await branding.GetLogoAsync() is not null;
        return View(new SettingsViewModel { Values = await settings.GetAllAsync() });
    }

    /// <summary>The platform's own logo: shown on the platform sign-in page, the platform screens and subscription invoice PDFs.</summary>
    [HttpPost("Logo")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Logo(IFormFile? logo, bool remove = false)
    {
        if (remove)
        {
            await branding.RemoveLogoAsync();
            await audit.LogAsync("settings.logo-removed", nameof(PlatformSetting), "platform.logo");
            TempData["Success"] = "Platform logo removed.";
        }
        else if (logo is null)
        {
            TempData["Error"] = "Choose an image file.";
        }
        else
        {
            await using var stream = logo.OpenReadStream();
            var error = await branding.SaveLogoAsync(stream, logo.Length);
            if (error is null)
            {
                await audit.LogAsync("settings.logo-updated", nameof(PlatformSetting), "platform.logo", details: $"{logo.Length} bytes");
                TempData["Success"] = "Platform logo saved.";
            }
            else
            {
                TempData["Error"] = error;
            }
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save()
    {
        var current = await settings.GetAllAsync();
        var errors = new List<string>();
        var changes = new Dictionary<string, string>();

        foreach (var key in PlatformSettingKeys.Defaults.Keys)
        {
            if (!Request.Form.TryGetValue(key, out var raw)) continue;
            var value = raw.ToString().Trim();
            if (value == current.GetValueOrDefault(key)) continue;

            if (IntKeys.Contains(key) && !(int.TryParse(value, out var n) && n is >= 0 and <= 3650)) errors.Add($"{key}: enter a whole number of 0-3650.");
            else if (key == PlatformSettingKeys.DefaultTaxPercent && !(decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var tax) && tax is >= 0 and <= 100)) errors.Add($"{key}: enter a percentage 0-100.");
            else if (key == PlatformSettingKeys.DefaultCurrency && !(value.Length == 3 && value.All(char.IsLetter))) errors.Add($"{key}: use a 3-letter currency code such as BDT.");
            else if (key == PlatformSettingKeys.ReminderDays && value.Length > 0 && !value.Split(',').All(p => int.TryParse(p.Trim(), out var d) && d is > 0 and <= 90)) errors.Add($"{key}: comma-separated days between 1 and 90, e.g. 7,3,1.");
            else if (key is PlatformSettingKeys.TermsUrl or PlatformSettingKeys.PrivacyUrl or PlatformSettingKeys.PlatformPublicUrl && value.Length > 0 && !(Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")) errors.Add($"{key}: enter a full http(s) address.");
            else if (key == PlatformSettingKeys.RegistrationEnabled && value is not ("true" or "false")) errors.Add($"{key}: true or false.");
            else if (key == PlatformSettingKeys.InvoicePrefix && !(value.Length is > 0 and <= 10 && value.All(c => char.IsLetterOrDigit(c) || c == '-'))) errors.Add($"{key}: 1-10 letters, digits or dashes.");
            else if (value.Length > 1000) errors.Add($"{key}: too long.");
            else changes[key] = key == PlatformSettingKeys.DefaultCurrency ? value.ToUpperInvariant() : value;
        }

        if (errors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", errors);
            return RedirectToAction(nameof(Index));
        }

        foreach (var (key, value) in changes)
        {
            await settings.SetAsync(key, value, Actor);
            // Record what changed without dumping long free text.
            audit.Add("settings.updated", nameof(PlatformSetting), key, details: value.Length > 120 ? "(long text edited)" : $"{current.GetValueOrDefault(key)} -> {value}");
        }
        await audit.LogAsync("settings.saved", nameof(PlatformSetting), details: $"{changes.Count} setting(s) changed");
        TempData["Success"] = changes.Count == 0 ? "Nothing changed." : $"{changes.Count} setting(s) saved.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Platform administrators manage the people who can sign in to the platform area (admins and billing staff).</summary>
[Route("Platform/Staff")]
[Authorize(Roles = Roles.PlatformAdmin)]
public class PlatformStaffController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISaasAudit audit) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var staff = await db.Users.AsNoTracking().Where(u => u.TenantId == null).OrderBy(u => u.FullName).ToListAsync();
        var rows = new List<(ApplicationUser User, string Roles)>();
        foreach (var u in staff) rows.Add((u, string.Join(", ", await userManager.GetRolesAsync(u))));
        ViewData["Rows"] = rows;
        return View(new StaffCreateViewModel());
    }

    [HttpPost("Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffCreateViewModel model)
    {
        if (model.Role is not (Roles.PlatformAdmin or Roles.PlatformBilling)) ModelState.AddModelError(nameof(model.Role), "Invalid role.");
        if (!ModelState.IsValid) { TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)); return RedirectToAction(nameof(Index)); }

        var user = new ApplicationUser { UserName = model.Email.Trim(), Email = model.Email.Trim(), FullName = model.FullName.Trim(), EmailConfirmed = true, IsActive = true, TenantId = null };
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded) { TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description)); return RedirectToAction(nameof(Index)); }
        await userManager.AddToRoleAsync(user, model.Role);
        await audit.LogAsync("platform-staff.created", nameof(ApplicationUser), user.Id, details: $"{user.Email} as {model.Role}");
        TempData["Success"] = "Platform user created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/Toggle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string id)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == null);
        if (user is null) return NotFound();
        if (user.Id == userManager.GetUserId(User)) { TempData["Error"] = "You cannot deactivate yourself."; return RedirectToAction(nameof(Index)); }
        user.IsActive = !user.IsActive;
        audit.Add("platform-staff.toggled", nameof(ApplicationUser), user.Id, details: $"{user.Email} active = {user.IsActive}");
        await db.SaveChangesAsync();
        TempData["Success"] = $"{user.Email} is now {(user.IsActive ? "active" : "deactivated")}.";
        return RedirectToAction(nameof(Index));
    }
}
