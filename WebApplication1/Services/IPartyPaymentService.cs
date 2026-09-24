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
    Task<PartyPaymentResult> RecordAsync(PartyLedgerType type, int partyId, decimal amount, DateTime date,
        string? notes, List<int>? warehouseIds, string? createdBy);
}
