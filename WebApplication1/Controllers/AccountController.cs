using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Middleware;
using WebApplication1.Models.Identity;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

[AllowAnonymous]
public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (signInManager.IsSignedIn(User))
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is null || !user.IsActive)
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

        var extraClaims = new List<Claim>
        {
            new("FullName", user.FullName)
        };

        var warehouses = await db.UserWarehouses
            .Where(uw => uw.UserId == user.Id)
            .Include(uw => uw.Warehouse)
            .Select(uw => uw.Warehouse!)
            .ToListAsync();

        foreach (var warehouse in warehouses)
        {
            extraClaims.Add(new Claim("WarehouseId", warehouse.Id.ToString()));
            extraClaims.Add(new Claim("WarehouseName", warehouse.Name));
        }

        await signInManager.SignInWithClaimsAsync(user, model.RememberMe, extraClaims);

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
        if (userId is not null)
        {
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActivityAt, (DateTime?)null));
            UserActivityMiddleware.Forget(userId);
        }

        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
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

        await signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Your password has been changed.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
