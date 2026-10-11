using System.Security.Claims;
using WebApplication1.Models.Identity;

namespace WebApplication1.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user's tenant id from the TenantId claim; 0 for platform staff or anonymous.</summary>
    public static int GetTenantId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("TenantId")?.Value, out var id) && id > 0 ? id : 0;

    public static bool IsPlatformStaff(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.PlatformAdmin) || user.IsInRole(Roles.PlatformBilling);

    /// <summary>Null for Admin/Manager (unrestricted); otherwise the warehouses this user's activity is scoped to.</summary>
    public static List<int>? GetWarehouseIds(this ClaimsPrincipal user)
    {
        var values = user.FindAll("WarehouseId").Select(c => c.Value).ToList();
        return values.Count == 0 ? null : values.Select(int.Parse).ToList();
    }

    /// <summary>True if the user is unrestricted, or the warehouse is one of their assigned warehouses.</summary>
    public static bool IsWarehouseAllowed(this ClaimsPrincipal user, int warehouseId)
    {
        var ids = user.GetWarehouseIds();
        return ids is null || ids.Contains(warehouseId);
    }

    /// <summary>
    /// Purchase (cost) prices are hidden from Sales Officers and Viewers: only Admin, Manager,
    /// Purchase Officer and Accountant see them. A user who also holds one of those roles sees them.
    /// </summary>
    public static bool CanSeePurchasePrice(this ClaimsPrincipal user) =>
        Roles.PurchasePriceViewers.Split(',').Any(user.IsInRole);

    /// <summary>Customer Ledger, customer dues and receivables — hidden from Purchase Officers.
    /// A user who also holds an allowed role (e.g. Sales Officer) sees them.</summary>
    public static bool CanSeeCustomerLedger(this ClaimsPrincipal user) =>
        Roles.CustomerLedgerViewers.Split(',').Any(user.IsInRole);

    /// <summary>Supplier Ledger, supplier dues and payables — hidden from Sales Officers and Viewers.</summary>
    public static bool CanSeeSupplierLedger(this ClaimsPrincipal user) =>
        Roles.SupplierLedgerViewers.Split(',').Any(user.IsInRole);
}
