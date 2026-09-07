using System.Security.Claims;

namespace WebApplication1.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Null for Admin/Manager (unrestricted); otherwise the single warehouse this user's activity is scoped to.</summary>
    public static int? GetWarehouseId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst("WarehouseId")?.Value;
        return int.TryParse(value, out var id) ? id : null;
    }
}
