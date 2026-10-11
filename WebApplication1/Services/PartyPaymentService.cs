using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

public class PartyPaymentService(ApplicationDbContext db, IAccountingService accountingService) : IPartyPaymentService
{
    // One thing the amount can be applied to, oldest first.
    private sealed class Target(string Label, decimal Due, Action<Payment> Assign)
    {
        public string Label { get; } = Label;
        public decimal Due { get; set; } = Due;
        public Action<Payment> Assign { get; } = Assign;
    }

    public async Task<PartyPaymentResult> RecordAsync(PartyLedgerType type, int partyId, decimal amount, DateTime date,
        PaymentMethod method, string? bankName, string? notes, List<int>? warehouseIds, string? createdBy,
        bool save = true, decimal discount = 0)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        discount = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        if (discount < 0 || amount < 0)
        {
            return Fail("Amount and discount can't be negative.");
        }
        if (amount <= 0 && discount <= 0)
        {
            return Fail("Enter an amount greater than zero.");
        }
        if (discount > 0 && type != PartyLedgerType.Customer)
        {
            return Fail("A discount can only be given to a customer.");
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

        // A discount settles real dues only (opening balance, then invoices) — it can't exceed
        // what is owed, and never becomes an advance.
        var totalDue = targets.Sum(t => t.Due);
        if (discount > totalDue)
        {
            return Fail($"The discount can't be more than the current due of {totalDue:N2}.");
        }

        // No upper limit: whatever is left after every open item is settled (or the whole amount,
        // when nothing is due) is kept as a party-level payment, which drives the opening balance
        // negative — the same "advance" representation a negative opening balance already uses.
        targets.Add(new Target("Advance", decimal.MaxValue,
            isCustomer ? (Payment p) => p.CustomerId = partyId : (Payment p) => p.SupplierId = partyId));

        // Payments the caller has added but not saved yet (save: false) still take a number.
        var paymentCount = await db.Payments.CountAsync()
                           + db.ChangeTracker.Entries<Payment>().Count(e => e.State == EntityState.Added);
        var result = new List<Payment>();
        var applied = new List<string>();

        // Oldest-first pass over the targets. The discount goes first, so the cash then fills
        // whatever it left open.
        async Task ApplyAsync(decimal total, bool isDiscount)
        {
            var remaining = total;
            foreach (var target in targets)
            {
                if (remaining <= 0) break;
                if (target.Due <= 0) continue;

                var part = Math.Min(remaining, target.Due);
                var payment = new Payment
                {
                    PaymentNumber = $"PAY-{++paymentCount:D6}",
                    Direction = isCustomer ? PaymentDirection.In : PaymentDirection.Out,
                    Date = date,
                    Amount = part,
                    Method = method,
                    BankName = isDiscount ? null : bankName,
                    IsDiscount = isDiscount,
                    // Ties the pieces of one ledger payment together in the Payments list.
                    Notes = isDiscount
                        ? $"Discount of {total:N2} — applied to {target.Label}" + (string.IsNullOrWhiteSpace(notes) ? "" : $" ({notes.Trim()})")
                        : string.IsNullOrWhiteSpace(notes)
                            ? $"Ledger payment of {total:N2} — applied to {target.Label}"
                            : $"{notes.Trim()} (ledger payment of {total:N2} — applied to {target.Label})",
                    CreatedBy = createdBy
                };
                target.Assign(payment);

                db.Payments.Add(payment);
                await accountingService.PostPaymentAsync(payment);
                result.Add(payment);
                applied.Add($"{target.Label} — {part:N2}{(isDiscount ? " (discount)" : "")}");
                target.Due -= part;
                remaining -= part;
            }
        }

        if (discount > 0) await ApplyAsync(discount, true);
        if (amount > 0) await ApplyAsync(amount, false);

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

    public async Task<PartyPaymentResult> RefundAdvanceAsync(PartyLedgerType type, int partyId, decimal amount, DateTime date,
        PaymentMethod method, string? bankName, string? notes, List<int>? warehouseIds, string? createdBy)
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
        var exists = isCustomer
            ? await db.Customers.AnyAsync(c => c.Id == partyId)
            : await db.Suppliers.AnyAsync(s => s.Id == partyId);
        if (!exists) return Fail(isCustomer ? "Customer not found." : "Supplier not found.");

        var advance = await RefundableAdvanceAsync(type, partyId, warehouseIds);
        if (advance <= 0)
        {
            return Fail(isCustomer
                ? "This customer has no advance to refund."
                : "This supplier holds no advance of ours to refund.");
        }
        if (amount > advance)
        {
            return Fail($"The refund can't be more than the advance of {advance:N2}.");
        }

        // A refund is a party-level payment with a negative amount: it undoes part of the
        // advance, so every dues figure (which all sum party-level payments) nets it automatically.
        var paymentCount = await db.Payments.CountAsync()
                           + db.ChangeTracker.Entries<Payment>().Count(e => e.State == EntityState.Added);
        var payment = new Payment
        {
            PaymentNumber = $"PAY-{paymentCount + 1:D6}",
            Direction = isCustomer ? PaymentDirection.In : PaymentDirection.Out,
            Date = date,
            Amount = -amount,
            Method = method,
            BankName = bankName,
            Notes = string.IsNullOrWhiteSpace(notes) ? "Advance refund" : $"Advance refund — {notes.Trim()}",
            CreatedBy = createdBy
        };
        if (isCustomer) payment.CustomerId = partyId; else payment.SupplierId = partyId;

        db.Payments.Add(payment);
        await accountingService.PostPaymentAsync(payment);
        await db.SaveChangesAsync();

        return new PartyPaymentResult
        {
            Ok = true,
            PaymentNumbers = [payment.PaymentNumber],
            Applied = [$"Advance refund — {amount:N2}"]
        };
    }

