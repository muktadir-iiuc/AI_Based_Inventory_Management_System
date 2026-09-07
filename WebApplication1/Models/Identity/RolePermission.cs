namespace WebApplication1.Models.Identity;

public class RolePermission
{
    public int Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string PermissionKey { get; set; } = string.Empty;
}

/// <summary>
/// Grantable, DB-backed permissions layered on top of the fixed roles: Admin always has every
/// permission; other roles only gain one when a RolePermission row grants it (managed on the
/// Admin-only Permissions screen). Add new entries to `All` to make a new capability grantable.
/// </summary>
public static class Permissions
{
    public const string StockTransfer = "StockTransfer.Manage";

    public static readonly (string Key, string Label, string Description)[] All =
    [
        (StockTransfer, "Manage Stock Transfers", "Create and cancel inter-warehouse stock transfers.")
    ];
}
