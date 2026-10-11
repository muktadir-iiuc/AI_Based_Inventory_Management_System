using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

// Builds the variable-length middle section of an 80mm thermal receipt (optional
// customer details/notes, then the item list) as fixed-width monospaced lines.
// The header, invoice meta, totals, and footer are native RDLC fields with real
// fonts/weights (see SalesInvoiceThermalReceipt.rdlc) — only this middle section,
// whose row count depends on the invoice, needs to stay plain text.
public static class ThermalReceiptFormatter
{
    // Characters per line. The detail textbox is 3in wide in Consolas 8pt bold (0.55em = 4.4pt per
    // character), so 49 characters span 2.99in of the 3in box (48 leaves a visible gap). It was 42 (2.57in), which
    // left the detail section visibly short of the header and totals. Keep this <= 49 or lines wrap.
    public const int LineWidth = 49;

    public static List<SalesInvoiceThermalLine> BuildDetailLines(SalesInvoiceReportHeader header, IReadOnlyList<SalesInvoiceReportLine> items, ReceiptLanguage language = ReceiptLanguage.English)
    {
        if (language == ReceiptLanguage.Bangla)
        {
            return BuildBanglaDetailLines(header, items);
        }

        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(header.CustomerAddress))
        {
            lines.Add(KeyValue("Address", header.CustomerAddress));
        }
        if (!string.IsNullOrWhiteSpace(header.CustomerPhone))
        {
            lines.Add(KeyValue("Phone", header.CustomerPhone));
        }
        if (lines.Count > 0)
        {
            lines.Add(Separator('-'));
        }

        if (items.Count > 0)
        {
            lines.Add(TwoColumn("ITEM", "AMOUNT"));
            lines.Add(Separator('-'));
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            lines.Add(TwoColumn(item.ProductName, item.LineTotal));
            if (!string.IsNullOrWhiteSpace(item.Details))
            {
                lines.Add($"  {item.Details}");
            }
            lines.Add($"  {item.Sku}  {item.Quantity} x {item.UnitPrice}".TrimEnd());
            if (i < items.Count - 1)
            {
                lines.Add(string.Empty);
            }
        }

        // The native TOTAL row below shows the net amount; when a discount was given, show how it
        // got there. (Plain lines here — the RDLC's fixed totals block can't grow a conditional row.)
        if (items.Count > 0 && decimal.TryParse(header.DiscountAmount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var discount) && discount > 0)
        {
            lines.Add(Separator('-'));
            lines.Add(TwoColumn("Subtotal", header.SubtotalAmount));
            lines.Add(TwoColumn("Discount", "-" + header.DiscountAmount));
        }

        if (!string.IsNullOrWhiteSpace(header.Notes))
        {
            lines.Add(Separator('-'));
            lines.AddRange(WrapNote(header.Notes));
        }

