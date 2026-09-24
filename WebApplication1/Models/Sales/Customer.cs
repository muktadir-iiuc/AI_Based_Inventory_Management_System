using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Common;
using WebApplication1.Models.Purchase;

namespace WebApplication1.Models.Sales;

public class Customer : BaseEntity
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ContactPerson { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    // Balance brought forward from before this customer was entered in the system, posted to
    // Accounts Receivable once at creation. Positive = the customer owes us; negative = an
    // advance they paid us. Not tied to any warehouse, so it counts for every user who can see
    // the customer. Paid down by Payments with CustomerId set (OpeningBalancePayments).
    [Display(Name = "Opening Balance")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningBalance { get; set; }

    [Display(Name = "Opening Balance Date")]
    [DataType(DataType.Date)]
    public DateTime? OpeningBalanceDate { get; set; }

    // Create-form helper only: the form takes a non-negative amount plus this flag, and the
    // controller turns it into the signed OpeningBalance above.
    [NotMapped]
    public bool OpeningBalanceIsAdvance { get; set; }

    public ICollection<SalesInvoice> SalesInvoices { get; set; } = [];

    public ICollection<Payment> OpeningBalancePayments { get; set; } = [];

    // Opening balance, plus invoiced minus paid minus returned across all Posted sales
    // invoices, minus payments against the opening balance — matches the Customer Ledger's
    // closing balance (see PartyLedgerViewModel.Build) exactly. A sales return posts a credit
    // straight to Accounts Receivable and never creates a refund Payment, so returns must be
    // subtracted here too or this figure overstates the due for any customer who has returned
    // goods. Requires SalesInvoices (with Items, Payments and Returns) and
    // OpeningBalancePayments to be loaded (see CustomersController) — computed in memory like
    // SalesInvoice.TotalAmount, not translated to SQL.
    [NotMapped]
    public decimal OutstandingDue => OpeningBalance
        - OpeningBalancePayments.Sum(p => p.Amount)
        + SalesInvoices
            .Where(s => s.Status == DocumentStatus.Posted)
            .Sum(s => s.TotalAmount - s.Payments.Sum(p => p.Amount) - s.Returns.Sum(r => r.TotalAmount));
}
