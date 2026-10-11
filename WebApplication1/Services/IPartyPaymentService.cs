using WebApplication1.Models.Accounting;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

public class PartyPaymentResult
{
    public bool Ok { get; init; }

    public string? Error { get; init; }

    /// <summary>Numbers of the payment records created (one per document the amount was applied to).</summary>
    public List<string> PaymentNumbers { get; init; } = [];

    /// <summary>Human-readable "what did this payment settle" lines, e.g. "SINV-000004 — 120.00".</summary>
    public List<string> Applied { get; init; } = [];
}

public interface IPartyPaymentService
{
    /// <summary>
    /// Records a payment received from a customer / made to a supplier against their total
    /// outstanding due — not against a chosen invoice. The amount is applied automatically, oldest
    /// first: the brought-forward opening balance, then invoices by date. Each piece becomes an
    /// ordinary payment (so every invoice's own due, the payment lists and the ledgers all stay
    /// consistent) with its own journal entry. Nothing is saved unless the whole amount fits.
    /// </summary>
    /// <param name="warehouseIds">The caller's warehouse scope (null = unrestricted): only invoices
    /// in these warehouses are paid down, exactly the invoices that make up the due they can see.</param>
    /// <param name="discount">Customers only: a discount allowed on the customer's total due, settled
    /// oldest-first like a payment but moving no money (posted Dr Discount Allowed, Cr Receivable).
    /// Capped at the current due; <paramref name="amount"/> may then be 0.</param>
    /// <param name="save">False leaves the payments added but unsaved, so the caller can commit
    /// them in the same SaveChanges as its own changes (Sales Invoice Create).</param>
    Task<PartyPaymentResult> RecordAsync(PartyLedgerType type, int partyId, decimal amount, DateTime date,
        PaymentMethod method, string? bankName, string? notes, List<int>? warehouseIds, string? createdBy,
        bool save = true, decimal discount = 0);

    /// <summary>
    /// Pays a party's advance back: money returned to a customer who paid more than they owed, or
    /// received back from a supplier we overpaid. Capped at the advance (see RefundableAdvanceAsync),
    /// never more. Stored as one party-level payment with a negative amount, so every dues figure
    /// nets it with no further changes, and posted as the reverse journal entry.
    /// </summary>
    Task<PartyPaymentResult> RefundAdvanceAsync(PartyLedgerType type, int partyId, decimal amount, DateTime date,
        PaymentMethod method, string? bankName, string? notes, List<int>? warehouseIds, string? createdBy);

    /// <summary>The most that can be refunded right now: the party's advance (negative balance),
    /// taking the smaller of the caller's warehouse scope and the whole account. 0 = none.</summary>
    Task<decimal> RefundableAdvanceAsync(PartyLedgerType type, int partyId, List<int>? warehouseIds);

    /// <summary>A customer's/supplier's current balance (negative = advance), as on their ledger.</summary>
    Task<decimal> PartyBalanceAsync(PartyLedgerType type, int partyId, List<int>? warehouseIds);

    /// <summary>
    /// A customer's current due (negative = advance): opening balance minus payments against it,
    /// plus invoiced minus paid minus returned on posted invoices in the caller's warehouse scope.
    /// Same formula as the customer ledger / Customers → Due.
    /// </summary>
    Task<decimal> CustomerBalanceAsync(int customerId, List<int>? warehouseIds);
}
