using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.Common;

// Single-row table holding the branding shown across the app: page titles, the sidebar
// brand, the login screen, the footer and the company header printed on report PDFs.
// Only one row is ever kept; ICompanySettingsService reads (and caches) the first one.
public class CompanySetting : BaseEntity
{
    [Required, StringLength(200), Display(Name = "Company Name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required, StringLength(50), Display(Name = "Short Name")]
    public string ShortName { get; set; } = string.Empty;

    [StringLength(300), Display(Name = "Address")]
    public string? Address { get; set; }

    [StringLength(50), Display(Name = "Phone")]
    public string? Phone { get; set; }

    // The logo lives in the database rather than on disk so the RDLC renderer can bind it as a
    // "Database" image source, which needs the raw bytes and their content type.
    public byte[]? LogoImage { get; set; }

    [StringLength(100)]
    public string? LogoMimeType { get; set; }
}
