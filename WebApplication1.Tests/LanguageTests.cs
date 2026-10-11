using System.Text.Json;
using WebApplication1.Services;

namespace WebApplication1.Tests;

public class LanguageTests
{
    [Theory]
    [InlineData("bn", "bn")]
    [InlineData("BANGLA", "bn")]
    [InlineData("en", "en")]
    [InlineData("English", "en")]
    [InlineData("fr", null)]
    [InlineData(null, null)]
    public void UiLanguage_normalizes_codes(string? input, string? expected) =>
        Assert.Equal(expected, UiLanguage.Normalize(input));

    [Fact]
    public void Receipt_language_resolution_prefers_request_then_remembered_then_english()
    {
        Assert.Equal(ReceiptLanguage.Bangla, ReceiptText.Resolve("bn", "en"));
        Assert.Equal(ReceiptLanguage.Bangla, ReceiptText.Resolve(null, "bn"));
        Assert.Equal(ReceiptLanguage.English, ReceiptText.Resolve(null, null));
        Assert.Equal(ReceiptLanguage.English, ReceiptText.Resolve("zz", null));
    }

    [Fact]
    public void Receipt_digits_become_bangla_only_for_bangla()
    {
        Assert.Equal("১২৩.৫০", ReceiptText.Digits("123.50", ReceiptLanguage.Bangla));
        Assert.Equal("123.50", ReceiptText.Digits("123.50", ReceiptLanguage.English));
    }

    [Fact]
    public void Dictionary_file_is_valid_and_covers_core_ui()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WebApplication1", "wwwroot", "i18n", "bn.js");
        var js = File.ReadAllText(path);
        const string prefix = "window.IMS_I18N=";
        Assert.StartsWith(prefix, js);
        using var doc = JsonDocument.Parse(js[prefix.Length..].TrimEnd().TrimEnd(';'));
        var dict = doc.RootElement.GetProperty("dict");
        Assert.True(dict.EnumerateObject().Count() > 1000);
        foreach (var key in new[] { "Dashboard", "Save", "Cancel", "Sales Invoices", "Products", "Logout" })
            Assert.True(dict.TryGetProperty(key, out var v) && v.GetString()!.Length > 0, key);
        foreach (var rule in doc.RootElement.GetProperty("rules").EnumerateArray())
            _ = new System.Text.RegularExpressions.Regex(rule[0].GetString()!);   // every rule must compile
    }
}
