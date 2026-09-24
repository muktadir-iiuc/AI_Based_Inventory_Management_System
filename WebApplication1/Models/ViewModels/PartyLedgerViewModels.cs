using Microsoft.AspNetCore.Mvc.Rendering;

namespace WebApplication1.Models.ViewModels;

/// <summary>Which side of the business a ledger is for. Decides the sign convention — a
/// customer's balance is money owed TO us, a supplier's is money we owe THEM — plus the
/// labels and document links the shared PartyLedger view renders.</summary>
public enum PartyLedgerType
{
    Customer = 1,
    Supplier = 2
}

/// <summary>One movement on a party's account. Debit/Credit are always stated from the
/// bookkeeping point of view of the party's control account (Receivable / Payable), so an
/// invoice debits a customer but credits a supplier.</summary>
public class PartyLedgerRow
{
    public DateTime Date { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public string DocumentNumber { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    /// <summary>Balance after this row, computed over the FULL history (see
    /// <see cref="PartyLedgerViewModel.Build"/>) — never from the filtered subset.</summary>
    public decimal RunningBalance { get; set; }

    /// <summary>Controller holding this document's Details page, or null when it has none
    /// (payments are only ever listed, never opened on their own page).</summary>
    public string? LinkController { get; set; }

    public int? LinkId { get; set; }

    /// <summary>Tie-break for same-day rows, so an invoice always sorts above the return and
    /// the payment that settle it rather than landing in arbitrary order.</summary>
    public int TypeRank { get; set; }
}

/// <summary>A party's closing balance, for the "pick a party" list shown when no specific
/// ledger has been opened yet.</summary>
public class PartyLedgerSummaryRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public decimal Balance { get; set; }
}

public class PartyLedgerViewModel
{
    public PartyLedgerType PartyType { get; set; }

    public int? PartyId { get; set; }

    public string? PartyName { get; set; }

    public string? PartyPhone { get; set; }

    public string? PartyAddress { get; set; }

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public bool WarehouseScoped { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public decimal ClosingBalance { get; set; }

    public List<PartyLedgerRow> Rows { get; set; } = [];

    /// <summary>Populated only when no party is selected — the landing state of the page.</summary>
    public List<PartyLedgerSummaryRow> Summary { get; set; } = [];

    public List<SelectListItem> Parties { get; set; } = [];

    public bool IsCustomer => PartyType == PartyLedgerType.Customer;

    public string Title => IsCustomer ? "Customer Ledger" : "Supplier Ledger";

    public string SummaryTitle => IsCustomer ? "Receivables by Customer" : "Payables by Supplier";

    public string PartyLabel => IsCustomer ? "Customer" : "Supplier";

    public string BalanceLabel => IsCustomer ? "Receivable" : "Payable";

    /// <summary>What a positive closing balance means, spelled out for the summary card.</summary>
    public string BalanceHint => IsCustomer ? "owed to us" : "we owe";

    /// <summary>Same running-balance rule as the petty cash ledger: the balance is computed
    /// once over the party's whole history (oldest first) so it stays a true account balance,
    /// and the date filter then only decides which already-computed rows are shown. Anything
    /// before <see cref="From"/> collapses into <see cref="OpeningBalance"/>.</summary>
    public void Build(IEnumerable<PartyLedgerRow> allRows)
    {
        // A customer owes us more when debited; a supplier is owed more when credited.
        var sign = IsCustomer ? 1 : -1;

        var ordered = allRows
            .OrderBy(r => r.Date)
            .ThenBy(r => r.TypeRank)
            .ThenBy(r => r.DocumentNumber, StringComparer.Ordinal)
            .ToList();

        var running = 0m;
        var opening = 0m;
        var kept = new List<PartyLedgerRow>(ordered.Count);

        foreach (var row in ordered)
        {
            running += sign * (row.Debit - row.Credit);
            row.RunningBalance = running;

            if (From.HasValue && row.Date.Date < From.Value.Date)
            {
                opening = running;
                continue;
            }
            if (To.HasValue && row.Date.Date > To.Value.Date)
            {
                continue;
            }
            kept.Add(row);
        }

        OpeningBalance = opening;
        Rows = kept;
        TotalDebit = kept.Sum(r => r.Debit);
        TotalCredit = kept.Sum(r => r.Credit);
        ClosingBalance = kept.Count > 0 ? kept[^1].RunningBalance : opening;
    }
}

// Flat, pre-formatted shapes for the A4 ledger report (see PartyLedgerReportBuilder) — every
// value is already its final display string, like SalesInvoiceReportHeader/Line.
public class PartyLedgerReportHeader
{
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;
    public byte[] CompanyLogo { get; set; } = SalesInvoiceReportHeader.TransparentPixel;
    public string CompanyLogoMimeType { get; set; } = "image/png";
    public string ReportTitle { get; set; } = string.Empty;
    public string PartyLabel { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public string PartyAddress { get; set; } = string.Empty;
    public string PartyPhone { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string PrintedOn { get; set; } = string.Empty;
    public string ScopeNote { get; set; } = string.Empty;
    public string OpeningBalance { get; set; } = string.Empty;
    public string TotalDebit { get; set; } = string.Empty;
    public string TotalCredit { get; set; } = string.Empty;
    public string ClosingLabel { get; set; } = string.Empty;
    public string ClosingBalance { get; set; } = string.Empty;
}

public class PartyLedgerReportLine
{
    public string Date { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string DocNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Debit { get; set; } = string.Empty;
    public string Credit { get; set; } = string.Empty;
    public string Balance { get; set; } = string.Empty;
}
