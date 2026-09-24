using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

/// <summary>
/// Answers natural-language questions using only data read live from the database - it matches
/// the question to a known intent and a real query, and never invents facts. Anything it can't
/// map to a known intent gets an explicit "I don't know" rather than a guess.
/// </summary>
public class ChatbotService(ApplicationDbContext db) : IChatbotService
{
    public async Task<ChatbotAnswer> AskAsync(string question, List<int>? warehouseIds)
    {
        var q = (question ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(q) || q is "hi" or "hello" or "hey" or "help" ||
            Has(q, "what can you do", "what can you answer", "what do you know"))
        {
            return Help();
        }

        var product = await FindProductAsync(q);

        if (product is not null && Has(q, "price", "cost", "sell for", "sale price", "cost price", "how much is", "how much does"))
        {
            return await ProductPriceAnswerAsync(product, warehouseIds);
        }

        if (product is not null && Has(q, "stock", "quantity", "available", "in hand", "how many", "left", "have"))
        {
            return await ProductStockAnswerAsync(product, warehouseIds);
        }

        var duesKeywords = new[] { "due", "dues", "owe", "owes", "outstanding", "balance", "unpaid", "pending payment", "pending amount" };

        var customer = await FindCustomerAsync(q);
        if (customer is not null && Has(q, duesKeywords))
        {
            return await CustomerDuesAnswerAsync(customer, warehouseIds);
        }

        var supplier = await FindSupplierAsync(q);
        if (supplier is not null && Has(q, duesKeywords))
        {
            return await SupplierDuesAnswerAsync(supplier, warehouseIds);
        }

        if (Has(q, "out of stock", "zero stock", "no stock left", "finished stock"))
        {
            return await OutOfStockAnswerAsync(warehouseIds);
        }

        if (Has(q, "reorder", "restock", "running low", "need to order", "need restocking") || HasAll(q, "low", "stock"))
        {
            return await LowStockAnswerAsync(warehouseIds);
        }

        var ranking = Has(q, "top", "best", "biggest", "largest", "most");
        if (ranking && q.Contains("customer"))
        {
            return await TopCustomersAnswerAsync(q, warehouseIds);
        }

        if (ranking && q.Contains("supplier"))
        {
            return await TopSuppliersAnswerAsync(q, warehouseIds);
        }

        if (ranking && Has(q, "sell", "sold", "product"))
        {
            return await TopSellingProductsAnswerAsync(q, warehouseIds);
        }

        if (Has(q, "receivable", "customers owe", "owed by customer", "customer due", "customer balance", "customer outstanding"))
        {
            return await AccountBalanceAnswerAsync("Accounts Receivable (money customers owe us)", SystemAccountCodes.AccountsReceivable, debitPositive: true);
        }

        if (Has(q, "payable", "owe supplier", "supplier due", "supplier balance", "we owe", "supplier outstanding"))
        {
            return await AccountBalanceAnswerAsync("Accounts Payable (money we owe suppliers)", SystemAccountCodes.AccountsPayable, debitPositive: false);
        }

        if (Has(q, "cash balance", "how much cash", "cash in hand", "cash on hand"))
        {
            return await AccountBalanceAnswerAsync("Cash balance", SystemAccountCodes.Cash, debitPositive: true);
        }

        if (Has(q, "invoice"))
        {
            var invoiceAnswer = await InvoiceLookupAnswerAsync(q, warehouseIds);
            if (invoiceAnswer is not null)
            {
                return invoiceAnswer;
            }
        }

        if (Has(q, "recent sale", "latest sale", "recent sales invoice", "last sale"))
        {
            return await RecentInvoicesAnswerAsync(isSales: true, warehouseIds);
        }

        if (Has(q, "recent purchase", "latest purchase", "recent purchase invoice", "last purchase"))
        {
            return await RecentInvoicesAnswerAsync(isSales: false, warehouseIds);
        }

        if (Has(q, "sale", "sold", "revenue") && !Has(q, "purchase"))
        {
            return await SalesTotalAnswerAsync(q, warehouseIds);
        }

        if (Has(q, "purchase", "bought", "spent on"))
        {
            return await PurchaseTotalAnswerAsync(q, warehouseIds);
        }

        if (Has(q, "how many product", "number of product", "total product", "count of product", "products do we have", "products are there"))
        {
            return await CountAnswerAsync("product", "products", db.Products.Where(p => p.IsActive).CountAsync());
        }

        if (Has(q, "how many categor", "number of categor", "total categor"))
        {
            return await CountAnswerAsync("category", "categories", db.Categories.Where(c => c.IsActive).CountAsync());
        }

        if (Has(q, "how many customer", "number of customer", "total customer"))
        {
            return await CountAnswerAsync("customer", "customers", db.Customers.Where(c => c.IsActive).CountAsync());
        }

        if (Has(q, "how many supplier", "number of supplier", "total supplier"))
        {
            return await CountAnswerAsync("supplier", "suppliers", db.Suppliers.Where(s => s.IsActive).CountAsync());
        }

        if (Has(q, "how many warehouse", "number of warehouse", "total warehouse"))
        {
            return await CountAnswerAsync("warehouse", "warehouses", db.Warehouses.Where(w => w.IsActive).CountAsync());
        }

        if (product is not null)
        {
            return await ProductStockAnswerAsync(product, warehouseIds);
        }

        return Unknown();
    }

