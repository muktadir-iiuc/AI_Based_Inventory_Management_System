using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

/// <summary>Plans are data: platform administrators create and change them here, no code change needed. Existing subscribers keep the terms they bought.</summary>
[Route("Platform/Plans")]
[Authorize(Roles = Roles.PlatformAdmin)]
public class PlatformPlansController(ApplicationDbContext db, ISaasAudit audit) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var plans = await db.SubscriptionPlans.AsNoTracking().Include(p => p.Features).OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync();
        ViewData["Subscribers"] = await db.TenantSubscriptions.AsNoTracking().GroupBy(s => s.PlanId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        return View(plans);
    }

    [HttpGet("Create")]
    public IActionResult Create() => View("Edit", new PlanEditViewModel { FeatureKeys = Features.All.Select(f => f.Key).ToList() });

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await db.SubscriptionPlans.AsNoTracking().Include(x => x.Features).FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        ViewData["Subscribers"] = await db.TenantSubscriptions.CountAsync(s => s.PlanId == id);
        return View(new PlanEditViewModel
        {
            Id = p.Id, Code = p.Code, Name = p.Name, Description = p.Description, Price = p.Price, Currency = p.Currency,
            IntervalCount = p.IntervalCount, IntervalUnit = p.IntervalUnit, TrialDays = p.TrialDays, GraceDays = p.GraceDays,
            TaxPercent = p.TaxPercent, DiscountPercent = p.DiscountPercent, MaxUsers = p.MaxUsers, MaxProducts = p.MaxProducts,
            MaxWarehouses = p.MaxWarehouses, MaxStorageMb = p.MaxStorageMb, AutoRenew = p.AutoRenew, IsPublic = p.IsPublic, Status = p.Status,
            AvailableFrom = p.AvailableFrom, AvailableTo = p.AvailableTo, SortOrder = p.SortOrder, FeatureKeys = p.Features.Select(f => f.FeatureKey).ToList()
        });
    }

    [HttpPost("Save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(PlanEditViewModel model)
    {
        model.Currency = (model.Currency ?? "").Trim().ToUpperInvariant();
        model.Code = (model.Code ?? "").Trim().ToLowerInvariant();
        model.FeatureKeys = model.FeatureKeys.Where(Features.IsKnown).Distinct().ToList();
        if (model.AvailableFrom is not null && model.AvailableTo is not null && model.AvailableTo < model.AvailableFrom)
        {
            ModelState.AddModelError(nameof(model.AvailableTo), "End date must be after the start date.");
        }
        if (await db.SubscriptionPlans.AnyAsync(p => p.Code == model.Code && p.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Code), "That plan code is already used.");
        }
        if (!ModelState.IsValid)
        {
            ViewData["Subscribers"] = await db.TenantSubscriptions.CountAsync(s => s.PlanId == model.Id);
            return View("Edit", model);
        }

        SubscriptionPlan plan;
        var isNew = model.Id == 0;
        if (isNew)
        {
            plan = new SubscriptionPlan();
            db.SubscriptionPlans.Add(plan);
        }
        else
        {
            plan = await db.SubscriptionPlans.Include(p => p.Features).FirstOrDefaultAsync(p => p.Id == model.Id) ?? throw new InvalidOperationException("Plan not found.");
            plan.UpdatedAt = DateTime.UtcNow;
        }

        plan.Code = model.Code; plan.Name = model.Name.Trim(); plan.Description = model.Description; plan.Price = BillingMath.Round(model.Price);
        plan.Currency = model.Currency; plan.IntervalCount = model.IntervalCount; plan.IntervalUnit = model.IntervalUnit; plan.TrialDays = model.TrialDays;
        plan.GraceDays = model.GraceDays; plan.TaxPercent = model.TaxPercent; plan.DiscountPercent = model.DiscountPercent;
        plan.MaxUsers = model.MaxUsers; plan.MaxProducts = model.MaxProducts; plan.MaxWarehouses = model.MaxWarehouses; plan.MaxStorageMb = model.MaxStorageMb;
        plan.AutoRenew = model.AutoRenew; plan.IsPublic = model.IsPublic; plan.Status = model.Status;
        plan.AvailableFrom = model.AvailableFrom?.ToUniversalTime(); plan.AvailableTo = model.AvailableTo?.ToUniversalTime(); plan.SortOrder = model.SortOrder;

        plan.Features.Where(f => !model.FeatureKeys.Contains(f.FeatureKey)).ToList().ForEach(f => db.PlanFeatures.Remove(f));
        foreach (var key in model.FeatureKeys.Where(k => plan.Features.All(f => f.FeatureKey != k)))
        {
            plan.Features.Add(new PlanFeature { FeatureKey = key });
        }

        audit.Add(isNew ? "plan.created" : "plan.updated", nameof(SubscriptionPlan), plan.Code, details: $"{plan.Name}: {plan.Price:0.00} {plan.Currency} every {plan.IntervalCount} {plan.IntervalUnit}, status {plan.Status}, features {string.Join('/', model.FeatureKeys)}");
        await db.SaveChangesAsync();
        TempData["Success"] = isNew ? "Plan created." : "Plan saved. Existing subscribers keep their current terms; changes apply to new subscriptions and plan changes.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/Status"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, PlanStatus status)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null) return NotFound();
        plan.Status = status;
        plan.UpdatedAt = DateTime.UtcNow;
        audit.Add("plan.status-changed", nameof(SubscriptionPlan), plan.Code, details: $"status {status}");
        await db.SaveChangesAsync();
        TempData["Success"] = $"Plan {plan.Name} is now {status}.";
        return RedirectToAction(nameof(Index));
    }
}
