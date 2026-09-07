using WebApplication1.Models.Common;

namespace WebApplication1.Services;

public interface ICompanySettingsService
{
    Task<CompanySetting> GetAsync();

    // A null LogoImage on <paramref name="values"/> keeps the stored logo untouched unless
    // removeLogo is set, so saving the form without picking a file never drops the logo.
    Task UpdateAsync(CompanySetting values, bool removeLogo, string? updatedBy);
}
