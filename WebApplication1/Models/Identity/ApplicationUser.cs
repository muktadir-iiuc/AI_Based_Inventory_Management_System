using Microsoft.AspNetCore.Identity;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null means unrestricted (Admin/Manager); otherwise the single warehouse this user's activity is scoped to.</summary>
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
}
