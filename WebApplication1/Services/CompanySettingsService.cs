using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models.Common;

namespace WebApplication1.Services;

// The layout renders the company name on every single request, so the single settings row is
// cached and only re-read after an admin saves a change.
public class CompanySettingsService(ApplicationDbContext db, IMemoryCache cache) : ICompanySettingsService
{
    private const string CacheKey = "company-settings";

    public const string DefaultCompanyName = "Bangladesh Tyre";
    public const string DefaultShortName = "Bangladesh Tyre";

    public async Task<CompanySetting> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out CompanySetting? cached) && cached is not null)
        {
            return cached;
        }

        var setting = await db.CompanySettings.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync()
            ?? new CompanySetting { CompanyName = DefaultCompanyName, ShortName = DefaultShortName };

        cache.Set(CacheKey, setting);
        return setting;
    }

    public async Task UpdateAsync(CompanySetting values, bool removeLogo, string? updatedBy)
    {
        var setting = await db.CompanySettings.OrderBy(c => c.Id).FirstOrDefaultAsync();

        if (setting is null)
        {
            setting = new CompanySetting { CreatedBy = updatedBy };
            db.CompanySettings.Add(setting);
        }

        setting.CompanyName = values.CompanyName;
        setting.ShortName = values.ShortName;
        setting.Address = values.Address;
        setting.Phone = values.Phone;

        if (removeLogo)
        {
            setting.LogoImage = null;
            setting.LogoMimeType = null;
        }
        else if (values.LogoImage is not null)
        {
            setting.LogoImage = values.LogoImage;
            setting.LogoMimeType = values.LogoMimeType;
        }

        await db.SaveChangesAsync();

        cache.Remove(CacheKey);
    }
}
