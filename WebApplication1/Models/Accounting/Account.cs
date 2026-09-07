using System.ComponentModel.DataAnnotations;
using WebApplication1.Models.Common;

namespace WebApplication1.Models.Accounting;

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Income = 4,
    Expense = 5
}

public class Account : BaseEntity
{
    [Required, StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public AccountType Type { get; set; }

    public bool IsSystemAccount { get; set; }

    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = [];
}

public static class SystemAccountCodes
{
    public const string Cash = "1000";
    public const string Inventory = "1100";
    public const string AccountsReceivable = "1200";
    public const string AccountsPayable = "2000";
    public const string OwnersEquity = "3000";
    public const string SalesRevenue = "4000";
    public const string CostOfGoodsSold = "5000";
}
