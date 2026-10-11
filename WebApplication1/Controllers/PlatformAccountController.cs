using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Middleware;
using WebApplication1.Models.Identity;
using WebApplication1.Services;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

/// <summary>
/// The platform operator's own sign-in page, separate from the tenant sign-in. Only platform accounts (no tenant, role
/// PlatformAdmin / PlatformBilling) can sign in here; a tenant user gets the same generic error as a wrong password.
/// </summary>
[AllowAnonymous]
public class PlatformAccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IPlatformBranding branding,
    IPasswordResetService passwordReset) : Controller
{
    [HttpGet("/Platform/Login")]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (signInManager.IsSignedIn(User))
        {
            return User.IsInRole(Roles.PlatformAdmin) || User.IsInRole(Roles.PlatformBilling) ? Redirect("/Platform") : RedirectToAction("Index", "Home");
        }
        return View(new PlatformLoginViewModel { ReturnUrl = returnUrl, PlatformName = await branding.GetNameAsync(), HasLogo = await branding.GetLogoAsync() is not null });
    }

    [HttpPost("/Platform/Login")]
    [EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(PlatformLoginViewModel model)
    {
        model.PlatformName = await branding.GetNameAsync();
        model.HasLogo = await branding.GetLogoAsync() is not null;
        model.ErrorMessage = "Invalid email or password.";
        if (!ModelState.IsValid)
        {
            model.ErrorMessage = null;
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        var isStaff = user is not null && user.TenantId is null && user.IsActive
            && (await userManager.IsInRoleAsync(user, Roles.PlatformAdmin) || await userManager.IsInRoleAsync(user, Roles.PlatformBilling));
        if (!isStaff)
        {
            return View(model);   // tenant users and unknown emails: the same message as a wrong password
        }

        var check = await signInManager.CheckPasswordSignInAsync(user!, model.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            model.ErrorMessage = check.IsLockedOut ? "This account is locked out. Try again later." : "Invalid email or password.";
            return View(model);
        }

        var claims = new List<Claim> { new("FullName", user!.FullName) };
        if (user.MustChangePassword) claims.Add(new Claim("MustChangePassword", "1"));
        await signInManager.SignInWithClaimsAsync(user, model.RememberMe, claims);
        if (UiLanguage.Normalize(user.PreferredLanguage) is { } preferred) UiLanguage.Remember(Response, preferred);

        var now = DateTime.UtcNow;
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, now).SetProperty(u => u.LastActivityAt, now));
        UserActivityMiddleware.MarkSeen(user.Id, now);

        return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? Redirect(model.ReturnUrl) : Redirect("/Platform");
    }

    private const string ResetView = "~/Views/PasswordReset/{0}.cshtml";

    private async Task<bool> IsStaffAsync(ApplicationUser? user) =>
        user is { TenantId: null, IsActive: true }
        && (await userManager.IsInRoleAsync(user, Roles.PlatformAdmin) || await userManager.IsInRoleAsync(user, Roles.PlatformBilling));

    private IActionResult ResetPage(string name, object? model = null)
    {
        ViewData["Badge"] = "Platform administration";
        ViewData["BackUrl"] = "/Platform/Login";
        return View(string.Format(ResetView, name), model);
    }

    [HttpGet("/Platform/ForgotPassword")]
    public IActionResult ForgotPassword() => ResetPage("ForgotPassword", new ForgotPasswordViewModel());

    [HttpPost("/Platform/ForgotPassword")]
    [EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return ResetPage("ForgotPassword", model);
        }

        // Same response whether or not the account exists. Platform staff only: tenant users reset on their own page.
        var user = await userManager.FindByEmailAsync(model.Email);
        if (await IsStaffAsync(user))
        {
            await passwordReset.SendLinkAsync(user!, token => Url.Action(nameof(ResetPassword), "PlatformAccount",
                new { email = user!.Email, token }, Request.Scheme)!);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet("/Platform/ForgotPassword/Sent")]
    public IActionResult ForgotPasswordConfirmation() => ResetPage("ForgotPasswordConfirmation");

    [HttpGet("/Platform/ResetPassword")]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return Redirect("/Platform/Login");
        }

        return ResetPage("ResetPassword", new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost("/Platform/ResetPassword")]
    [EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return ResetPage("ResetPassword", model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (!await IsStaffAsync(user))
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));   // don't reveal that the account doesn't exist
        }

        var result = await passwordReset.ResetAsync(user!, model.Token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return ResetPage("ResetPassword", model);
        }

        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [HttpGet("/Platform/ResetPassword/Done")]
    public IActionResult ResetPasswordConfirmation() => ResetPage("ResetPasswordConfirmation");

    /// <summary>The platform logo (anonymous: the sign-in page needs it). Falls back to a built-in mark.</summary>
    [HttpGet("/Platform/Logo")]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> Logo()
    {
        var logo = await branding.GetLogoAsync();
        if (logo is not null) return File(logo.Bytes, logo.ContentType);

        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64" role="img" aria-label="Platform">
              <rect width="64" height="64" rx="14" fill="#312e81"/>
              <path d="M20 42h25a9 9 0 0 0 1.3-17.9A12 12 0 0 0 23 26a8 8 0 0 0-3 16z" fill="#fff"/>
              <circle cx="46" cy="46" r="9" fill="#f59e0b"/>
              <path d="M42 46l3 3 5-6" stroke="#312e81" stroke-width="2.5" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            """;
        return Content(svg, "image/svg+xml");
    }
}
