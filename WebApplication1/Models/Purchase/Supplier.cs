using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Common;

namespace WebApplication1.Models.Purchase;

public class Supplier : BaseEntity
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

    // Balance brought forward from before this supplier was entered in the system, posted to
    // Accounts Payable once at creation. Positive = we owe the supplier; negative = an
    // advance we paid them. Not tied to any warehouse, so it counts for every user who can see
    // the supplier. Paid down by Payments with SupplierId set (OpeningBalancePayments).
    [Display(Name = "Opening Balance")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningBalance { get; set; }

    [Display(Name = "Opening Balance Date")]
    [DataType(DataType.Date)]
    public DateTime? OpeningBalanceDate { get; set; }

    // Create-form helper only — see Customer.OpeningBalanceIsAdvance.
    [NotMapped]
    public bool OpeningBalanceIsAdvance { get; set; }

    public ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = [];

    public ICollection<Payment> OpeningBalancePayments { get; set; } = [];
}
