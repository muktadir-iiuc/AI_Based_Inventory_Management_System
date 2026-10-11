using AspNetCore.Reporting;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Saas;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services.Saas;

/// <summary>Same field set as the sales invoice report, plus the tax line a subscription invoice carries.</summary>
public class SaasInvoiceReportHeader : SalesInvoiceReportHeader
{
    public string TaxAmount { get; set; } = string.Empty;
}

public interface ISaasInvoicePdfService
{
    /// <summary>
    /// Renders the subscription invoice with the same RDLC layout family as the sales invoice PDF
    /// (logo block, title, invoice and bill-to boxes, item table, totals, amount in words, signatures).
    /// The caller must already have checked the user may see this invoice.
    /// </summary>
    Task<byte[]> RenderAsync(int invoiceId);
}

public class SaasInvoicePdfService(ApplicationDbContext db, IPlatformSettings settings, IPlatformBranding branding, IWebHostEnvironment env) : ISaasInvoicePdfService
{
    public async Task<byte[]> RenderAsync(int invoiceId)
    {
        var invoice = await db.SaasInvoices.AsNoTracking().Include(i => i.Items).Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Id == invoiceId) ?? throw new InvalidOperationException("Invoice not found.");

        var local = (DateTime d) => d.ToLocalTime();
        var charged = invoice.Items.Where(i => i.ItemType is InvoiceItemType.Subscription or InvoiceItemType.Adjustment).OrderBy(i => i.ItemType).ThenBy(i => i.Id).ToList();
        var discount = -invoice.Items.Where(i => i.ItemType == InvoiceItemType.Discount).Sum(i => i.Amount);
        var tax = invoice.Items.Where(i => i.ItemType == InvoiceItemType.Tax).Sum(i => i.Amount);
        var subtotal = charged.Sum(i => i.Amount);
        var balance = invoice.Total - invoice.AmountPaid;

        // What the tenant owes in total across every open invoice (this one included).
        var open = new[] { InvoiceStatus.Pending, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue };
        var outstanding = (await db.SaasInvoices.AsNoTracking()
            .Where(i => i.TenantId == invoice.TenantId && i.Currency == invoice.Currency && open.Contains(i.Status))
            .Select(i => new { i.Total, i.AmountPaid }).ToListAsync()).Sum(i => i.Total - i.AmountPaid);

        var phone = invoice.Tenant?.ContactPhone;
        var address = string.Join(" | ", new[]
        {
            invoice.BillToAddress,
            invoice.BillToEmail,
            string.IsNullOrWhiteSpace(invoice.BillToTaxId) ? null : $"Tax/BIN: {invoice.BillToTaxId}"
        }.Where(v => !string.IsNullOrWhiteSpace(v)));

        var platformPhone = await settings.GetAsync(PlatformSettingKeys.PlatformPhone);
        var logo = await branding.GetLogoAsync();
        var header = new SaasInvoiceReportHeader
        {
            // The issuer of a subscription invoice is the platform operator, never the tenant's own company profile.
            CompanyName = await branding.GetNameAsync(),
            CompanyAddress = await settings.GetAsync(PlatformSettingKeys.PlatformAddress),
            CompanyPhone = string.IsNullOrWhiteSpace(platformPhone) ? string.Empty : $"Phone: {platformPhone}",
            CompanyLogo = logo?.Bytes ?? SalesInvoiceReportHeader.TransparentPixel,
            CompanyLogoMimeType = logo?.ContentType ?? "image/png",
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = local(invoice.IssueDate).ToDateTimeText(),
            Status = invoice.Status switch { InvoiceStatus.PartiallyPaid => "Partially paid", var s => s.ToString() },
            CustomerName = invoice.BillToName ?? string.Empty,
            CustomerAddress = address,
            CustomerPhone = phone ?? string.Empty,
            WarehouseName = $"{invoice.PlanName} ({local(invoice.PeriodStart):dd MMM yyyy} - {local(invoice.PeriodEnd):dd MMM yyyy})",
            Notes = string.Join(" ", new[] { $"Due date: {local(invoice.DueDate):dd MMM yyyy}.", invoice.Notes }.Where(v => !string.IsNullOrWhiteSpace(v))),
            SubtotalAmount = subtotal.ToString("N2"),
            DiscountAmount = discount.ToString("N2"),
            TaxAmount = tax.ToString("N2"),
            TotalAmount = invoice.Total.ToString("N2"),
            PaidAmount = invoice.AmountPaid.ToString("N2"),
            DueAmount = balance.ToString("N2"),
            CustomerOutstandingDue = outstanding.ToString("N2"),
            // The words helper is Bangladeshi Taka; other currencies are not spelled out rather than spelled wrongly.
            AmountInWords = invoice.Currency.Equals("BDT", StringComparison.OrdinalIgnoreCase) ? invoice.Total.ToTakaWords() : string.Empty
        };

        var lines = charged.Select((item, index) => new SalesInvoiceReportLine
        {
            SL = (index + 1).ToString(),
            ProductName = item.Description,
            Category = item.ItemType == InvoiceItemType.Subscription ? "Subscription" : "Adjustment",
            Quantity = item.Quantity.ToString("0.##"),
            UnitPrice = item.UnitPrice.ToString("N2"),
            LineTotal = item.Amount.ToString("N2")
        }).ToList();

        var report = new LocalReport(Path.Combine(env.ContentRootPath, "Reports", "SaasInvoiceReport.rdlc"));
        report.AddDataSource("InvoiceHeader", new List<SaasInvoiceReportHeader> { header });
        report.AddDataSource("InvoiceItems", lines);
        return report.Execute(RenderType.Pdf, 1, null, string.Empty).MainStream;
    }

    // The platform operator's logo: drop a PNG at wwwroot/img/platform-logo.png. Without one the header is name-only - the
    // tenant-facing company logo (company-logo.png) is a different business's brand and is never used here.
    private async Task<byte[]> LoadLogoAsync()
    {
        var path = Path.Combine(env.WebRootPath, "img", "platform-logo.png");
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : SalesInvoiceReportHeader.TransparentPixel;
    }
}
