using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    public ICollection<SalesInvoice> SalesInvoices { get; set; } = [];

    // Invoiced minus paid, across all Posted sales invoices. Requires SalesInvoices to be
    // loaded with their Items and Payments (see CustomersController) — computed in memory
    // like SalesInvoice.TotalAmount, not translated to SQL.
    [NotMapped]
    public decimal OutstandingDue => SalesInvoices
        .Where(s => s.Status == DocumentStatus.Posted)
        .Sum(s => s.TotalAmount - s.Payments.Sum(p => p.Amount));
}
