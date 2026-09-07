using System.ComponentModel.DataAnnotations;
using WebApplication1.Models.Accounting;

namespace WebApplication1.Models.ViewModels;

public class JournalEntryCreateViewModel
{
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    [Required(ErrorMessage = "Please enter a description.")]
    public string Description { get; set; } = string.Empty;

    public List<JournalLineInput> Lines { get; set; } = [];
}

public class JournalLineInput
{
    [Required]
    public int AccountId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Debit { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Credit { get; set; }

    public string? Memo { get; set; }
}

public class PaymentCreateViewModel
{
    public PaymentDirection Direction { get; set; } = PaymentDirection.In;

    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    public int? PurchaseInvoiceId { get; set; }
    public int? SalesInvoiceId { get; set; }

    public string? Notes { get; set; }
}

public class LedgerViewModel
{
    public Account Account { get; set; } = null!;
    public List<JournalEntryLine> Lines { get; set; } = [];
}
