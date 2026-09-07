using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Accounting;

public enum JournalSource
{
    Manual = 1,
    PurchaseInvoice = 2,
    SalesInvoice = 3,
    Payment = 4,
    Reversal = 5
}

public class JournalEntry
{
    public int Id { get; set; }

    public string EntryNumber { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public string Description { get; set; } = string.Empty;

    public JournalSource Source { get; set; } = JournalSource.Manual;

    public string? SourceReference { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public decimal TotalDebit => Lines.Sum(l => l.Debit);

    [NotMapped]
    public decimal TotalCredit => Lines.Sum(l => l.Credit);

    public ICollection<JournalEntryLine> Lines { get; set; } = [];
}

public class JournalEntryLine
{
    public int Id { get; set; }

    public int JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Debit { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Credit { get; set; }

    public string? Memo { get; set; }
}
