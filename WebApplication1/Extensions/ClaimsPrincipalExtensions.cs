using System.Security.Claims;

namespace WebApplication1.Extensions;

public static class ClaimsPrincipalExtensions
{
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
}