        return lines.Select(text => new SalesInvoiceThermalLine { Text = text }).ToList();
    }

    // ------------------------------------------------------------------ Bangla

    // The English layout pads with spaces so amounts line up in a monospaced font. Bangla has no monospaced font and its letters combine
    // (vowel signs, conjuncts), so column padding and character counts do not work: this layout never pads. Each item is a short block -
    // name, optional details/SKU, then "qty x price = total" - and long text is wrapped by user-perceived characters (text elements).
    // Characters per wrapped line; Bangla glyphs are wider than Latin ones in a 3 inch box.
    public const int BanglaLineWidth = 32;
    private const int BanglaSeparatorWidth = 62;

    private static List<SalesInvoiceThermalLine> BuildBanglaDetailLines(SalesInvoiceReportHeader header, IReadOnlyList<SalesInvoiceReportLine> items)
    {
        var l = ReceiptText.For(ReceiptLanguage.Bangla);
        var separator = new string('-', BanglaSeparatorWidth);
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(header.CustomerAddress))
        {
            lines.AddRange(Wrap($"{l.Address}: {header.CustomerAddress}", BanglaLineWidth, "  "));
        }
        if (!string.IsNullOrWhiteSpace(header.CustomerPhone))
        {
            lines.Add($"{l.Phone}: {header.CustomerPhone}");
        }
        if (lines.Count > 0)
        {
            lines.Add(separator);
        }

        if (items.Count > 0)
        {
            lines.Add($"{l.Item}  /  {l.Amount}");
            lines.Add(separator);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            lines.AddRange(Wrap($"{ReceiptText.Digits((i + 1).ToString(), ReceiptLanguage.Bangla)}. {item.ProductName}", BanglaLineWidth, "    "));
            if (!string.IsNullOrWhiteSpace(item.Details))
            {
                lines.AddRange(Wrap(item.Details, BanglaLineWidth - 4, "    ").Select(x => "    " + x.TrimStart()));
            }
            if (!string.IsNullOrWhiteSpace(item.Sku))
            {
                lines.Add($"    {item.Sku}");
            }
            lines.Add($"    {item.Quantity} {l.Qty} {item.UnitPrice} = {item.LineTotal}");
            if (i < items.Count - 1)
            {
                lines.Add(string.Empty);
            }
        }

        if (items.Count > 0 && decimal.TryParse(Normalize(header.DiscountAmount), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var discount) && discount > 0)
        {
            lines.Add(separator);
            lines.Add($"{l.Subtotal}: {header.SubtotalAmount}");
            lines.Add($"{l.Discount}: -{header.DiscountAmount}");
        }

        if (!string.IsNullOrWhiteSpace(header.Notes))
        {
            lines.Add(separator);
            lines.AddRange(Wrap($"{l.Note}{header.Notes}", BanglaLineWidth, "  "));
        }

        return lines.Select(text => new SalesInvoiceThermalLine { Text = text }).ToList();
    }

    // Bangla digits back to 0-9 so the discount can be tested numerically.
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = text.Select(c => c is >= '\u09E6' and <= '\u09EF' ? (char)('0' + (c - '\u09E6')) : c).ToArray();
        return new string(chars).Replace(",", string.Empty);
    }

    /// <summary>Word-wraps to <paramref name="width"/> user-perceived characters, indenting continuation lines. A word longer than a line is split.</summary>
    internal static List<string> Wrap(string text, int width, string continuationIndent)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var currentLength = 0;

        void Flush()
        {
            if (currentLength > 0) result.Add(current.ToString());
            current.Clear();
            currentLength = 0;
        }

        foreach (var rawWord in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = rawWord;
            var wordLength = Length(word);
            var indent = result.Count > 0 && currentLength == 0 ? continuationIndent : string.Empty;
            var indentLength = Length(indent);

            if (currentLength > 0 && currentLength + 1 + wordLength > width)
            {
                Flush();
                indent = continuationIndent;
                indentLength = Length(indent);
            }

            // A single word wider than the line: break it between text elements.
            while (indentLength + wordLength > width && width > indentLength + 1)
            {
                var take = width - indentLength;
                var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(word);
                var cut = 0;
                var count = 0;
                while (count < take && enumerator.MoveNext()) { cut = enumerator.ElementIndex + enumerator.GetTextElement().Length; count++; }
                if (currentLength > 0) Flush();
                result.Add(indent + word[..cut]);
                word = word[cut..];
                wordLength = Length(word);
                indent = continuationIndent;
                indentLength = Length(indent);
            }

            if (currentLength == 0) { current.Append(indent); currentLength = indentLength; }
            else { current.Append(' '); currentLength++; }
            current.Append(word);
            currentLength += wordLength;
        }
        Flush();
        return result;
    }

    private static int Length(string text) => new System.Globalization.StringInfo(text).LengthInTextElements;

    private static string Separator(char c) => new(c, LineWidth);

    private static string KeyValue(string label, string value)
    {
        const int labelWidth = 10;
        var paddedLabel = label.Length >= labelWidth ? label[..labelWidth] : label.PadRight(labelWidth);
        var line = $"{paddedLabel}: {value}";
        return line.Length > LineWidth ? line[..LineWidth] : line;
    }

    private static string TwoColumn(string left, string right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        if (right.Length >= LineWidth)
        {
            right = right[..LineWidth];
        }
        var maxLeft = Math.Max(0, LineWidth - right.Length - 1);
        if (left.Length > maxLeft)
        {
            left = left.Length > 3 ? string.Concat(left.AsSpan(0, Math.Max(0, maxLeft - 1)), ".") : left[..maxLeft];
        }
        var padding = Math.Max(1, LineWidth - left.Length - right.Length);
        return left + new string(' ', padding) + right;
    }

    private static List<string> WrapNote(string text)
    {
        const string prefix = "Note: ";
        var lines = new List<string>();
        var current = prefix;
        var atLineStart = true;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = atLineStart ? current + word : current + " " + word;
            if (candidate.Length > LineWidth && !atLineStart)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
            atLineStart = false;
        }
        lines.Add(current);
        return lines.Select(l => l.Length > LineWidth ? l[..LineWidth] : l).ToList();
    }
}
