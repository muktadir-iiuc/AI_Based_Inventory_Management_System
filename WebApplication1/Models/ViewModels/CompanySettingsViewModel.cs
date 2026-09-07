using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels;

public class CompanySettingsViewModel
{
    [Required, StringLength(200), Display(Name = "Company Name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required, StringLength(50), Display(Name = "Short Name")]
    public string ShortName { get; set; } = string.Empty;

    [StringLength(300), Display(Name = "Address")]
    public string? Address { get; set; }

    [StringLength(50), Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Display(Name = "Logo")]
    public IFormFile? LogoFile { get; set; }

    [Display(Name = "Remove the current logo")]
    public bool RemoveLogo { get; set; }

    public bool HasLogo { get; set; }
}
