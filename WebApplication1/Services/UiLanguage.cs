namespace WebApplication1.Services;

/// <summary>The language the whole interface is shown in. English unless the user chose Bangla; the choice lives in a cookie
/// (so it also applies on the sign-in pages) and in <c>ApplicationUser.PreferredLanguage</c> (so it follows the user to other devices).</summary>
public static class UiLanguage
{
    public const string CookieName = "ui_lang";
    public const string English = "en";
    public const string Bangla = "bn";

    /// <summary>Normalises a requested code to "en" or "bn"; anything else is null.</summary>
    public static string? Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "bn" or "bangla" or "bengali" => Bangla,
        "en" or "english" => English,
        _ => null
    };

    public static string Current(HttpContext? context) =>
        Normalize(context?.Request.Cookies[CookieName]) ?? English;

    public static bool IsBangla(HttpContext? context) => Current(context) == Bangla;

    public static void Remember(HttpResponse response, string code) =>
        response.Cookies.Append(CookieName, code, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(2),
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = false
        });
}
