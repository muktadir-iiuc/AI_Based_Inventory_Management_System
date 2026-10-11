using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Saas.Payments;
using Microsoft.AspNetCore.RateLimiting;

namespace WebApplication1.Controllers;

/// <summary>
/// The tenant's own billing portal. Every query is bound to the signed-in user's TenantId claim - an id
/// in the URL is only ever used together with it, so one tenant can never read or change another's billing.
/// Reachable while the subscription is suspended or expired (see TenantAccessMiddleware).
/// </summary>
[Authorize]
public class BillingController(
    ApplicationDbContext db,
    ISubscriptionService subscriptions,
    IEntitlementService entitlements,
    IBillingNoticeService notices,
    ISaasInvoicePdfService invoicePdf,
    IPaymentGatewayService gateway,
    IBillingPaymentService billingPayments,
    IPaymentProviderRegistry providers,
    IPlatformSettings platformSettings,
    IDataAgreementService dataAgreement,
    IConfiguration configuration,
    ISaasAudit audit) : Controller
{
    private int TenantId => User.GetTenantId();
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Anyone in the tenant sees why they are restricted; only admins manage billing.
    public async Task<IActionResult> Restricted()
    {
        var access = await subscriptions.GetAccessAsync(TenantId);
        if (access.IsFull) return RedirectToAction("Index", "Home");
        return View(access);
    }

    public IActionResult FeatureLocked(string? what) => View(model: what);
    public IActionResult LimitReached(string? what) => View(model: what);

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Index()
    {
        ViewData["AgreementPending"] = !await dataAgreement.HasAcceptedCurrentAsync(TenantId);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == TenantId);
        var sub = await db.TenantSubscriptions.AsNoTracking().Include(s => s.Plan).FirstOrDefaultAsync(s => s.TenantId == TenantId);
        var model = new BillingIndexViewModel
        {
            Tenant = tenant,
            Subscription = sub,
            Plan = sub?.Plan,
            PendingPlan = sub?.PendingPlanId is int pid ? await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid) : null,
            Access = await subscriptions.GetAccessAsync(TenantId),
            Limits = await subscriptions.GetLimitsAsync(TenantId),
            Usage = await entitlements.GetUsageAsync(),
            Features = await subscriptions.GetFeaturesAsync(TenantId),
            OutstandingInvoices = await db.SaasInvoices.AsNoTracking()
                .Where(i => i.TenantId == TenantId && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Overdue))
                .OrderBy(i => i.DueDate).ToListAsync(),
            Notifications = await db.SaasNotifications.AsNoTracking()
                .Where(n => n.TenantId == TenantId && n.Channel == NotificationChannel.InApp)
                .OrderByDescending(n => n.CreatedAt).Take(8).ToListAsync(),
            IsOwner = tenant.OwnerUserId == UserId
        };
        return View(model);
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Invoices()
    {
        var list = await db.SaasInvoices.AsNoTracking().Where(i => i.TenantId == TenantId).OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id).ToListAsync();
        return View(list);
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> InvoiceDetails(int id)
    {
        var invoice = await db.SaasInvoices.AsNoTracking().Include(i => i.Items).Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == TenantId);
        if (invoice is null) return NotFound();

        // Payment options: online providers that are switched on AND have credentials, plus manual bank/cash reporting.
        var configs = await db.PaymentProviders.AsNoTracking().Where(p => p.IsEnabled).OrderBy(p => p.SortOrder).ToListAsync();
        var online = configs.Where(c => c.Code != "manual" && providers.Find(c.Code) is { IsConfigured: true }).ToList();
        ViewData["OnlineProviders"] = online;
        ViewData["ManualEnabled"] = configs.Any(c => c.Code == "manual");
        ViewData["ManualInstructions"] = await platformSettings.GetAsync(PlatformSettingKeys.ManualPaymentInstructions);
        ViewData["RecentAttempts"] = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.InvoiceId == invoice.Id && a.TenantId == TenantId).OrderByDescending(a => a.CreatedAt).Take(5).ToListAsync();
        return View(invoice);
    }

    // ---------------------------------------------------------------- paying

    private static readonly string[] ManualMethods = ["BankTransfer", "Cash", "bKash", "Nagad", "Rocket"];

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin), EnableRateLimiting("payment-start")]
    public async Task<IActionResult> Pay(int invoiceId, string provider)
    {
        // Public URL the gateway calls back. Behind a proxy/CDN set Payments:PublicBaseUrl explicitly.
        var baseUrl = configuration["Payments:PublicBaseUrl"] is { Length: > 0 } configured ? configured : $"{Request.Scheme}://{Request.Host}";
        var result = await gateway.StartAsync(TenantId, invoiceId, provider, baseUrl, User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (!result.Succeeded || result.RedirectUrl is null)
        {
            TempData["Error"] = result.Error ?? "Could not start the payment.";
            return RedirectToAction(nameof(InvoiceDetails), new { id = invoiceId });
        }
        return Redirect(result.RedirectUrl);
    }

    /// <summary>Customer reports a bank transfer / cash / mobile-wallet payment. It only counts once our team confirms it.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin), EnableRateLimiting("payment-start")]
    public async Task<IActionResult> SubmitManualPayment(int invoiceId, string method, string? reference, decimal amount, string? note)
    {
        var invoice = await db.SaasInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == TenantId);
        if (invoice is null) return NotFound();

        string? error = null;
        if (!ManualMethods.Contains(method)) error = "Choose a payment method.";
        else if (method != "Cash" && (reference?.Trim().Length ?? 0) < 3) error = "Enter the transaction / transfer reference.";
        else if (amount <= 0 || amount > invoice.Total - invoice.AmountPaid) error = "Enter an amount between 0 and the outstanding balance.";
        else if (await db.SaasPayments.CountAsync(p => p.InvoiceId == invoice.Id && p.Status == SaasPaymentStatus.AwaitingConfirmation) >= 3)
            error = "You already have payments waiting for confirmation on this invoice.";

        if (error is null)
        {
            var result = await billingPayments.ApplyPaymentAsync(new PaymentRequest(invoice.Id, amount, invoice.Currency, method, "manual",
                Reference: reference?.Trim(), Note: note?.Trim(), ExpectedTenantId: TenantId, RequiresConfirmation: true));
            if (result.Outcome == PaymentOutcome.AwaitingConfirmation)
            {
                TempData["Success"] = "Payment reported. We will confirm it shortly and your invoice will update automatically.";
                return RedirectToAction(nameof(InvoiceDetails), new { id = invoiceId });
            }
            error = result.Message;
        }

        TempData["Error"] = error;
        return RedirectToAction(nameof(InvoiceDetails), new { id = invoiceId });
    }

    /// <summary>Where the customer lands after the gateway. Shows our verified record - the redirect itself proves nothing.</summary>
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> PaymentResult(string reference)
    {
        var attempt = await db.PaymentAttempts.AsNoTracking().Include(a => a.Invoice)
            .FirstOrDefaultAsync(a => a.Reference == reference && a.TenantId == TenantId);
        return attempt is null ? NotFound() : View(attempt);
    }

    /// <summary>The invoice as a PDF in the same layout family as the sales invoice. Only the tenant's own invoices.</summary>
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> InvoicePdf(int id)
    {
        var invoice = await db.SaasInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.TenantId == TenantId);
        if (invoice is null) return NotFound();

        var bytes = await invoicePdf.RenderAsync(invoice.Id);
        Response.Headers.ContentDisposition = $"inline; filename=\"{invoice.InvoiceNumber}.pdf\"";
        return File(bytes, "application/pdf");
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Payments()
    {
        var list = await db.SaasPayments.AsNoTracking().Include(p => p.Invoice)
            .Where(p => p.TenantId == TenantId).OrderByDescending(p => p.ReceivedAt).ToListAsync();
        return View(list);
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Plans()
    {
        ViewData["NeedsAgreement"] = !await dataAgreement.HasAcceptedCurrentAsync(TenantId);
        var now = DateTime.UtcNow;
        var plans = await db.SubscriptionPlans.AsNoTracking().Include(p => p.Features)
            .Where(p => p.IsPublic && p.Status == PlanStatus.Active && p.Price > 0)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Price).ToListAsync();
        return View(new BillingPlansViewModel
        {
            Subscription = await db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == TenantId),
            Plans = plans.Where(p => p.IsAvailable(now)).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ChoosePlan(int planId, bool acceptDataAgreement)
    {
        // Subscribing means accepting the data agreement (once per version; the acceptance is stored with the subscription).
        if (!await dataAgreement.HasAcceptedCurrentAsync(TenantId))
        {
            if (!acceptDataAgreement)
            {
                TempData["Error"] = "Please accept the data agreement to continue.";
                return RedirectToAction(nameof(Plans));
            }
            var subId = await db.TenantSubscriptions.Where(s => s.TenantId == TenantId).Select(s => (int?)s.Id).FirstOrDefaultAsync();
            await dataAgreement.AcceptAsync(TenantId, "ChoosePlan", UserId, User.Identity?.Name, subId);
            audit.Add("DataAgreement.Accepted", "Tenant", TenantId.ToString(), TenantId, details: "version " + DataAgreement.Version + " (plan selection)");
            await db.SaveChangesAsync();
        }

        try
        {
            var invoice = await subscriptions.RequestPlanChangeAsync(TenantId, planId, User.Identity?.Name);
            if (invoice is null)
            {
                TempData["Success"] = "Your plan change is scheduled for the next renewal.";
                return RedirectToAction(nameof(Index));
            }
            TempData["Success"] = $"Invoice {invoice.InvoiceNumber} was created. Pay it to activate the plan.";
            return RedirectToAction(nameof(InvoiceDetails), new { id = invoice.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Plans));
        }
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Cancel(bool immediate, bool requestData)
    {
        // Cancelling is an owner decision, not any administrator's.
        var ownerId = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.OwnerUserId).FirstOrDefaultAsync();
        if (ownerId != UserId)
        {
            TempData["Error"] = "Only the account owner can cancel the subscription.";
            return RedirectToAction(nameof(Index));
        }

        await subscriptions.CancelAsync(TenantId, immediate, User.Identity?.Name);
        if (requestData && !await db.DataExportRequests.AnyAsync(r => r.TenantId == TenantId && r.Status == ExportStatus.Requested))
        {
            // Leaving: the business asked for its data together with the cancellation (CSV by default; it can pick another format on the Data page).
            db.DataExportRequests.Add(new DataExportRequest { TenantId = TenantId, Format = ExportFormat.Csv, Reason = ExportReason.CancelPlan, RequestedByUserId = UserId, RequestedBy = User.Identity?.Name });
            audit.Add("DataExport.Requested", "DataExportRequest", null, TenantId, details: "Csv, CancelPlan (with cancellation)");
            await db.SaveChangesAsync();
        }
        TempData["Success"] = (immediate ? "Subscription cancelled." : "Subscription will end at the end of the paid term and will not renew.")
            + (requestData ? " Your data request was sent - download it from Data & agreement once it is ready." : "");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Resume()
    {
        try
        {
            await subscriptions.ResumeAsync(TenantId, User.Identity?.Name);
            TempData["Success"] = "Subscription renewal resumed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Profile()
    {
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == TenantId);
        return View(new BillingProfileViewModel
        {
            Name = t.Name, ContactName = t.ContactName, ContactEmail = t.ContactEmail, BillingEmail = t.BillingEmail,
            ContactPhone = t.ContactPhone, Address = t.Address, City = t.City, TaxId = t.TaxId
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Profile(BillingProfileViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var t = await db.Tenants.FirstAsync(x => x.Id == TenantId);
        t.Name = model.Name.Trim();
        t.ContactName = model.ContactName;
        t.ContactEmail = model.ContactEmail;
        t.BillingEmail = model.BillingEmail;
        t.ContactPhone = model.ContactPhone;
        t.Address = model.Address;
        t.City = model.City;
        t.TaxId = model.TaxId;
        t.UpdatedAt = DateTime.UtcNow;
        audit.Add("tenant.billing-profile-updated", nameof(Models.Tenancy.Tenant), t.Id.ToString(), t.Id);
        await db.SaveChangesAsync();
        TempData["Success"] = "Billing details updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> MarkNotificationsRead()
    {
        var unread = await db.SaasNotifications.Where(n => n.TenantId == TenantId && n.Channel == NotificationChannel.InApp && n.ReadAt == null).ToListAsync();
        foreach (var n in unread) n.ReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
