using System.Security.Cryptography;

namespace WebApplication1.Services.Saas;

/// <summary>Random temporary passwords that satisfy the application's password rules (upper, lower, digit, symbol, 8+).</summary>
public static class PasswordGenerator
{
    // No look-alike characters (0/O, 1/l/I) so the password can be read out or typed from a printout.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*?";

    public static string Create(int length = 14)
    {
        length = Math.Max(length, 10);
        var all = Upper + Lower + Digits + Symbols;
        var chars = new List<char>
        {
            Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols)
        };
        while (chars.Count < length) chars.Add(Pick(all));

        // Fisher-Yates so the four guaranteed characters are not always first.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string([.. chars]);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
