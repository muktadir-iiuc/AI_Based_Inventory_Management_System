namespace WebApplication1.Models.Identity;

public class RolePermission
{
    public int Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string PermissionKey { get; set; } = string.Empty;
}

/// <summary>A permission granted to one specific user (Users &amp; Roles → Edit), independent of their roles.</summary>
public class UserPermission
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}

/// <summary>
/// Grantable, DB-backed permissions layered on top of the fixed roles: Admin and Manager always
/// have every permission; anyone else only gains one when it is granted to them individually
/// (UserPermission — the Page Permissions screen or the user's Edit page). RolePermission is legacy
/// and no longer read. Add new entries to `All` to make a new page assignable; it then appears as a
/// column on the Page Permissions screen automatically.
/// </summary>
public static class Permissions
{
    public const string PurchaseInvoice = "PurchaseInvoice.View";
    public const string SalesInvoice = "SalesInvoice.View";
    public const string StockTransfer = "StockTransfer.Manage";
    public const string StockAdjustment = "StockAdjustment.Manage";

    public static readonly (string Key, string Label, string Description)[] All =
    [
        (PurchaseInvoice, "Purchase Invoices", "See the Purchase Invoices and Purchase Returns pages (creating them still needs the Purchase Officer role). Never available to users who can't see purchase prices (Sales Officers, Viewers)."),
        (SalesInvoice, "Sales Invoices", "See the Sales Invoices and Sales Returns pages (creating them still needs the Sales Officer role)."),
        (StockTransfer, "Stock Transfers", "See the Stock Transfers menu and request inter-warehouse transfers (a Manager/Admin approves them)."),
        (StockAdjustment, "Stock Adjustments", "See the Stock Adjustments menu and request stock corrections (a Manager/Admin approves them).")
    ];

    /// <summary>Pages ticked automatically for a new user of this role (an Admin/Manager can untick them).</summary>
    public static string[] DefaultsFor(string role) => role switch
    {
        Roles.PurchaseOfficer => [PurchaseInvoice],
        Roles.SalesOfficer => [SalesInvoice],
        _ => []
    };

    /// <summary>
    /// Whether a user holding these roles may be given this page at all. Purchase Invoices are
    /// full of purchase prices, so they can't be granted to someone without a cost-viewing role.
    /// </summary>
    public static bool IsGrantable(string key, IEnumerable<string> roles) =>
        key != PurchaseInvoice || roles.Any(r => Roles.PurchasePriceViewers.Split(',').Contains(r));
}
