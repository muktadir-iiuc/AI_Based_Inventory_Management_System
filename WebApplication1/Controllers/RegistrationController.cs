using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

/// <summary>Self-service sign-up: a business registers, gets its own tenant and starts the configured trial plan.</summary>
[AllowAnonymous]
public class RegistrationController(
    ApplicationDbContext db,
    ITenantProvisioningService provisioning,
    IDataAgreementService dataAgreement,
    IPlatformSettings settings,
    ILogger<RegistrationController> logger) : Controller
{
    [HttpGet("/Register")]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return await IsOpenAsync() ? View(new RegisterViewModel()) : View("Closed");
    }

    [HttpPost("/Register")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("register")]
    public async Task<IActionResult> Index(RegisterViewModel model)
    {
        if (!await IsOpenAsync()) return View("Closed");

        // Bots fill every field, including the hidden one. Pretend success so they learn nothing.
        if (!string.IsNullOrEmpty(model.Website))
        {
            return RedirectToAction("Login", "Account");
        }
        if (!model.AcceptTerms) ModelState.AddModelError(nameof(model.AcceptTerms), "You must accept the terms to continue.");
        if (!ModelState.IsValid) return View(model);

        var planCode = await settings.GetAsync(PlatformSettingKeys.RegistrationPlanCode);
        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == planCode && p.Status == PlanStatus.Active);
        if (plan is null)
        {
            logger.LogError("Registration plan '{Code}' is missing or not active.", planCode);
            return View("Closed");
        }

        // Plans with a trial start free; a paid plan starts with an invoice to pay first.
        var mode = plan.TrialDays > 0 ? StartMode.Trial : StartMode.InvoiceFirst;
        var result = await provisioning.CreateAsync(new TenantCreateRequest(model.BusinessName, model.OwnerName, model.Email, model.Password, plan.Id, mode, ContactPhone: model.Phone), "self-registration");
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e);
            return View(model);
        }

        if (result.Tenant is { } created)
        {
            await dataAgreement.AcceptAsync(created.Id, "Registration", created.OwnerUserId, model.Email);
            await db.SaveChangesAsync();
        }

        TempData["Success"] = mode == StartMode.Trial
            ? $"Welcome! Your {plan.TrialDays}-day free trial has started. Sign in with {model.Email}."
            : "Your account was created. Sign in to review and pay your first invoice.";
        return RedirectToAction("Login", "Account");
    }

    private async Task<bool> IsOpenAsync() => string.Equals(await settings.GetAsync(PlatformSettingKeys.RegistrationEnabled), "true", StringComparison.OrdinalIgnoreCase);
}
