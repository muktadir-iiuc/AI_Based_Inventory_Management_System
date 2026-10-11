using Microsoft.AspNetCore.Identity;

namespace WebApplication1.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Set when a platform admin issued a temporary password: the user must choose their own before doing anything else.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Owning tenant. Null only for platform staff (PlatformAdmin / PlatformBilling).</summary>
    public int? TenantId { get; set; }
    /// <summary>Interface language the user chose: "en" or "bn". Null = never chose (English).</summary>
    public string? PreferredLanguage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC time of the most recent successful sign-in.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>UTC time this user last used the system (see UserActivityMiddleware). Null once they
    /// sign out, or if they have never signed in.</summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>Empty means unrestricted (Admin/Manager); otherwise the warehouses this user's activity is scoped to.</summary>
    public ICollection<UserWarehouse> UserWarehouses { get; set; } = [];
}