    // The advance is what the ledger shows as a negative balance. The opening balance and the
    // advance itself aren't warehouse-scoped but invoices are, so a warehouse-restricted user could
    // see an advance that dues in other warehouses already eat into: the refundable amount is the
    // smaller of the advance in the caller's scope and across the whole account.
    public async Task<decimal> RefundableAdvanceAsync(PartyLedgerType type, int partyId, List<int>? warehouseIds)
    {
        var scoped = await PartyBalanceAsync(type, partyId, warehouseIds);
        var overall = warehouseIds == null ? scoped : await PartyBalanceAsync(type, partyId, null);
        return Math.Max(0, -Math.Max(scoped, overall));
    }

    public Task<decimal> CustomerBalanceAsync(int customerId, List<int>? warehouseIds) =>
        PartyBalanceAsync(PartyLedgerType.Customer, customerId, warehouseIds);

    // Same formula as the party ledger's current balance / Customers|Suppliers -> Due:
    // opening balance - party-level payments + invoiced - paid - returned (invoices in scope).
    // Positive = they owe us (customer) / we owe them (supplier); negative = advance.
    public async Task<decimal> PartyBalanceAsync(PartyLedgerType type, int partyId, List<int>? warehouseIds)
    {
        if (type == PartyLedgerType.Customer)
        {
            var invoices = db.SalesInvoices.Where(i => i.CustomerId == partyId && i.Status == DocumentStatus.Posted
                                                       && (warehouseIds == null || warehouseIds.Contains(i.WarehouseId)));

            var invoiced = await db.SalesInvoiceItems.Where(i => i.IsCurrent && invoices.Any(s => s.Id == i.SalesInvoiceId))
                .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice - i.DiscountShare)) ?? 0;
            var paid = await db.Payments.Where(p => invoices.Any(s => s.Id == p.SalesInvoiceId))
                .SumAsync(p => (decimal?)p.Amount) ?? 0;
            var returned = await db.SalesReturnItems.Where(r => invoices.Any(s => s.Id == r.SalesReturn!.SalesInvoiceId))
                .SumAsync(r => (decimal?)(r.Quantity * r.UnitPrice - r.DiscountShare)) ?? 0;

            // Opening balances and the payments against them aren't warehouse-scoped — see the ledger.
            var opening = await db.Customers.Where(c => c.Id == partyId).Select(c => c.OpeningBalance).FirstOrDefaultAsync();
            var openingPaid = await db.Payments.Where(p => p.SalesInvoiceId == null && p.CustomerId == partyId)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return opening - openingPaid + invoiced - paid - returned;
        }
        else
        {
            var invoices = db.PurchaseInvoices.Where(i => i.SupplierId == partyId && i.Status == DocumentStatus.Posted
                                                          && (warehouseIds == null || warehouseIds.Contains(i.WarehouseId)));

            var invoiced = await db.PurchaseInvoiceItems.Where(i => i.IsCurrent && invoices.Any(s => s.Id == i.PurchaseInvoiceId))
                .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0;
            var paid = await db.Payments.Where(p => invoices.Any(s => s.Id == p.PurchaseInvoiceId))
                .SumAsync(p => (decimal?)p.Amount) ?? 0;
            var returned = await db.PurchaseReturnItems.Where(r => invoices.Any(s => s.Id == r.PurchaseReturn!.PurchaseInvoiceId))
                .SumAsync(r => (decimal?)(r.Quantity * r.UnitCost)) ?? 0;

            var opening = await db.Suppliers.Where(s => s.Id == partyId).Select(s => s.OpeningBalance).FirstOrDefaultAsync();
            var openingPaid = await db.Payments.Where(p => p.PurchaseInvoiceId == null && p.SupplierId == partyId)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return opening - openingPaid + invoiced - paid - returned;
        }
    }

    private static PartyPaymentResult Fail(string error) => new() { Ok = false, Error = error };
}
