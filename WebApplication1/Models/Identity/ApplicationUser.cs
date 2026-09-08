using Microsoft.AspNetCore.Identity;

namespace WebApplication1.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Empty means unrestricted (Admin/Manager); otherwise the warehouses this user's activity is scoped to.</summary>
    public ICollection<UserWarehouse> UserWarehouses { get; set; } = [];
}
