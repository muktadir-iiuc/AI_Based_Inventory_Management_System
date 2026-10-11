using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Saas;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Saas.Payments;

namespace WebApplication1.Controllers;

[Route("Platform/Subscriptions")]
public class PlatformSubscriptionsController(ApplicationDbContext db) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int? planId, string? q, int page = 1)
    {
        var query = db.TenantSubscriptions.AsNoTracking().Include(s => s.Tenant).Include(s => s.Plan).AsQueryable();
        if (Enum.TryParse<SubscriptionStatus>(status, out var st)) query = query.Where(s => s.Status == st);
        if (planId is int pid) query = query.Where(s => s.PlanId == pid);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(s => s.Tenant!.Name.Contains(q.Trim()) || s.Tenant!.Code.Contains(q.Trim()));

        ViewData["Plans"] = await db.SubscriptionPlans.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        ViewData["Status"] = status; ViewData["PlanId"] = planId; ViewData["Q"] = q;
        return View(await query.OrderBy(s => s.CurrentPeriodEnd ?? s.TrialEndsAt).ToPagedAsync(page, PageSize));
    }
}

[Route("Platform/Invoices")]
public class PlatformInvoicesController(ApplicationDbContext db, IInvoiceService invoices, IBillingPaymentService payments, ISaasInvoicePdfService invoicePdf, ISaasAudit audit) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? status, int? tenantId, DateTime? from, DateTime? to, int page = 1)
    {
        var query = db.SaasInvoices.AsNoTracking().Include(i => i.Tenant).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(i => i.InvoiceNumber.Contains(q.Trim()) || i.Tenant!.Name.Contains(q.Trim()));
        if (Enum.TryParse<InvoiceStatus>(status, out var st)) query = query.Where(i => i.Status == st);
        if (tenantId is int tid) query = query.Where(i => i.TenantId == tid);
        if (from is { } f) query = query.Where(i => i.IssueDate >= f.ToUniversalTime());
        if (to is { } t) query = query.Where(i => i.IssueDate < t.ToUniversalTime().AddDays(1));

        return View(new InvoiceListViewModel
        {
            Q = q, Status = status, TenantId = tenantId, From = from, To = to,
            Page = await query.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id).ToPagedAsync(page, PageSize)
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await db.SaasInvoices.AsNoTracking().Include(i => i.Tenant).Include(i => i.Items).Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        ViewData["Audit"] = await db.SaasAuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(SaasInvoice) && a.EntityId == id.ToString()).OrderByDescending(a => a.Id).Take(20).ToListAsync();
        ViewData["Refunds"] = await db.SaasRefunds.AsNoTracking().Where(r => r.InvoiceId == id).OrderByDescending(r => r.CreatedAt).ToListAsync();
        ViewData["IsAdmin"] = IsPlatformAdmin;
        return View(invoice);
    }

    [HttpGet("{id:int}/Pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var invoice = await db.SaasInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        var bytes = await invoicePdf.RenderAsync(id);
        Response.Headers.ContentDisposition = $"inline; filename=\"{invoice.InvoiceNumber}.pdf\"";
        return File(bytes, "application/pdf");
    }

    [HttpPost("{id:int}/Cancel"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> CancelInvoice(int id, string reason)
    {
        try { await invoices.CancelAsync(id, reason, Actor); TempData["Success"] = "Invoice cancelled."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Adjust"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Adjust(int id, string description, decimal amount)
    {
        try { await invoices.AdjustAsync(id, description, amount, Actor); TempData["Success"] = "Invoice adjusted."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Cash or bank money received by our staff. Counts immediately because an authorised person is recording it.</summary>
    [HttpPost("{id:int}/RecordPayment"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(int id, string method, string? reference, decimal amount, string? note)
    {
        var allowed = new[] { "Cash", "BankTransfer", "bKash", "Nagad", "Rocket" };
        if (!allowed.Contains(method)) { TempData["Error"] = "Choose a payment method."; return RedirectToAction(nameof(Details), new { id }); }

        var invoice = await db.SaasInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        var result = await payments.ApplyPaymentAsync(new PaymentRequest(id, amount, invoice.Currency, method, "manual",
            Reference: reference?.Trim(), Note: note?.Trim(), ExpectedTenantId: invoice.TenantId));
        if (result.Outcome == PaymentOutcome.Applied) TempData["Success"] = $"Payment recorded. Receipt {result.Payment?.ReceiptNumber}.";
        else TempData["Error"] = result.Message ?? "Payment was not recorded.";
        return RedirectToAction(nameof(Details), new { id });
    }
}

[Route("Platform/Payments")]
public class PlatformPaymentsController(
    ApplicationDbContext db, IBillingPaymentService payments, IPaymentGatewayService gateway, IRefundService refunds) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, string? provider, string tab = "payments", int page = 1)
    {
        var vm = new PaymentsListViewModel { Tab = tab, IsAdmin = IsPlatformAdmin };
        vm.AwaitingCount = await db.SaasPayments.CountAsync(p => p.Status == SaasPaymentStatus.AwaitingConfirmation);
        vm.ReviewCount = await db.PaymentAttempts.CountAsync(a => a.Status == PaymentAttemptStatus.UnderReview || a.Status == PaymentAttemptStatus.NeedsReview);

        if (tab is "attempts" or "review")
        {
            var q = db.PaymentAttempts.AsNoTracking().AsQueryable();
            if (tab == "review") q = q.Where(a => a.Status == PaymentAttemptStatus.UnderReview || a.Status == PaymentAttemptStatus.NeedsReview);
            else if (Enum.TryParse<PaymentAttemptStatus>(status, out var st)) q = q.Where(a => a.Status == st);
            vm.Attempts = await q.OrderByDescending(a => a.CreatedAt).ToPagedAsync(page, PageSize);
        }
        else
        {
            var q = db.SaasPayments.AsNoTracking().Include(p => p.Invoice).AsQueryable();
            if (tab == "awaiting") q = q.Where(p => p.Status == SaasPaymentStatus.AwaitingConfirmation);
            else if (Enum.TryParse<SaasPaymentStatus>(status, out var st)) q = q.Where(p => p.Status == st);
            if (!string.IsNullOrWhiteSpace(provider)) q = q.Where(p => p.Provider == provider);
            vm.Payments = await q.OrderByDescending(p => p.ReceivedAt).ToPagedAsync(page, PageSize);
        }

        var tenantIds = vm.Payments.Items.Select(p => p.TenantId).Concat(vm.Attempts.Items.Select(a => a.TenantId)).Distinct().ToList();
        vm.TenantNames = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
        ViewData["Status"] = status; ViewData["Provider"] = provider;
        return View(vm);
    }

    [HttpPost("{id:int}/Confirm"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id)
    {
        var r = await payments.ConfirmManualPaymentAsync(id, Actor);
        if (r.Outcome == PaymentOutcome.Applied) TempData["Success"] = "Payment confirmed and applied to the invoice.";
        else TempData["Error"] = r.Message ?? "Could not confirm the payment.";
        return RedirectToAction(nameof(Index), new { tab = "awaiting" });
    }

    [HttpPost("{id:int}/Reject"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string reason)
    {
        try { await payments.RejectManualPaymentAsync(id, reason ?? "", Actor); TempData["Success"] = "Payment rejected; the tenant was notified."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { tab = "awaiting" });
    }

    [HttpPost("Attempts/{id:int}/Release"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Release(int id)
    {
        try
        {
            var r = await gateway.ReleaseHeldAsync(id, Actor);
            if (r.Succeeded) TempData["Success"] = "Held payment released and applied."; else TempData["Error"] = r.Message;
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { tab = "review" });
    }

    [HttpPost("{id:int}/Refund"), ValidateAntiForgeryToken, Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Refund(int id, decimal amount, string reason, int? invoiceId)
    {
        var r = await refunds.RefundAsync(id, amount, reason ?? "", Actor);
        if (r.Succeeded) TempData["Success"] = $"Refund of {amount:0.00} recorded ({r.Refund!.Status})."; else TempData["Error"] = r.Error;
        return invoiceId is int inv ? RedirectToAction("Details", "PlatformInvoices", new { id = inv }) : RedirectToAction(nameof(Index));
    }
}

[Route("Platform/Reports")]
public class PlatformReportsController(IPlatformMetricsService metrics, ApplicationDbContext db) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? year, string view = "monthly")
    {
        var y = year ?? DateTime.UtcNow.Year;
        ViewData["Year"] = y; ViewData["View"] = view;
        ViewData["Revenue"] = view == "yearly"
            ? (await Task.WhenAll(Enumerable.Range(y - 4, 5).Select(yy => metrics.GetRevenueAsync(yy, false)))).SelectMany(r => r).ToList()
            : (await metrics.GetRevenueAsync(y, true)).ToList();
        ViewData["Outstanding"] = await metrics.GetOutstandingAsync();
        var since = DateTime.UtcNow.AddDays(-90);
        ViewData["FailedAttempts"] = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.CreatedAt >= since && (a.Status == PaymentAttemptStatus.Failed || a.Status == PaymentAttemptStatus.Cancelled || a.Status == PaymentAttemptStatus.Expired || a.Status == PaymentAttemptStatus.NeedsReview))
            .OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync();
        ViewData["RejectedPayments"] = await db.SaasAuditLogs.AsNoTracking().Where(a => a.At >= since && a.Action == "payment.rejected").OrderByDescending(a => a.Id).Take(30).ToListAsync();
        var tenantIds = ((List<SaasAuditLog>)ViewData["RejectedPayments"]!).Select(a => a.TenantId).Concat(((List<PaymentAttempt>)ViewData["FailedAttempts"]!).Select(a => (int?)a.TenantId)).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        ViewData["TenantNames"] = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
        return View();
    }

    [HttpGet("Revenue.csv")]
    public async Task<IActionResult> RevenueCsv(int? year)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var sb = new StringBuilder().AppendLine(Csv(["Period", "Collected", "Refunded", "Net", "Payments"]));
        foreach (var r in await metrics.GetRevenueAsync(y, true))
        {
            sb.AppendLine(Csv([r.Period, r.Collected.ToString("0.00"), r.Refunded.ToString("0.00"), r.Net.ToString("0.00"), r.Payments.ToString()]));
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"revenue-{y}.csv");
    }

    [HttpGet("Outstanding.csv")]
    public async Task<IActionResult> OutstandingCsv()
    {
        var sb = new StringBuilder().AppendLine(Csv(["Tenant", "Open invoices", "Overdue invoices", "Balance", "Currency", "Oldest due"]));
        foreach (var r in await metrics.GetOutstandingAsync())
        {
            sb.AppendLine(Csv([r.TenantName, r.Invoices.ToString(), r.OverdueInvoices.ToString(), r.Balance.ToString("0.00"), r.Currency, r.OldestDue.ToString("yyyy-MM-dd")]));
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", "outstanding.csv");
    }
}

[Route("Platform/AuditLogs")]
public class PlatformAuditController(ApplicationDbContext db) : PlatformControllerBase
{
    private IQueryable<SaasAuditLog> Filter(string? act, string? actor, string? result, int? tenantId, DateTime? from, DateTime? to)
    {
        var q = db.SaasAuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(act)) q = q.Where(a => a.Action.Contains(act.Trim()));
        if (!string.IsNullOrWhiteSpace(actor)) q = q.Where(a => a.ActorName != null && a.ActorName.Contains(actor.Trim()));
        if (!string.IsNullOrWhiteSpace(result)) q = q.Where(a => a.Result == result);
        if (tenantId is int t) q = q.Where(a => a.TenantId == t);
        if (from is { } f) q = q.Where(a => a.At >= f.ToUniversalTime());
        if (to is { } e) q = q.Where(a => a.At < e.ToUniversalTime().AddDays(1));
        return q;
    }

    [HttpGet("")]
    // NB: the query key is "act", not "action" - "action" is an MVC route value and would always bind to the action name.
    public async Task<IActionResult> Index(string? act, string? actor, string? result, int? tenantId, DateTime? from, DateTime? to, int page = 1) =>
        View(new AuditListViewModel
        {
            Action = act, Actor = actor, Result = result, TenantId = tenantId, From = from, To = to,
            Page = await Filter(act, actor, result, tenantId, from, to).OrderByDescending(a => a.Id).ToPagedAsync(page, 50)
        });

    [HttpGet("Export.csv"), Authorize(Roles = Roles.PlatformAdmin)]
    public async Task<IActionResult> Export(string? act, string? actor, string? result, int? tenantId, DateTime? from, DateTime? to)
    {
        var rows = await Filter(act, actor, result, tenantId, from, to).OrderByDescending(a => a.Id).Take(10000).ToListAsync();
        var sb = new StringBuilder().AppendLine(Csv(["Time (UTC)", "Actor", "Action", "Entity", "Entity id", "Tenant", "Result", "Details", "IP"]));
        foreach (var a in rows)
        {
            sb.AppendLine(Csv([a.At.ToString("yyyy-MM-dd HH:mm:ss"), a.ActorName, a.Action, a.EntityType, a.EntityId, a.TenantId?.ToString(), a.Result, a.Details, a.IpAddress]));
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", "audit-log.csv");
    }
}
