using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

public class PartyPaymentService(ApplicationDbContext db, IAccountingService accountingService) : IPartyPaymentService
{
    // One thing the amount can be applied to, oldest first.
    private sealed record Target(string Label, decimal Due, Action<Payment> Assign);

    public async Task<PartyPaymentResult> RecordAsync(PartyLedgerType type, int partyId, decimal amount, DateTime date,
        string? notes, List<int>? warehouseIds, string? createdBy)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            return Fail("Enter an amount greater than zero.");
        }

        var isCustomer = type == PartyLedgerType.Customer;
        var partyName = isCustomer ? "customer" : "supplier";

        // Opening balance is signed in the party's natural direction (positive = they owe us / we
        // owe them), and every party-level payment (no invoice) reduces it — see Customer.OpeningBalance.
        decimal openingRemaining;
        var targets = new List<Target>();
        if (isCustomer)
        {
            var customer = await db.Customers.Include(c => c.OpeningBalancePayments).FirstOrDefaultAsync(c => c.Id == partyId);
            if (customer is null) return Fail("Customer not found.");
            openingRemaining = customer.OpeningBalance - customer.OpeningBalancePayments.Sum(p => p.Amount);

            var invoices = await db.SalesInvoices
                .Include(i => i.Items).Include(i => i.Payments).Include(i => i.Returns).ThenInclude(r => r.Items)
                .Where(i => i.CustomerId == partyId && i.Status == DocumentStatus.Posted)
                .Where(i => warehouseIds == null || warehouseIds.Contains(i.WarehouseId))
                .OrderBy(i => i.Date).ThenBy(i => i.Id)
                .ToListAsync();
            foreach (var invoice in invoices)
            {
                var due = invoice.TotalAmount - invoice.Payments.Sum(p => p.Amount) - invoice.Returns.Sum(r => r.TotalAmount);
                if (due > 0)
                {
                    var id = invoice.Id;
                    targets.Add(new Target(invoice.InvoiceNumber, due, p => p.SalesInvoiceId = id));
                }
            }
            if (openingRemaining > 0)
            {
                targets.Insert(0, new Target("Opening balance", openingRemaining, p => p.CustomerId = partyId));
            }
        }
        else
        {
            var supplier = await db.Suppliers.Include(s => s.OpeningBalancePayments).FirstOrDefaultAsync(s => s.Id == partyId);
            if (supplier is null) return Fail("Supplier not found.");
            openingRemaining = supplier.OpeningBalance - supplier.OpeningBalancePayments.Sum(p => p.Amount);

            var invoices = await db.PurchaseInvoices
                .Include(i => i.Items).Include(i => i.Payments).Include(i => i.Returns).ThenInclude(r => r.Items)
                .Where(i => i.SupplierId == partyId && i.Status == DocumentStatus.Posted)
                .Where(i => warehouseIds == null || warehouseIds.Contains(i.WarehouseId))
                .OrderBy(i => i.Date).ThenBy(i => i.Id)
                .ToListAsync();
            foreach (var invoice in invoices)
            {
                var due = invoice.TotalAmount - invoice.Payments.Sum(p => p.Amount) - invoice.Returns.Sum(r => r.TotalAmount);
                if (due > 0)
                {
                    var id = invoice.Id;
                    targets.Add(new Target(invoice.InvoiceNumber, due, p => p.PurchaseInvoiceId = id));
                }
            }
            if (openingRemaining > 0)
            {
                targets.Insert(0, new Target("Opening balance", openingRemaining, p => p.SupplierId = partyId));
            }
        }

        // What the ledger shows as this party's closing balance: everything still open, net of any
        // advance (a negative opening balance) that has not been used up yet.
        var totalDue = targets.Sum(t => t.Due) + Math.Min(openingRemaining, 0);
        if (totalDue <= 0)
        {
            return Fail($"This {partyName} has nothing due, so there is nothing to pay.");
        }
        if (amount > totalDue)
        {
            return Fail($"The amount cannot exceed the {partyName}'s outstanding due of {totalDue:N2}.");
        }

        var paymentCount = await db.Payments.CountAsync();
        var remaining = amount;
        var result = new List<Payment>();
        var applied = new List<string>();

        foreach (var target in targets)
        {
            if (remaining <= 0) break;

            var part = Math.Min(remaining, target.Due);
            var payment = new Payment
            {
                PaymentNumber = $"PAY-{++paymentCount:D6}",
                Direction = isCustomer ? PaymentDirection.In : PaymentDirection.Out,
                Date = date,
                Amount = part,
                // Ties the pieces of one ledger payment together in the Payments list.
                Notes = string.IsNullOrWhiteSpace(notes)
                    ? $"Ledger payment of {amount:N2} — applied to {target.Label}"
                    : $"{notes.Trim()} (ledger payment of {amount:N2} — applied to {target.Label})",
                CreatedBy = createdBy
            };
            target.Assign(payment);

            db.Payments.Add(payment);
            await accountingService.PostPaymentAsync(payment);
            result.Add(payment);
            applied.Add($"{target.Label} — {part:N2}");
            remaining -= part;
        }

        // Only reachable if an advance (negative opening balance) made totalDue smaller than the
        // sum of the open items, in which case the amount is always fully placed above; guard anyway.
        if (remaining > 0)
        {
            return Fail("The amount could not be fully applied. Please try a smaller amount.");
        }

        await db.SaveChangesAsync();

        return new PartyPaymentResult
        {
            Ok = true,
            PaymentNumbers = result.Select(p => p.PaymentNumber).ToList(),
            Applied = applied
        };
    }

    private static PartyPaymentResult Fail(string error) => new() { Ok = false, Error = error };
}
