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
        PaymentMethod method, string? bankName, string? notes, List<int>? warehouseIds, string? createdBy,
        bool save = true)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            return Fail("Enter an amount greater than zero.");
        }
        if (PaymentMethods.Validate(method, bankName) is { } methodError)
        {
            return Fail(methodError);
        }
        bankName = PaymentMethods.CleanBankName(method, bankName);

        var isCustomer = type == PartyLedgerType.Customer;

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

        // No upper limit: whatever is left after every open item is settled (or the whole amount,
        // when nothing is due) is kept as a party-level payment, which drives the opening balance
        // negative — the same "advance" representation a negative opening balance already uses.
        targets.Add(new Target("Advance", decimal.MaxValue,
            isCustomer ? (Payment p) => p.CustomerId = partyId : (Payment p) => p.SupplierId = partyId));

        // Payments the caller has added but not saved yet (save: false) still take a number.
        var paymentCount = await db.Payments.CountAsync()
                           + db.ChangeTracker.Entries<Payment>().Count(e => e.State == EntityState.Added);
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
                Method = method,
                BankName = bankName,
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

        if (save)
        {
            await db.SaveChangesAsync();
        }

        return new PartyPaymentResult
        {
            Ok = true,
            PaymentNumbers = result.Select(p => p.PaymentNumber).ToList(),
            Applied = applied
        };
    }

    public async Task<decimal> CustomerBalanceAsync(int customerId, List<int>? warehouseIds)
    {
        var invoices = db.SalesInvoices.Where(i => i.CustomerId == customerId && i.Status == DocumentStatus.Posted
                                                   && (warehouseIds == null || warehouseIds.Contains(i.WarehouseId)));

        var invoiced = await db.SalesInvoiceItems.Where(i => i.IsCurrent && invoices.Any(s => s.Id == i.SalesInvoiceId))
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice - i.DiscountShare)) ?? 0;
        var paid = await db.Payments.Where(p => invoices.Any(s => s.Id == p.SalesInvoiceId))
            .SumAsync(p => (decimal?)p.Amount) ?? 0;
        var returned = await db.SalesReturnItems.Where(r => invoices.Any(s => s.Id == r.SalesReturn!.SalesInvoiceId))
            .SumAsync(r => (decimal?)(r.Quantity * r.UnitPrice - r.DiscountShare)) ?? 0;

        // Opening balances and the payments against them aren't warehouse-scoped — see the ledger.
        var opening = await db.Customers.Where(c => c.Id == customerId).Select(c => c.OpeningBalance).FirstOrDefaultAsync();
        var openingPaid = await db.Payments.Where(p => p.SalesInvoiceId == null && p.CustomerId == customerId)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        return opening - openingPaid + invoiced - paid - returned;
    }

    private static PartyPaymentResult Fail(string error) => new() { Ok = false, Error = error };
}
