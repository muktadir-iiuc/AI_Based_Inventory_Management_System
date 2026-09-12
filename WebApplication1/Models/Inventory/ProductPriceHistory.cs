using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Identity;

namespace WebApplication1.Models.Inventory;

// Immutable audit trail for Product.SalePrice changes: one row per actual change, never
// overwritten. Doubles as the source of truth for the monthly price-review reminder — a
// product is "reviewed" for a calendar month once it has at least one row with ChangedAt
// in that month (see IProductPriceService.GetPendingMonthlyReviewQuery).
public class ProductPriceHistory
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OldPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NewPrice { get; set; }

    public string ChangedByUserId { get; set; } = string.Empty;
    public ApplicationUser? ChangedByUser { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
