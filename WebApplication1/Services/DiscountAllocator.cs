using WebApplication1.Models.Sales;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

// Turns a whole-invoice discount into per-line shares. Storing the discount on the lines (rather
// than only on the invoice) means every existing "sum of quantity x price" query, report and
// ledger becomes net of the discount by subtracting the line's share, and a sales return can
// credit back exactly the discount that belonged to the returned goods.
public static class DiscountAllocator
{
    /// <summary>The currency amount for a discount entered as an amount or a percentage of the subtotal.</summary>
    public static decimal ToAmount(DiscountType type, decimal value, decimal subtotal)
    {
        if (value <= 0 || subtotal <= 0)
        {
            return 0;
        }

        return type == DiscountType.Percent
            ? Math.Round(subtotal * value / 100m, 2, MidpointRounding.AwayFromZero)
            : Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Spreads <paramref name="discount"/> over the lines in proportion to their gross value. Each
    /// share is rounded to cents and the rounding remainder goes to the largest line, so the
    /// shares add up to the discount exactly and no share exceeds its own line.
    /// </summary>
    public static void Allocate(IReadOnlyList<SalesInvoiceItem> items, decimal discount)
    {
        foreach (var item in items)
        {
            item.DiscountShare = 0;
        }

        var subtotal = items.Sum(i => i.Quantity * i.UnitPrice);
        if (discount <= 0 || subtotal <= 0 || items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            item.DiscountShare = Math.Round(discount * (item.Quantity * item.UnitPrice) / subtotal, 2, MidpointRounding.AwayFromZero);
        }

        var remainder = discount - items.Sum(i => i.DiscountShare);
        if (remainder != 0)
        {
            var largest = items.OrderByDescending(i => i.Quantity * i.UnitPrice).First();
            largest.DiscountShare += remainder;
        }
    }

    /// <summary>
    /// The discount that belongs to returning <paramref name="quantity"/> of an invoice line whose
    /// total share was <paramref name="lineShare"/>: proportional, except that returning everything
    /// still returnable gives back whatever is left, so the pieces sum to the line's share exactly.
    /// </summary>
    public static decimal ReturnShare(decimal lineShare, decimal lineQuantity, decimal quantity, decimal alreadyReturnedShare, decimal remainingQuantity)
    {
        if (lineShare <= 0 || lineQuantity <= 0 || quantity <= 0)
        {
            return 0;
        }

        return quantity >= remainingQuantity
            ? lineShare - alreadyReturnedShare
            : Math.Round(lineShare * quantity / lineQuantity, 2, MidpointRounding.AwayFromZero);
    }
}
