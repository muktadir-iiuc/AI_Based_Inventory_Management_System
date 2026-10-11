using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Controllers;

[Route("Platform/Tenants")]
public class PlatformTenantsController(
    ApplicationDbContext db,
    ITenantContext tenantContext,
    UserManager<ApplicationUser> userManager,
    ISubscriptionService subscriptions,
    IEntitlementService entitlements,
    ITenantProvisioningService provisioning,
    ITenantRetentionService retention,
    ITenantPortalService portal,
    ISaasAudit audit) : PlatformControllerBase
{
    // ---------------------------------------------------------------- list

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? status, string? sub, string sort = "created", int page = 1)
    {
        var query = db.Tenants.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t => t.Name.Contains(term) || t.Code.Contains(term) || (t.ContactEmail != null && t.ContactEmail.Contains(term)));
        }
        if (Enum.TryParse<TenantStatus>(status, out var ts)) query = query.Where(t => t.Status == ts);
        if (Enum.TryParse<SubscriptionStatus>(sub, out var ss))
        {
            query = query.Where(t => db.TenantSubscriptions.Any(s => s.TenantId == t.Id && s.Status == ss));
        }

        query = sort switch
        {
            "name" => query.OrderBy(t => t.Name),
            "name_desc" => query.OrderByDescending(t => t.Name),
            "status" => query.OrderBy(t => t.Status).ThenBy(t => t.Name),
            "oldest" => query.OrderBy(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };

        var paged = await query.ToPagedAsync(page, PageSize);
        var ids = paged.Items.Select(t => t.Id).ToList();
        var subs = await db.TenantSubscriptions.AsNoTracking().Include(s => s.Plan).Where(s => ids.Contains(s.TenantId)).ToDictionaryAsync(s => s.TenantId);
        var userCounts = await db.Users.AsNoTracking().Where(u => u.TenantId != null && ids.Contains(u.TenantId.Value))
            .GroupBy(u => u.TenantId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);

        return View(new TenantListViewModel
        {
            Q = q, Status = status, SubStatus = sub, Sort = sort,
            Page = new PagedResult<TenantListItem>
            {
                Page = paged.Page, PageSize = paged.PageSize, Total = paged.Total,
                Items = paged.Items.Select(t => new TenantListItem
                {
                    Tenant = t, Subscription = subs.GetValueOrDefault(t.Id), PlanName = subs.GetValueOrDefault(t.Id)?.Plan?.Name, Users = userCounts.GetValueOrDefault(t.Id)
                }).ToList()
            }
        });
    }

    // ---------------------------------------------------------------- create

    [HttpGet("Create"), Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Create() => View(new TenantCreateViewModel { Plans = await PlansAsync() });

    [HttpPost("Create"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Create(TenantCreateViewModel model)
    {
        model.Plans = await PlansAsync();
        if (!ModelState.IsValid) return View(model);

        var result = await provisioning.CreateAsync(new TenantCreateRequest(model.Name, model.OwnerName, model.OwnerEmail, model.OwnerPassword,
            model.PlanId, model.StartMode, model.Code, model.Phone, model.Address, model.City, model.TaxId), Actor);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e);
            return View(model);
        }

        TempData["Success"] = $"Tenant {result.Tenant!.Name} created. The owner can sign in with {model.OwnerEmail}.";
        return RedirectToAction(nameof(Details), new { id = result.Tenant.Id });
    }

    private Task<List<SubscriptionPlan>> PlansAsync() =>
        db.SubscriptionPlans.AsNoTracking().Where(p => p.Status == PlanStatus.Active).OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync();

    // ---------------------------------------------------------------- details

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();

        var sub = await db.TenantSubscriptions.AsNoTracking().Include(s => s.Plan).FirstOrDefaultAsync(s => s.TenantId == id);

        // Counts only (users / products / warehouses) - never the tenant's business records.
        UsageSnapshot usage;
        using (tenantContext.UseTenant(id))
        {
            usage = await entitlements.GetUsageAsync();
        }

        var users = await db.Users.AsNoTracking().Where(u => u.TenantId == id).OrderBy(u => u.FullName).ToListAsync();
        var userRows = new List<TenantUserRow>();
        foreach (var u in users)
        {
            userRows.Add(new TenantUserRow { User = u, Roles = (await userManager.GetRolesAsync(u)).ToList(), IsOwner = u.Id == tenant.OwnerUserId });
        }

        return View(new TenantDetailsViewModel
        {
            Tenant = tenant, Subscription = sub, Plan = sub?.Plan, AllPlans = await PlansAsync(),
            Access = await subscriptions.GetAccessAsync(id), Limits = await subscriptions.GetLimitsAsync(id), Usage = usage,
            PlanFeatures = sub is null ? [] : (await db.PlanFeatures.AsNoTracking().Where(f => f.PlanId == sub.PlanId).Select(f => f.FeatureKey).ToListAsync()).ToHashSet(),
            FeatureOverrides = await db.TenantFeatureOverrides.AsNoTracking().Where(o => o.TenantId == id).ToDictionaryAsync(o => o.FeatureKey, o => o.IsEnabled),
            Invoices = await db.SaasInvoices.AsNoTracking().Where(i => i.TenantId == id).OrderByDescending(i => i.IssueDate).Take(20).ToListAsync(),
            Payments = await db.SaasPayments.AsNoTracking().Include(p => p.Invoice).Where(p => p.TenantId == id).OrderByDescending(p => p.ReceivedAt).Take(20).ToListAsync(),
            History = await db.SubscriptionHistories.AsNoTracking().Where(h => h.TenantId == id).OrderByDescending(h => h.CreatedAt).Take(30).ToListAsync(),
            Audit = await db.SaasAuditLogs.AsNoTracking().Where(a => a.TenantId == id).OrderByDescending(a => a.Id).Take(30).ToListAsync(),
            Users = userRows, PurgeEligibleOn = await retention.GetPurgeEligibleOnAsync(id), IsAdmin = IsPlatformAdmin,
            Links = await portal.GetLinksAsync(tenant, $"{Request.Scheme}://{Request.Host}")
        });
    }

    // ---------------------------------------------------------------- edit profile (admin)

    [HttpGet("{id:int}/Edit"), Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Edit(int id)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        return View(new TenantProfileViewModel
        {
            Name = t.Name, ContactName = t.ContactName, ContactEmail = t.ContactEmail, BillingEmail = t.BillingEmail, ContactPhone = t.ContactPhone,
            Address = t.Address, City = t.City, Country = t.Country, TaxId = t.TaxId
        });
    }

    [HttpPost("{id:int}/Edit"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Edit(int id, TenantProfileViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        t.Name = model.Name.Trim(); t.ContactName = model.ContactName; t.ContactEmail = model.ContactEmail; t.BillingEmail = model.BillingEmail;
        t.ContactPhone = model.ContactPhone; t.Address = model.Address; t.City = model.City; t.Country = model.Country; t.TaxId = model.TaxId;
        t.UpdatedAt = DateTime.UtcNow;
        audit.Add("tenant.updated", nameof(Tenant), id.ToString(), id, details: "profile edited by platform admin");
        await db.SaveChangesAsync();
        TempData["Success"] = "Tenant updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------- lifecycle actions (admin only, audited in the services)

    private async Task<IActionResult> Run(int id, Func<Task> action, string success)
    {
        try
        {
            await action();
            TempData["Success"] = success;
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Suspend"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Suspend(int id, string reason) =>
        string.IsNullOrWhiteSpace(reason) ? Task.FromResult(Fail(id, "A reason is required.")) : Run(id, () => subscriptions.SuspendAsync(id, reason.Trim(), Actor), "Tenant suspended. Payments will not lift this suspension.");

    [HttpPost("{id:int}/Reactivate"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Reactivate(int id) => Run(id, () => subscriptions.ReactivateAsync(id, Actor), "Administrative suspension lifted.");

    [HttpPost("{id:int}/Disable"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Disable(int id, string reason) =>
        string.IsNullOrWhiteSpace(reason) ? Task.FromResult(Fail(id, "A reason is required.")) : Run(id, () => subscriptions.DisableAsync(id, reason.Trim(), Actor), "Tenant disabled. Its users can no longer sign in.");

    [HttpPost("{id:int}/Enable"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Enable(int id) => Run(id, () => subscriptions.EnableAsync(id, Actor), "Tenant enabled.");

    [HttpPost("{id:int}/Terminate"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Terminate(int id, string reason, string confirmCode)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();
        if (!string.Equals(confirmCode?.Trim(), tenant.Code, StringComparison.Ordinal)) return Fail(id, "Type the tenant code exactly to confirm termination.");
        if (string.IsNullOrWhiteSpace(reason)) return Fail(id, "A reason is required.");
        return await Run(id, () => subscriptions.TerminateAsync(id, reason.Trim(), Actor), "Tenant terminated. Data is kept until the retention period ends.");
    }

    [HttpPost("{id:int}/Purge"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Purge(int id, string confirmCode) =>
        Run(id, () => retention.PurgeAsync(id, confirmCode, Actor), "Business data permanently erased. Financial records were kept.");

    private IActionResult Fail(int id, string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------- subscription administration (admin)

    [HttpPost("{id:int}/Subscribe"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Subscribe(int id, int planId, StartMode mode) =>
        Run(id, async () => await subscriptions.StartAsync(id, planId, mode, Actor), "Subscription started.");

    [HttpPost("{id:int}/AssignPlan"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> AssignPlan(int id, int planId) =>
        Run(id, () => subscriptions.AssignPlanNowAsync(id, planId, Actor), "Plan changed immediately (no invoice raised).");

    [HttpPost("{id:int}/Terms"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Terms(int id, decimal price, int intervalCount, BillingIntervalUnit unit, int graceDays, decimal taxPercent, decimal discountPercent, bool autoRenew) =>
        Run(id, () => subscriptions.SetCustomTermsAsync(id, price, intervalCount, unit, graceDays, taxPercent, discountPercent, autoRenew, Actor), "Billing terms updated for this tenant.");

    [HttpPost("{id:int}/Trial"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Trial(int id, DateTime trialEnds) =>
        Run(id, () => subscriptions.SetTrialEndAsync(id, trialEnds.ToUniversalTime(), Actor), "Trial end updated.");

    [HttpPost("{id:int}/Limits"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Limits(int id, int? users, int? products, int? warehouses, int? storageMb) =>
        Run(id, () => subscriptions.SetLimitOverridesAsync(id, users, products, warehouses, storageMb, Actor), "Limits updated (blank = use the plan's limit).");

    [HttpPost("{id:int}/Features"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> FeatureOverrides(int id) =>
        Run(id, async () =>
        {
            foreach (var (key, _) in Features.All)
            {
                // "default" | "on" | "off" per feature.
                bool? value = Request.Form[$"f_{key}"].ToString() switch { "on" => true, "off" => false, _ => null };
                await subscriptions.SetFeatureOverrideAsync(id, key, value, Actor);
            }
        }, "Feature access updated.");

    [HttpPost("{id:int}/Cancel"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Cancel(int id, bool immediate) =>
        Run(id, () => subscriptions.CancelAsync(id, immediate, Actor), immediate ? "Subscription cancelled." : "Subscription will end at the end of the paid term.");

    [HttpPost("{id:int}/Resume"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public Task<IActionResult> Resume(int id) => Run(id, () => subscriptions.ResumeAsync(id, Actor), "Renewal resumed.");

    // ---------------------------------------------------------------- owner and administrators (admin)

    [HttpPost("{id:int}/Owner"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Owner(int id, string userId)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == id);   // must be one of THIS tenant's users
        if (tenant is null || user is null || !await userManager.IsInRoleAsync(user, Roles.Admin)) return Fail(id, "Choose one of the tenant's administrators.");
        tenant.OwnerUserId = user.Id;
        tenant.UpdatedAt = DateTime.UtcNow;
        audit.Add("tenant.owner-changed", nameof(Tenant), id.ToString(), id, details: $"owner is now {user.Email}");
        await db.SaveChangesAsync();
        TempData["Success"] = "Owner updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------- access panel: portal URL and user credentials (admin only)

    [HttpPost("{id:int}/PortalUrl"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> PortalUrl(int id, string? url)
    {
        var error = await portal.SetPortalUrlAsync(id, url, Actor);
        if (error is not null) return Fail(id, error);
        TempData["Success"] = string.IsNullOrWhiteSpace(url) ? "Portal URL removed." : "Portal URL saved. Point that domain at this application (DNS + reverse proxy); sign-in there is limited to this tenant.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<ApplicationUser?> TenantUserAsync(int id, string userId) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == id);   // a user of THIS tenant only - never a platform account or another tenant's

    /// <summary>
    /// Sets a new password for one of the tenant's users: the admin's own text, or a generated one. The user must replace it at
    /// their next sign-in. The password is shown once on the response page and is never stored, logged or put in the audit trail.
    /// </summary>
    [HttpPost("{id:int}/Users/{userId}/ResetPassword"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> ResetPassword(int id, string userId, string? newPassword)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        var user = await TenantUserAsync(id, userId);
        if (tenant is null || user is null) return NotFound();

        var password = string.IsNullOrWhiteSpace(newPassword) ? PasswordGenerator.Create() : newPassword;
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded) return Fail(id, string.Join(" ", result.Errors.Select(e => e.Description)));

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.UpdateSecurityStampAsync(user);   // signs the user out everywhere
        audit.Add("tenant.user-password-reset", nameof(ApplicationUser), user.Id, id, details: $"password reset for {user.Email}");
        await db.SaveChangesAsync();

        var links = await portal.GetLinksAsync(tenant, $"{Request.Scheme}://{Request.Host}");
        Response.Headers.CacheControl = "no-store";
        return View("PasswordReset", new PasswordResetViewModel(id, tenant.Name, user.Email ?? user.UserName ?? "", password, tenant.PortalUrl ?? links.TenantSignIn));
    }

    [HttpPost("{id:int}/Users/{userId}/Toggle"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> ToggleUser(int id, string userId)
    {
        var user = await TenantUserAsync(id, userId);
        if (user is null) return NotFound();
        user.IsActive = !user.IsActive;
        await userManager.UpdateAsync(user);
        if (!user.IsActive) await userManager.UpdateSecurityStampAsync(user);
        audit.Add("tenant.user-toggled", nameof(ApplicationUser), user.Id, id, details: $"{user.Email} active = {user.IsActive}");
        await db.SaveChangesAsync();
        TempData["Success"] = $"{user.Email} is now {(user.IsActive ? "active" : "deactivated")}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Users/{userId}/Unlock"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> UnlockUser(int id, string userId)
    {
        var user = await TenantUserAsync(id, userId);
        if (user is null) return NotFound();
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        audit.Add("tenant.user-unlocked", nameof(ApplicationUser), user.Id, id, details: user.Email);
        await db.SaveChangesAsync();
        TempData["Success"] = $"{user.Email} unlocked.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Users/{userId}/SignOut"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> SignOutUser(int id, string userId)
    {
        var user = await TenantUserAsync(id, userId);
        if (user is null) return NotFound();
        await userManager.UpdateSecurityStampAsync(user);
        audit.Add("tenant.user-signed-out", nameof(ApplicationUser), user.Id, id, details: user.Email);
        await db.SaveChangesAsync();
        TempData["Success"] = $"{user.Email} will have to sign in again.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Users/{userId}/Email"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> ChangeUserEmail(int id, string userId, string email)
    {
        var user = await TenantUserAsync(id, userId);
        if (user is null) return NotFound();
        email = (email ?? "").Trim();
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)) return Fail(id, "Enter a valid email address.");
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null && existing.Id != user.Id) return Fail(id, "That email address is already used by another account.");

        var old = user.Email;
        var r1 = await userManager.SetEmailAsync(user, email);
        var r2 = r1.Succeeded ? await userManager.SetUserNameAsync(user, email) : r1;
        if (!r2.Succeeded) return Fail(id, string.Join(" ", r2.Errors.Select(e => e.Description)));
        audit.Add("tenant.user-email-changed", nameof(ApplicationUser), user.Id, id, details: $"{old} -> {email}");
        await db.SaveChangesAsync();
        TempData["Success"] = "Sign-in email changed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/AddAdmin"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> AddAdmin(int id, string fullName, string email, string password)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == id)) return NotFound();
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email)) return Fail(id, "Name and email are required.");
        if (!await entitlements.CanAddForTenantAsync(id, LimitKind.Users)) return Fail(id, "The tenant's plan user limit has been reached. Raise the limit first.");

        var user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim(), FullName = fullName.Trim(), EmailConfirmed = true, IsActive = true, TenantId = id };
        var created = await userManager.CreateAsync(user, password ?? string.Empty);
        if (!created.Succeeded) return Fail(id, string.Join(" ", created.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, Roles.Admin);
        audit.Add("tenant.admin-added", nameof(Tenant), id.ToString(), id, details: $"administrator {user.Email} created");
        await db.SaveChangesAsync();
        TempData["Success"] = $"Administrator {user.Email} created.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
