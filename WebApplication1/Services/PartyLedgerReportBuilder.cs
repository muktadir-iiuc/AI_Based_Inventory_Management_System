using System.Globalization;
using AspNetCore.Reporting;
using WebApplication1.Models.Common;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

// Renders a Customer/Supplier ledger as an A4 PDF in the same style as the Sales Invoice report
// (Reports/PartyLedgerReport.rdlc). As with that report, every value is pre-formatted to its final
// display string here so the RDLC only ever needs bare "=Fields!X.Value" bindings — the renderer
// can't compile richer VB expressions on this runtime.
public static class PartyLedgerReportBuilder
{
    public static byte[] Render(PartyLedgerViewModel vm, CompanySetting company, string contentRootPath)
    {
        var closingLabel = (vm.IsCustomer, Math.Sign(vm.ClosingBalance)) switch
        {
            (true, > 0) => "Closing (owed to us)",
            (true, < 0) => "Closing (advance received)",
            (false, > 0) => "Closing (we owe)",
            (false, < 0) => "Closing (advance paid)",
            _ => "Closing Balance"
        };

        var header = new PartyLedgerReportHeader
        {
            CompanyName = company.CompanyName,
            CompanyAddress = company.Address ?? string.Empty,
            CompanyPhone = string.IsNullOrWhiteSpace(company.Phone) ? string.Empty : $"Phone: {company.Phone}",
            CompanyLogo = company.LogoImage ?? SalesInvoiceReportHeader.TransparentPixel,
            CompanyLogoMimeType = company.LogoImage is null ? "image/png" : company.LogoMimeType ?? "image/png",
            ReportTitle = vm.Title.ToUpperInvariant(),
            PartyLabel = $"{vm.PartyLabel}: ",
            PartyName = vm.PartyName ?? string.Empty,
            PartyAddress = vm.PartyAddress ?? string.Empty,
            PartyPhone = vm.PartyPhone ?? string.Empty,
            Period = $"{vm.From?.ToString("dd MMM yyyy") ?? "Beginning"} to {vm.To?.ToString("dd MMM yyyy") ?? "Today"}",
            PrintedOn = DateTime.Now.ToString("dd MMM yyyy HH:mm"),
            ScopeNote = vm.WarehouseScoped ? "Assigned warehouse(s) only" : "All warehouses",
            OpeningBalance = Money(vm.OpeningBalance),
            TotalDebit = Money(vm.TotalDebit),
            TotalCredit = Money(vm.TotalCredit),
            ClosingLabel = closingLabel,
            ClosingBalance = Money(vm.ClosingBalance)
        };

        var lines = new List<PartyLedgerReportLine>();

        // Anything before the From date collapses into a single brought-forward row, exactly as
        // the on-screen ledger's Opening Balance card does.
        if (vm.From.HasValue)
        {
            lines.Add(new PartyLedgerReportLine
            {
                Date = vm.From.Value.ToString("dd MMM yyyy"),
                DocType = "Opening Balance",
                DocNumber = string.Empty,
                Description = "Balance brought forward",
                Balance = Money(vm.OpeningBalance)
            });
        }

        lines.AddRange(vm.Rows.Select(r => new PartyLedgerReportLine
        {
            Date = r.Date.ToString("dd MMM yyyy"),
            DocType = r.DocumentType,
            DocNumber = r.DocumentNumber,
            Description = r.Description ?? string.Empty,
            Debit = r.Debit == 0 ? string.Empty : Money(r.Debit),
            Credit = r.Credit == 0 ? string.Empty : Money(r.Credit),
            Balance = Money(r.RunningBalance)
        }));

        // A data region with no rows would drop its header row, so an empty period gets a
        // placeholder line instead.
        if (lines.Count == 0)
        {
            lines.Add(new PartyLedgerReportLine { Description = "No transactions in this period", Balance = Money(vm.ClosingBalance) });
        }

        var report = new LocalReport(Path.Combine(contentRootPath, "Reports", "PartyLedgerReport.rdlc"));
        report.AddDataSource("LedgerHeader", new List<PartyLedgerReportHeader> { header });
        report.AddDataSource("LedgerRows", lines);

        return report.Execute(RenderType.Pdf, 1, null, string.Empty).MainStream;
    }

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture);
}
