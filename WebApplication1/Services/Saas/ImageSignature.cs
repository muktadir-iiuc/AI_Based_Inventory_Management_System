namespace WebApplication1.Services.Saas;

/// <summary>Identifies an uploaded logo from its first bytes. A browser-supplied content type is only a claim; the file itself decides.</summary>
public static class ImageSignature
{
    /// <returns>"image/png", "image/jpeg", "image/gif" or "image/bmp" - or null for anything else (SVG, HTML, scripts, ...).</returns>
    public static string? Detect(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (b.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return "image/gif";
        if (b.StartsWith("BM"u8)) return "image/bmp";
        return null;
    }
}
