using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

// Builds the variable-length middle section of an 80mm thermal receipt (optional
// customer details/notes, then the item list) as fixed-width monospaced lines.
// The header, invoice meta, totals, and footer are native RDLC fields with real
// fonts/weights (see SalesInvoiceThermalReceipt.rdlc) — only this middle section,
// whose row count depends on the invoice, needs to stay plain text.
public static class ThermalReceiptFormatter
{
    public const int LineWidth = 42;

    public static List<SalesInvoiceThermalLine> BuildDetailLines(SalesInvoiceReportHeader header, IReadOnlyList<SalesInvoiceReportLine> items)
    {
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

        foreach (var item in items)
        {
            lines.Add(TwoColumn(item.ProductName, item.LineTotal));
            lines.Add($"  {item.Sku}  {item.Quantity} x {item.UnitPrice}".TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(header.Notes))
        {
            lines.Add(Separator('-'));
            lines.AddRange(WrapNote(header.Notes));
        }

        return lines.Select(text => new SalesInvoiceThermalLine { Text = text }).ToList();
    }

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
