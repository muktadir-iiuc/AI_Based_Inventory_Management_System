namespace WebApplication1.Extensions;

// Amounts written out for printed documents, in the Bangladeshi (South Asian) grouping the
// currency uses: crore / lakh / thousand / hundred, e.g. 1,25,050.75 ->
// "Taka One Lakh Twenty-Five Thousand Fifty and Seventy-Five Paisa Only".
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
        ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string ToTakaWords(this decimal amount)
    {
        amount = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var taka = (long)Math.Truncate(amount);
        var paisa = (int)((amount - taka) * 100);

        var words = $"Taka {Words(taka)}";
        if (paisa > 0)
        {
            words += $" and {Words(paisa)} Paisa";
        }
        return words + " Only";
    }

    private static string Words(long n)
    {
        if (n == 0) return Ones[0];

        var parts = new List<string>();
        // Anything above 99 crore is itself spelled out in crore/lakh/..., e.g. "One Hundred Crore".
        if (n >= 10_000_000)
        {
            parts.Add(Words(n / 10_000_000) + " Crore");
            n %= 10_000_000;
        }
        if (n >= 100_000)
        {
            parts.Add(BelowHundred(n / 100_000) + " Lakh");
            n %= 100_000;
        }
        if (n >= 1_000)
        {
            parts.Add(BelowHundred(n / 1_000) + " Thousand");
            n %= 1_000;
        }
        if (n >= 100)
        {
            parts.Add(Ones[n / 100] + " Hundred");
            n %= 100;
        }
        if (n > 0)
        {
            parts.Add(BelowHundred(n));
        }
        return string.Join(" ", parts);
    }

    private static string BelowHundred(long n) =>
        n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? "-" + Ones[n % 10] : "");
}
