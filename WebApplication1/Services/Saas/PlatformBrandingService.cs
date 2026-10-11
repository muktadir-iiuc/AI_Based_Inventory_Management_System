using WebApplication1.Models.Saas;

namespace WebApplication1.Services.Saas;

public record PlatformLogo(byte[] Bytes, string ContentType);

public interface IPlatformBranding
{
    /// <summary>The platform operator's product name (Platform Settings). Shown on the platform sign-in page, platform screens and subscription invoices.</summary>
    Task<string> GetNameAsync();

    /// <summary>The uploaded platform logo, or null when none was uploaded.</summary>
    Task<PlatformLogo?> GetLogoAsync();

    /// <summary>Validates and stores a logo (PNG or JPEG, up to 512 KB, checked by file signature - the declared content type is not trusted). Returns an error or null.</summary>
    Task<string?> SaveLogoAsync(Stream content, long length);

    Task RemoveLogoAsync();
}

/// <summary>
/// The platform's own branding, kept completely apart from any tenant's company profile: a tenant sees its own name and logo,
/// the platform administrator sees this one. The logo lives in App_Data/Branding (not web-served directly).
/// </summary>
public class PlatformBrandingService(IPlatformSettings settings, IWebHostEnvironment env) : IPlatformBranding
{
    public const int MaxLogoBytes = 512 * 1024;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private string Folder => Path.Combine(env.ContentRootPath, "App_Data", "Branding");

    public async Task<string> GetNameAsync()
    {
        var name = (await settings.GetAsync(PlatformSettingKeys.PlatformName)).Trim();
        return name.Length > 0 ? name : PlatformSettingKeys.Defaults[PlatformSettingKeys.PlatformName];
    }

    public async Task<PlatformLogo?> GetLogoAsync()
    {
        foreach (var (file, type) in new[] { ("platform-logo.png", "image/png"), ("platform-logo.jpg", "image/jpeg") })
        {
            var path = Path.Combine(Folder, file);
            if (File.Exists(path))
            {
                return new PlatformLogo(await File.ReadAllBytesAsync(path), type);
            }
        }
        return null;
    }

    public async Task<string?> SaveLogoAsync(Stream content, long length)
    {
        if (length <= 0) return "Choose an image file.";
        if (length > MaxLogoBytes) return "The logo must be 512 KB or smaller.";

        var buffer = new byte[length];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await content.ReadAsync(buffer.AsMemory(read));
            if (n == 0) break;
            read += n;
        }
        if (read != buffer.Length) return "The file could not be read.";

        string extension;
        if (buffer.AsSpan().StartsWith(PngSignature)) extension = "png";
        else if (buffer.AsSpan().StartsWith(JpegSignature)) extension = "jpg";
        else return "Only PNG or JPEG images are accepted.";

        Directory.CreateDirectory(Folder);
        await RemoveLogoAsync();
        await File.WriteAllBytesAsync(Path.Combine(Folder, $"platform-logo.{extension}"), buffer);
        return null;
    }

    public Task RemoveLogoAsync()
    {
        foreach (var file in new[] { "platform-logo.png", "platform-logo.jpg" })
        {
            var path = Path.Combine(Folder, file);
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
