using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

/// <summary>Switches the interface language (English / Bangla). Works signed-out too (sign-in pages).</summary>
[AllowAnonymous]
public class LanguageController(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : Controller
{
    // Only sets a harmless preference cookie, so a stale page (expired antiforgery token) must still be able to switch language.
    [HttpPost, IgnoreAntiforgeryToken]
    public async Task<IActionResult> Set(string lang, string? returnUrl = null)
    {
        var code = UiLanguage.Normalize(lang) ?? UiLanguage.English;
        UiLanguage.Remember(Response, code);

        if (User.Identity?.IsAuthenticated == true && userManager.GetUserId(User) is { } userId)
        {
            await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, code));
        }

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : Redirect("/");
    }
}