    private static bool Has(string q, params string[] keywords) => keywords.Any(q.Contains);

    private static bool HasAll(string q, params string[] keywords) => keywords.All(q.Contains);

    private static ChatbotAnswer Help() => new()
    {
        Text = "I can only answer questions using real data from this system's database - I won't make anything up. Try asking things like:",
        Lines =
        [
            "How many <product name> are in stock?",
            "What's low on stock / what needs reordering?",
            "What's out of stock?",
            "What are the top selling products this month?",
            "Who are our top customers?",
            "What's our total sales today / this week / this month / this year?",
            "What's our total purchases this month?",
            "What's our cash balance?",
            "How much do customers owe us? / How much do we owe suppliers?",
            "What are the current dues of <customer name>? / How much do we owe <supplier name>?",
            "How many products/customers/suppliers/warehouses do we have?",
            "Show me recent sales invoices / recent purchase invoices.",
            "Show me invoice SI-0001 (any real invoice number)."
        ]
    };

    private static ChatbotAnswer Unknown() => new()
    {
        Text = "I couldn't match that to anything I can look up in the system. I only answer from real data in this database, so I won't guess. Ask me for help to see example questions I can answer."
    };

    private async Task<Product?> FindProductAsync(string q)
    {
        var candidates = await db.Products.Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Sku, p.Name })
            .ToListAsync();

        var tokens = q.Split([' ', ',', '?', '.', '!', '\'', '"', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

        var bySku = candidates.FirstOrDefault(p => tokens.Contains(p.Sku.ToLowerInvariant()));
        var match = bySku ?? candidates
            .Where(p => p.Name.Length >= 3 && q.Contains(p.Name.ToLowerInvariant()))
            .OrderByDescending(p => p.Name.Length)
            .FirstOrDefault();

        return match is null
            ? null
            : await db.Products.Include(p => p.UnitOfMeasure).FirstOrDefaultAsync(p => p.Id == match.Id);
    }

    private async Task<Models.Sales.Customer?> FindCustomerAsync(string q)
    {
        var candidates = await db.Customers.Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        var match = candidates
            .Where(c => c.Name.Length >= 3 && q.Contains(c.Name.ToLowerInvariant()))
            .OrderByDescending(c => c.Name.Length)
            .FirstOrDefault();

        return match is null ? null : await db.Customers.FirstOrDefaultAsync(c => c.Id == match.Id);
    }

    private async Task<Supplier?> FindSupplierAsync(string q)
    {
        var candidates = await db.Suppliers.Where(s => s.IsActive)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();

        var match = candidates
            .Where(s => s.Name.Length >= 3 && q.Contains(s.Name.ToLowerInvariant()))
            .OrderByDescending(s => s.Name.Length)
            .FirstOrDefault();

        return match is null ? null : await db.Suppliers.FirstOrDefaultAsync(s => s.Id == match.Id);
    }

    // Same opening-balance-plus-invoiced-minus-paid-minus-returned formula as
    // Customer.OutstandingDue / the Customer Ledger - a sales return credits Accounts Receivable
    // directly and never creates a refund Payment, so it has to be subtracted here too or the
    // chatbot would overstate the due for any customer who has returned goods. The opening
    // balance and payments against it aren't warehouse-scoped (same as the ledger).
    private async Task<ChatbotAnswer> CustomerDuesAnswerAsync(Models.Sales.Customer customer, List<int>? warehouseIds)
    {
        var invoices = await db.SalesInvoices.Include(s => s.Items).Include(s => s.Payments)
            .Include(s => s.Returns).ThenInclude(r => r.Items)
            .Where(s => s.CustomerId == customer.Id && s.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(s.WarehouseId)))
            .ToListAsync();

        var openingPaid = await db.Payments
            .Where(p => p.SalesInvoiceId == null && p.CustomerId == customer.Id)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        var totalInvoiced = invoices.Sum(s => s.TotalAmount);
        var totalPaid = invoices.Sum(s => s.Payments.Sum(p => p.Amount)) + openingPaid;
        var totalReturned = invoices.Sum(s => s.Returns.Sum(r => r.TotalAmount));
        var due = customer.OpeningBalance + totalInvoiced - totalPaid - totalReturned;
        var breakdown = DuesBreakdown(customer.OpeningBalance, totalInvoiced, totalPaid, totalReturned);

        return new ChatbotAnswer
        {
            Text = due <= 0
                ? $"{customer.Name} has no outstanding dues. {breakdown}."
                : $"{customer.Name} currently owes {due:C} ({breakdown})."
        };
    }

    // Mirror of CustomerDuesAnswerAsync for the payable side: a purchase return debits
    // Accounts Payable directly and never creates a refund Payment.
    private async Task<ChatbotAnswer> SupplierDuesAnswerAsync(Supplier supplier, List<int>? warehouseIds)
    {
        var invoices = await db.PurchaseInvoices.Include(p => p.Items).Include(p => p.Payments)
            .Include(p => p.Returns).ThenInclude(r => r.Items)
            .Where(p => p.SupplierId == supplier.Id && p.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(p.WarehouseId)))
            .ToListAsync();

        var openingPaid = await db.Payments
            .Where(p => p.PurchaseInvoiceId == null && p.SupplierId == supplier.Id)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        var totalInvoiced = invoices.Sum(p => p.TotalAmount);
        var totalPaid = invoices.Sum(p => p.Payments.Sum(pay => pay.Amount)) + openingPaid;
        var totalReturned = invoices.Sum(p => p.Returns.Sum(r => r.TotalAmount));
        var due = supplier.OpeningBalance + totalInvoiced - totalPaid - totalReturned;
        var breakdown = DuesBreakdown(supplier.OpeningBalance, totalInvoiced, totalPaid, totalReturned);

        return new ChatbotAnswer
        {
            Text = due <= 0
                ? $"We have no outstanding dues to {supplier.Name}. {breakdown}."
                : $"We currently owe {supplier.Name} {due:C} ({breakdown})."
        };
    }

    // Opening balance and returns are only mentioned when nonzero, to keep the common case short.
    private static string DuesBreakdown(decimal openingBalance, decimal totalInvoiced, decimal totalPaid, decimal totalReturned)
    {
        var parts = new List<string>();
        if (openingBalance > 0)
        {
            parts.Add($"opening balance {openingBalance:C}");
        }
        else if (openingBalance < 0)
        {
            parts.Add($"opening advance {-openingBalance:C}");
        }
        parts.Add($"total invoiced {totalInvoiced:C}");
        parts.Add($"paid {totalPaid:C}");
        if (totalReturned > 0)
        {
            parts.Add($"returned {totalReturned:C}");
        }
        return string.Join(", ", parts);
    }

    private static string UnitLabel(Product product) => product.UnitOfMeasure?.Symbol ?? product.UnitOfMeasure?.Name ?? "unit(s)";

    private async Task<ChatbotAnswer> ProductStockAnswerAsync(Product product, List<int>? warehouseIds)
    {
        if (warehouseIds is not null)
        {
            var stock = await db.ProductWarehouseStocks
                .Where(s => s.ProductId == product.Id && warehouseIds.Contains(s.WarehouseId))
                .SumAsync(s => (decimal?)s.Quantity) ?? 0;
            var note = stock <= product.ReorderLevel ? " That's at or below its reorder level." : "";

            if (warehouseIds.Count == 1)
            {
                var warehouseName = await db.Warehouses.Where(w => w.Id == warehouseIds[0]).Select(w => w.Name).FirstOrDefaultAsync();
                return new ChatbotAnswer { Text = $"{product.Name} ({product.Sku}) has {stock:0.##} {UnitLabel(product)} in stock at {warehouseName}.{note}" };
            }

            return new ChatbotAnswer { Text = $"{product.Name} ({product.Sku}) has {stock:0.##} {UnitLabel(product)} in stock across your assigned warehouses.{note}" };
        }

        var totalNote = product.CurrentStock <= product.ReorderLevel ? " That's at or below its reorder level." : "";
        return new ChatbotAnswer { Text = $"{product.Name} ({product.Sku}) has {product.CurrentStock:0.##} {UnitLabel(product)} in stock across all warehouses.{totalNote}" };
    }

    // Selling price now lives on whichever batch is next to be consumed (FIFO), not on the
    // product itself — Product.SalePrice/CostPrice are only ever a default suggested at
    // purchase time, never authoritative for what something actually sells for.
    private async Task<ChatbotAnswer> ProductPriceAnswerAsync(Product product, List<int>? warehouseIds)
    {
        var batchesQuery = db.ProductBatches
            .Where(b => b.ProductId == product.Id && b.IsActive && b.RemainingQuantity > 0);
        if (warehouseIds is not null)
        {
            batchesQuery = batchesQuery.Where(b => warehouseIds.Contains(b.WarehouseId));
        }

        var batches = await batchesQuery
            .OrderBy(b => b.PurchaseDate).ThenBy(b => b.Id)
            .Select(b => new { b.PurchasePrice, b.SalePrice })
            .ToListAsync();

        if (batches.Count == 0)
        {
            return new ChatbotAnswer { Text = $"{product.Name} ({product.Sku}) has no stock batches to price right now." };
        }

        var next = batches[0];
        var distinctSalePrices = batches.Select(b => b.SalePrice).Distinct().Count();
        var note = distinctSalePrices > 1
            ? $" ({batches.Count} batches in stock at different prices — this is the next one to be sold, oldest first)"
            : "";

        return new ChatbotAnswer
        {
            Text = $"{product.Name} ({product.Sku}): current selling price {next.SalePrice:C}, cost {next.PurchasePrice:C}{note}."
        };
    }

    private async Task<ChatbotAnswer> OutOfStockAnswerAsync(List<int>? warehouseIds)
    {
        List<string> names;
        if (warehouseIds is not null)
        {
            names = await db.ProductWarehouseStocks
                .Include(s => s.Product)
                .Where(s => warehouseIds.Contains(s.WarehouseId) && s.Product!.IsActive && s.Quantity <= 0)
                .Select(s => s.Product!.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();
        }
        else
        {
            names = await db.Products
                .Where(p => p.IsActive && p.CurrentStock <= 0)
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToListAsync();
        }

        return names.Count == 0
            ? new ChatbotAnswer { Text = "Nothing is out of stock right now." }
            : new ChatbotAnswer { Text = $"{names.Count} product(s) are out of stock:", Lines = names };
    }

    private async Task<ChatbotAnswer> LowStockAnswerAsync(List<int>? warehouseIds)
    {
        List<string> lines;
        if (warehouseIds is not null)
        {
            lines = await db.ProductWarehouseStocks
                .Include(s => s.Product)
                .Where(s => warehouseIds.Contains(s.WarehouseId) && s.Product!.IsActive && s.Quantity <= s.Product!.ReorderLevel)
                .OrderBy(s => s.Quantity)
                .Select(s => $"{s.Product!.Name}: {s.Quantity:0.##} in stock (reorder level {s.Product!.ReorderLevel:0.##})")
                .ToListAsync();
        }
        else
        {
            lines = await db.Products
                .Where(p => p.IsActive && p.CurrentStock <= p.ReorderLevel)
                .OrderBy(p => p.CurrentStock)
                .Select(p => $"{p.Name}: {p.CurrentStock:0.##} in stock (reorder level {p.ReorderLevel:0.##})")
                .ToListAsync();
        }

        return lines.Count == 0
            ? new ChatbotAnswer { Text = "Nothing is low on stock right now - everything is above its reorder level." }
            : new ChatbotAnswer { Text = $"{lines.Count} product(s) are at or below their reorder level:", Lines = lines };
    }

    private (DateTime? Start, DateTime? End, string Label) ParsePeriod(string q)
    {
        var today = DateTime.UtcNow.Date;

        if (Has(q, "today")) return (today, today, "today");
        if (Has(q, "yesterday")) return (today.AddDays(-1), today.AddDays(-1), "yesterday");
        if (Has(q, "this week")) return (today.AddDays(-(int)today.DayOfWeek), today, "this week");
        if (Has(q, "last week"))
        {
            var start = today.AddDays(-(int)today.DayOfWeek - 7);
            return (start, start.AddDays(6), "last week");
        }
        if (Has(q, "this month")) return (new DateTime(today.Year, today.Month, 1), today, "this month");
        if (Has(q, "last month"))
        {
            var firstOfThisMonth = new DateTime(today.Year, today.Month, 1);
            var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);
            return (firstOfLastMonth, firstOfThisMonth.AddDays(-1), "last month");
        }
        if (Has(q, "this year")) return (new DateTime(today.Year, 1, 1), today, "this year");

        return (null, null, "all time");
    }

    private async Task<ChatbotAnswer> SalesTotalAnswerAsync(string q, List<int>? warehouseIds)
    {
        var (start, end, label) = ParsePeriod(q);
        var total = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(i.SalesInvoice!.WarehouseId))
                        && (!start.HasValue || i.SalesInvoice!.Date.Date >= start)
                        && (!end.HasValue || i.SalesInvoice!.Date.Date <= end))
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0;

        return new ChatbotAnswer { Text = $"Total sales {label}: {total:C}." };
    }

    private async Task<ChatbotAnswer> PurchaseTotalAnswerAsync(string q, List<int>? warehouseIds)
    {
        var (start, end, label) = ParsePeriod(q);
        var total = await db.PurchaseInvoiceItems
            .Where(i => i.PurchaseInvoice!.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(i.PurchaseInvoice!.WarehouseId))
                        && (!start.HasValue || i.PurchaseInvoice!.Date.Date >= start)
                        && (!end.HasValue || i.PurchaseInvoice!.Date.Date <= end))
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0;

        return new ChatbotAnswer { Text = $"Total purchases {label}: {total:C}." };
    }

    private static int ParseTopN(string q)
    {
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i] is "top" && int.TryParse(tokens[i + 1], out var n) && n is > 0 and <= 20)
            {
                return n;
            }
        }
        return 5;
    }

    private async Task<ChatbotAnswer> TopSellingProductsAnswerAsync(string q, List<int>? warehouseIds)
    {
        var (start, end, label) = ParsePeriod(q);
        var top = ParseTopN(q);

        var rows = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(i.SalesInvoice!.WarehouseId))
                        && (!start.HasValue || i.SalesInvoice!.Date.Date >= start)
                        && (!end.HasValue || i.SalesInvoice!.Date.Date <= end))
            .GroupBy(i => i.Product!.Name)
            .Select(g => new { Name = g.Key, Qty = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.Quantity * i.UnitPrice) })
            .OrderByDescending(g => g.Qty)
            .Take(top)
            .ToListAsync();

        return rows.Count == 0
            ? new ChatbotAnswer { Text = $"No sales recorded {label}." }
            : new ChatbotAnswer
            {
                Text = $"Top {rows.Count} selling product(s) {label} (by quantity sold):",
                Lines = rows.Select(r => $"{r.Name}: {r.Qty:0.##} sold, {r.Revenue:C} revenue").ToList()
            };
    }

    private async Task<ChatbotAnswer> TopCustomersAnswerAsync(string q, List<int>? warehouseIds)
    {
        var (start, end, label) = ParsePeriod(q);
        var top = ParseTopN(q);

        var rows = await db.SalesInvoiceItems
            .Where(i => i.SalesInvoice!.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(i.SalesInvoice!.WarehouseId))
                        && (!start.HasValue || i.SalesInvoice!.Date.Date >= start)
                        && (!end.HasValue || i.SalesInvoice!.Date.Date <= end))
            .GroupBy(i => i.SalesInvoice!.Customer!.Name)
            .Select(g => new { Name = g.Key, Revenue = g.Sum(i => i.Quantity * i.UnitPrice) })
            .OrderByDescending(g => g.Revenue)
            .Take(top)
            .ToListAsync();

        return rows.Count == 0
            ? new ChatbotAnswer { Text = $"No sales recorded {label}." }
            : new ChatbotAnswer
            {
                Text = $"Top {rows.Count} customer(s) {label} (by revenue):",
                Lines = rows.Select(r => $"{r.Name}: {r.Revenue:C}").ToList()
            };
    }

    private async Task<ChatbotAnswer> TopSuppliersAnswerAsync(string q, List<int>? warehouseIds)
    {
        var (start, end, label) = ParsePeriod(q);
        var top = ParseTopN(q);

        var rows = await db.PurchaseInvoiceItems
            .Where(i => i.PurchaseInvoice!.Status == DocumentStatus.Posted
                        && (warehouseIds == null || warehouseIds.Contains(i.PurchaseInvoice!.WarehouseId))
                        && (!start.HasValue || i.PurchaseInvoice!.Date.Date >= start)
                        && (!end.HasValue || i.PurchaseInvoice!.Date.Date <= end))
            .GroupBy(i => i.PurchaseInvoice!.Supplier!.Name)
            .Select(g => new { Name = g.Key, Spend = g.Sum(i => i.Quantity * i.UnitPrice) })
            .OrderByDescending(g => g.Spend)
            .Take(top)
            .ToListAsync();

        return rows.Count == 0
            ? new ChatbotAnswer { Text = $"No purchases recorded {label}." }
            : new ChatbotAnswer
            {
                Text = $"Top {rows.Count} supplier(s) {label} (by amount purchased from them):",
                Lines = rows.Select(r => $"{r.Name}: {r.Spend:C}").ToList()
            };
    }

    private async Task<ChatbotAnswer> AccountBalanceAnswerAsync(string label, string accountCode, bool debitPositive)
    {
        var account = await db.Accounts.Include(a => a.JournalEntryLines).FirstOrDefaultAsync(a => a.Code == accountCode);
        if (account is null)
        {
            return new ChatbotAnswer { Text = $"{label} account isn't set up in the chart of accounts." };
        }

        var debit = account.JournalEntryLines.Sum(l => l.Debit);
        var credit = account.JournalEntryLines.Sum(l => l.Credit);
        var balance = debitPositive ? debit - credit : credit - debit;

        return new ChatbotAnswer { Text = $"{label}: {balance:C}." };
    }

    private async Task<ChatbotAnswer?> InvoiceLookupAnswerAsync(string q, List<int>? warehouseIds)
    {
        var tokens = q.Split([' ', ',', '?', '.', '!'], StringSplitOptions.RemoveEmptyEntries);
        var candidate = tokens.FirstOrDefault(t => t.Any(char.IsDigit) && t.Any(char.IsLetter));
        if (candidate is null)
        {
            return null;
        }

        var salesInvoice = await db.SalesInvoices.Include(s => s.Customer).Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => (warehouseIds == null || warehouseIds.Contains(s.WarehouseId)) && s.InvoiceNumber.ToLower() == candidate)
            .FirstOrDefaultAsync();
        if (salesInvoice is not null)
        {
            return new ChatbotAnswer
            {
                Text = $"Sales invoice {salesInvoice.InvoiceNumber} - {salesInvoice.Customer?.Name}, {salesInvoice.Date:d}, status {salesInvoice.Status}, total {salesInvoice.TotalAmount:C}.",
                Lines = salesInvoice.Items.Select(i => $"{i.Quantity:0.##} x {i.Product?.Name ?? "(product)"} @ {i.UnitPrice:C}").ToList()
            };
        }

        var purchaseInvoice = await db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Items).ThenInclude(i => i.Product)
            .Where(p => (warehouseIds == null || warehouseIds.Contains(p.WarehouseId)) && p.InvoiceNumber.ToLower() == candidate)
            .FirstOrDefaultAsync();
        if (purchaseInvoice is not null)
        {
            return new ChatbotAnswer
            {
                Text = $"Purchase invoice {purchaseInvoice.InvoiceNumber} - {purchaseInvoice.Supplier?.Name}, {purchaseInvoice.Date:d}, status {purchaseInvoice.Status}, total {purchaseInvoice.TotalAmount:C}.",
                Lines = purchaseInvoice.Items.Select(i => $"{i.Quantity:0.##} x {i.Product?.Name ?? "(product)"} @ {i.UnitPrice:C}").ToList()
            };
        }

        return new ChatbotAnswer { Text = $"I couldn't find any invoice numbered \"{candidate}\" in the system." };
    }

    private async Task<ChatbotAnswer> RecentInvoicesAnswerAsync(bool isSales, List<int>? warehouseIds)
    {
        if (isSales)
        {
            var rows = await db.SalesInvoices.Include(s => s.Customer)
                .Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId))
                .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id)
                .Take(5)
                .Select(s => $"{s.InvoiceNumber} - {s.Customer!.Name}, {s.Date:d}, {s.Status}, {s.Items.Sum(i => i.Quantity * i.UnitPrice):C}")
                .ToListAsync();

            return rows.Count == 0
                ? new ChatbotAnswer { Text = "There are no sales invoices in the system yet." }
                : new ChatbotAnswer { Text = "Most recent sales invoices:", Lines = rows };
        }
        else
        {
            var rows = await db.PurchaseInvoices.Include(p => p.Supplier)
                .Where(p => warehouseIds == null || warehouseIds.Contains(p.WarehouseId))
                .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
                .Take(5)
                .Select(p => $"{p.InvoiceNumber} - {p.Supplier!.Name}, {p.Date:d}, {p.Status}, {p.Items.Sum(i => i.Quantity * i.UnitPrice):C}")
                .ToListAsync();

            return rows.Count == 0
                ? new ChatbotAnswer { Text = "There are no purchase invoices in the system yet." }
                : new ChatbotAnswer { Text = "Most recent purchase invoices:", Lines = rows };
        }
    }

    private static async Task<ChatbotAnswer> CountAnswerAsync(string singular, string plural, Task<int> countTask)
    {
        var count = await countTask;
        var noun = count == 1 ? singular : plural;
        return new ChatbotAnswer { Text = $"There {(count == 1 ? "is" : "are")} {count} active {noun} in the system." };
    }
}
