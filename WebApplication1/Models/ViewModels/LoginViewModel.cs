using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    /// <summary>Set when the page was opened from a tenant's own link (/t/{code}) or domain: only that tenant's users may sign in there.</summary>
    public string? TenantCode { get; set; }
    public string? TenantName { get; set; }

    /// <summary>What the page shows as its brand: the tenant's own company name and logo on a tenant link, the platform's name otherwise.</summary>
    public string BrandName { get; set; } = string.Empty;
    public string? BrandLogoUrl { get; set; }

    /// <summary>Set when a platform account tried the tenant page: points it to the platform sign-in instead.</summary>
    public bool ShowPlatformLink { get; set; }

    /// <summary>The brand was inferred (this browser's last business, or the only business) rather than chosen by the address: offer a way to another business.</summary>
    public bool ShowOtherBusinessLink { get; set; }

    public string? ErrorMessage { get; set; }
}
