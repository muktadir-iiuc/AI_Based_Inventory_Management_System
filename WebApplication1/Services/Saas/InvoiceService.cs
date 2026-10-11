using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Saas;
using WebApplication1.Models.Tenancy;

namespace WebApplication1.Services.Saas;

public interface IInvoiceService
{
    /// <summary>
    /// Creates the invoice for one billing period of a subscription, or returns the one that already exists
    /// for that period - the same period is never invoiced twice. Does not save the caller's other changes
    /// beyond what is needed to number the invoice.
    /// </summary>
    Task<SaasInvoice> CreateForPeriodAsync(TenantSubscription subscription, BillingTerms terms, InvoicePurpose purpose,
        DateTime periodStart, DateTime? dueDate = null, string? actor = null);

    Task CancelAsync(int invoiceId, string? reason, string? actor);

    /// <summary>Adds a credit (negative) or extra charge (positive) line to an invoice that has no payments yet. The reason is audited.</summary>
    Task AdjustAsync(int invoiceId, string description, decimal amount, string? actor);
}

public class InvoiceService(
    ApplicationDbContext db,
    IPlatformSettings settings,
    ISaasNotifier notifier,
    ISaasAudit audit,
    TimeProvider time) : IInvoiceService
{
    public async Task<SaasInvoice> CreateForPeriodAsync(TenantSubscription subscription, BillingTerms terms,
        InvoicePurpose purpose, DateTime periodStart, DateTime? dueDate = null, string? actor = null)
    {
        var existing = await db.SaasInvoices.Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.SubscriptionId == subscription.Id && i.PeriodStart == periodStart && i.Status != InvoiceStatus.Cancelled);
        if (existing is not null)
        {
            return existing;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == subscription.TenantId);
        var amounts = BillingMath.Compute(terms.Price, terms.DiscountPercent, terms.TaxPercent);
        var periodEnd = BillingMath.AddInterval(periodStart, terms.IntervalCount, terms.IntervalUnit);
        var dueDays = await settings.GetIntAsync(PlatformSettingKeys.InvoiceDueDays, 7);
        var prefix = await settings.GetAsync(PlatformSettingKeys.InvoicePrefix);

        var invoice = new SaasInvoice
        {
            // Placeholder, replaced below by a number derived from the generated Id - unique without a counter table.
            InvoiceNumber = Guid.NewGuid().ToString("N"),
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            PlanId = terms.PlanId,
            PlanName = terms.PlanName,
            Purpose = purpose,
            Status = InvoiceStatus.Pending,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IssueDate = now,
            DueDate = dueDate ?? now.AddDays(dueDays),
            Currency = terms.Currency,
            SubTotal = amounts.SubTotal,
            DiscountTotal = amounts.Discount,
            TaxTotal = amounts.Tax,
            Total = amounts.Total,
            BillToName = tenant.Name,
            BillToEmail = tenant.BillingEmail ?? tenant.ContactEmail,
            BillToAddress = string.Join(", ", new[] { tenant.Address, tenant.City, tenant.Country }.Where(s => !string.IsNullOrWhiteSpace(s))),
            BillToTaxId = tenant.TaxId,
            CreatedAt = now,
            CreatedBy = actor ?? audit.Actor
        };

        invoice.Items.Add(new SaasInvoiceItem
        {
            ItemType = InvoiceItemType.Subscription,
            Description = $"{terms.PlanName} subscription, {BillingMath.DescribeInterval(terms.IntervalCount, terms.IntervalUnit)} ({periodStart:dd MMM yyyy} - {periodEnd:dd MMM yyyy})",
            Quantity = 1, UnitPrice = amounts.SubTotal, Amount = amounts.SubTotal
        });
        if (amounts.Discount > 0)
        {
            invoice.Items.Add(new SaasInvoiceItem { ItemType = InvoiceItemType.Discount, Description = $"Discount {terms.DiscountPercent:0.##}%", Quantity = 1, UnitPrice = -amounts.Discount, Amount = -amounts.Discount });
        }
        if (amounts.Tax > 0)
        {
            invoice.Items.Add(new SaasInvoiceItem { ItemType = InvoiceItemType.Tax, Description = $"Tax {terms.TaxPercent:0.##}%", Quantity = 1, UnitPrice = amounts.Tax, Amount = amounts.Tax });
        }

        db.SaasInvoices.Add(invoice);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A concurrent request created the invoice for this period first (unique index on subscription + period).
            db.Entry(invoice).State = EntityState.Detached;
            foreach (var item in invoice.Items)
            {
                db.Entry(item).State = EntityState.Detached;
            }
            var winner = await db.SaasInvoices.Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.SubscriptionId == subscription.Id && i.PeriodStart == periodStart && i.Status != InvoiceStatus.Cancelled);
            return winner ?? throw new InvalidOperationException("Invoice could not be created.");
        }

        invoice.InvoiceNumber = $"{(string.IsNullOrWhiteSpace(prefix) ? "SINV" : prefix)}-{now:yyyyMM}-{invoice.Id:D6}";
        audit.Add("invoice.created", nameof(SaasInvoice), invoice.Id.ToString(), invoice.TenantId,
            details: $"{invoice.InvoiceNumber} {purpose} total {invoice.Total:0.00} {invoice.Currency}, period {periodStart:yyyy-MM-dd}");
        await notifier.QueueAsync(invoice.TenantId, "invoice-generated",
            $"Invoice {invoice.InvoiceNumber} issued",
            $"Invoice {invoice.InvoiceNumber} for {invoice.PlanName} ({invoice.Total:0.00} {invoice.Currency}) is due on {invoice.DueDate:dd MMM yyyy}.",
            $"invoice-generated:{invoice.Id}");
        await db.SaveChangesAsync();
        return invoice;
    }

    public async Task AdjustAsync(int invoiceId, string description, decimal amount, string? actor)
    {
        amount = BillingMath.Round(amount);
        if (amount == 0 || string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException("Enter a description and a non-zero amount.");
        }
        var invoice = await db.SaasInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status is not (InvoiceStatus.Pending or InvoiceStatus.Overdue) || invoice.AmountPaid > 0)
        {
            throw new InvalidOperationException("Only an unpaid, open invoice can be adjusted.");
        }
        if (invoice.Total + amount <= 0)
        {
            throw new InvalidOperationException("An adjustment cannot bring the invoice to zero or below. Cancel the invoice instead.");
        }

        invoice.Items.Add(new SaasInvoiceItem { ItemType = InvoiceItemType.Adjustment, Description = description.Trim(), Quantity = 1, UnitPrice = amount, Amount = amount });
        if (amount < 0) invoice.DiscountTotal += -amount; else invoice.SubTotal += amount;
        invoice.Total += amount;
        audit.Add("invoice.adjusted", nameof(SaasInvoice), invoice.Id.ToString(), invoice.TenantId, details: $"{invoice.InvoiceNumber}: {amount:+0.00;-0.00} {invoice.Currency} - {description.Trim()}");
        await db.SaveChangesAsync();
    }

    public async Task CancelAsync(int invoiceId, string? reason, string? actor)
    {
        var invoice = await db.SaasInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled || invoice.AmountPaid > 0)
        {
            throw new InvalidOperationException("Only an unpaid invoice can be cancelled.");
        }

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancelledAt = time.GetUtcNow().UtcDateTime;
        audit.Add("invoice.cancelled", nameof(SaasInvoice), invoice.Id.ToString(), invoice.TenantId, details: reason);
        await db.SaveChangesAsync();
    }
}
