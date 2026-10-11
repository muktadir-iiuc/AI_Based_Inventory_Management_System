using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Middleware;
using WebApplication1.Models.Identity;
using WebApplication1.Services;
using WebApplication1.Models.Tenancy;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Controllers;

[AllowAnonymous]
public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    ITenantContext tenantContext,
    Services.Saas.ITenantPortalService portal,
    Services.Saas.IPlatformBranding branding,
    IPasswordResetService passwordReset) : Controller
{
    private const string LastTenantCookie = "ims_tenant";

    /// <summary>
    /// Which business's name and logo the sign-in page shows. An explicit tenant link or domain always wins. Otherwise the page is
    /// still branded when the business is obvious: the business this browser last signed in to (a cookie holding only its code), or
    /// the only business on the platform. Otherwise it stays neutral. This is DISPLAY only - nothing is enforced from the guess, so any
    /// user can still sign in on the shared page; only an explicit link/domain restricts who may sign in.
    /// </summary>
    private async Task<Tenant?> BrandTenantAsync(Tenant? explicitTenant, bool otherBusiness)
    {
        if (explicitTenant is not null || otherBusiness) return explicitTenant;

        if (Request.Cookies[LastTenantCookie] is { Length: > 0 and <= 60 } code && await portal.ResolveAsync(null, code) is { } last)
        {
            return last;
        }

        var candidates = await db.Tenants.AsNoTracking()
            .Where(t => t.PurgedAt == null && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Suspended) && db.Users.Any(u => u.TenantId == t.Id))
            .OrderBy(t => t.Id).Take(2).ToListAsync();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    // A tenant's own name and logo when known; the platform's neutral product name otherwise.
    private async Task BrandAsync(LoginViewModel model, Tenant? explicitTenant, Tenant? brandTenant)
    {
        var portalTenant = brandTenant;
        model.TenantCode = explicitTenant?.Code;          // only an explicit link/domain restricts sign-in
        model.TenantName = portalTenant?.Name;
        model.ShowOtherBusinessLink = explicitTenant is null && portalTenant is not null;
        if (portalTenant is null)
        {
            model.BrandName = await branding.GetNameAsync();
            model.BrandLogoUrl = "/Platform/Logo";
            return;
        }

        using (tenantContext.UseTenant(portalTenant.Id))
        {
            var company = await db.CompanySettings.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
            model.BrandName = string.IsNullOrWhiteSpace(company?.CompanyName) ? portalTenant.Name : company.CompanyName;
            model.BrandLogoUrl = company?.LogoImage is null ? null : $"/CompanySettings/Logo?tenant={portalTenant.Code}";
        }
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null, string? tenant = null, bool other = false)
    {
        if (signInManager.IsSignedIn(User))
        {
            return RedirectToAction("Index", "Home");
        }

        // "Sign in to a different business": forget the remembered one and show the neutral page.
        if (other)
        {
            Response.Cookies.Delete(LastTenantCookie);
        }

        // A tenant's own link or domain shows that business's name and only lets its users in.
        var portalTenant = await portal.ResolveAsync(Request.Host.Host, tenant);
        var model = new LoginViewModel { ReturnUrl = returnUrl };
        await BrandAsync(model, portalTenant, await BrandTenantAsync(portalTenant, other));
        return View(model);
    }

    [HttpPost]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var portalTenant = await portal.ResolveAsync(Request.Host.Host, model.TenantCode);
        await BrandAsync(model, portalTenant, await BrandTenantAsync(portalTenant, otherBusiness: false));
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        // On a tenant's page only that tenant's users may sign in; anyone else gets the same generic message as a wrong password.
        if (user is null || !user.IsActive || (portalTenant is not null && user.TenantId != portalTenant.Id))
        {
            model.ErrorMessage = "Invalid email or password.";
            return View(model);
        }

        var check = await signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            model.ErrorMessage = check.IsLockedOut
                ? "This account is locked out. Try again later."
                : "Invalid email or password.";
            return View(model);
        }

        // Platform accounts have their own console sign-in; they never use the business sign-in page.
        if (user.TenantId is null)
        {
            model.ErrorMessage = await userManager.IsInRoleAsync(user, Roles.PlatformAdmin) || await userManager.IsInRoleAsync(user, Roles.PlatformBilling)
                ? "This is a platform account. Sign in on the platform administration page."
                : "Invalid email or password.";
            model.ShowPlatformLink = model.ErrorMessage.StartsWith("This is a platform");
            return View(model);
        }

        var extraClaims = new List<Claim>
        {
            new("FullName", user.FullName)
        };
        if (user.MustChangePassword)
        {
            extraClaims.Add(new Claim("MustChangePassword", "1"));
        }

        // The tenant claim is the only source of tenant identity for the rest of the session.
        // Platform staff have none. Disabled/terminated tenants cannot sign in; suspended ones can
        // (they are limited to billing by TenantAccessMiddleware).
        var warehouses = new List<Models.Inventory.Warehouse>();
        if (user.TenantId is int tenantId)
        {
            var tenantStatus = await db.Tenants.Where(t => t.Id == tenantId).Select(t => (TenantStatus?)t.Status).FirstOrDefaultAsync();
            if (tenantStatus is null or TenantStatus.Disabled or TenantStatus.Terminated)
            {
                model.ErrorMessage = "This business account is not active. Contact support.";
                return View(model);
            }

            extraClaims.Add(new Claim(TenantContext.ClaimType, tenantId.ToString()));

            using (tenantContext.UseTenant(tenantId))
            {
                warehouses = await db.UserWarehouses
                    .Where(uw => uw.UserId == user.Id)
                    .Include(uw => uw.Warehouse)
                    .Select(uw => uw.Warehouse!)
                    .ToListAsync();
            }
        }

        foreach (var warehouse in warehouses)
        {
            extraClaims.Add(new Claim("WarehouseId", warehouse.Id.ToString()));
            extraClaims.Add(new Claim("WarehouseName", warehouse.Name));
        }

        await signInManager.SignInWithClaimsAsync(user, model.RememberMe, extraClaims);
        if (UiLanguage.Normalize(user.PreferredLanguage) is { } preferred) UiLanguage.Remember(Response, preferred);

        // Remember which business this browser belongs to, so the next sign-in page already shows its name and logo. Only the code is stored.
        if (await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.Code).FirstOrDefaultAsync() is { } tenantCode)
        {
            Response.Cookies.Append(LastTenantCookie, tenantCode, new CookieOptions
            {
                HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddDays(180)
            });
        }

        // Presence for Users > Active Users.
        var now = DateTime.UtcNow;
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, now).SetProperty(u => u.LastActivityAt, now));
        UserActivityMiddleware.MarkSeen(user.Id, now);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var wasPlatformStaff = User.IsPlatformStaff();
        if (userId is not null)
        {
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActivityAt, (DateTime?)null));
            UserActivityMiddleware.Forget(userId);
        }

        await signInManager.SignOutAsync();
        // Platform staff go back to the platform console sign-in, never to a business page.
        return wasPlatformStaff ? Redirect("/Platform/Login") : RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ChangePassword()
    {
        if (!signInManager.IsSignedIn(User))
        {
            return RedirectToAction(nameof(Login));
        }

        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!signInManager.IsSignedIn(User))
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        if (user.MustChangePassword)
        {
            // A temporary password was in use: clear the flag and make the user sign in again with the new one.
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
            await signInManager.SignOutAsync();
            TempData["Success"] = "Password changed. Sign in with your new password.";
            return RedirectToAction(nameof(Login));
        }

        await signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Your password has been changed.";
        return RedirectToAction("Index", "Home");
    }

    private const string ResetView = "~/Views/PasswordReset/{0}.cshtml";

    private IActionResult ResetPage(string name, object? model = null)
    {
        ViewData["Badge"] = "Business sign-in";
        ViewData["BackUrl"] = Url.Action(nameof(Login));
        return View(string.Format(ResetView, name), model);
    }

    [HttpGet]
    public IActionResult ForgotPassword() => ResetPage("ForgotPassword", new ForgotPasswordViewModel());

    [HttpPost]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return ResetPage("ForgotPassword", model);
        }

        // Same response whether or not the account exists, so the form can't be used to probe emails.
        // Business users only: platform staff reset on their own page.
        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is { IsActive: true, TenantId: not null })
        {
            await passwordReset.SendLinkAsync(user, token => Url.Action(nameof(ResetPassword), "Account",
                new { email = user.Email, token }, Request.Scheme)!);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => ResetPage("ForgotPasswordConfirmation");

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return RedirectToAction(nameof(Login));
        }

        return ResetPage("ResetPassword", new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return ResetPage("ResetPassword", model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not { IsActive: true, TenantId: not null })
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));   // don't reveal that the account doesn't exist
        }

        var result = await passwordReset.ResetAsync(user, model.Token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return ResetPage("ResetPassword", model);
        }

        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => ResetPage("ResetPasswordConfirmation");

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
