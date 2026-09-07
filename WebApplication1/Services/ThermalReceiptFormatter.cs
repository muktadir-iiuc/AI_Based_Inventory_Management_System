using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

// Renders a sales invoice as fixed-width monospaced lines for an 80mm thermal
// receipt printer. 42 characters is the standard column count for 80mm paper
// at the default ("Font A") size on most ESC/POS-compatible printers.
public static class ThermalReceiptFormatter
{
    public const int LineWidth = 42;

    public static List<SalesInvoiceThermalLine> BuildReceipt(SalesInvoiceReportHeader header, IReadOnlyList<SalesInvoiceReportLine> items)
    {
        var lines = new List<string>
        {
            Center(header.CompanyName.ToUpperInvariant()),
            Center("Sales Invoice Receipt"),
            Separator('=')
        };

        lines.Add(KeyValue("Invoice #", header.InvoiceNumber));
        lines.Add(KeyValue("Customer", header.CustomerName));
        if (!string.IsNullOrWhiteSpace(header.WarehouseName))
        {
            lines.Add(KeyValue("Warehouse", header.WarehouseName));
        }
        lines.Add(KeyValue("Date", header.InvoiceDate));
        lines.Add(KeyValue("Status", header.Status));
        lines.Add(string.Empty);
        lines.Add(Separator('-'));

        foreach (var item in items)
        {
            lines.Add(TwoColumn(item.ProductName, item.LineTotal));
            lines.Add($"  {item.Quantity} x {item.UnitPrice}");
        }

        lines.Add(Separator('-'));
        lines.Add(TwoColumn("TOTAL", header.TotalAmount));
        lines.Add(TwoColumn("PAID", header.PaidAmount));
        lines.Add(TwoColumn("DUE", header.DueAmount));

        if (!string.IsNullOrWhiteSpace(header.Notes))
        {
            lines.Add(string.Empty);
            lines.Add(Wrap(header.Notes));
        }

        lines.Add(string.Empty);
        lines.Add(Center("Thank You!"));
        lines.Add(Separator('='));

        return lines.Select(text => new SalesInvoiceThermalLine { Text = text }).ToList();
    }

    private static string Separator(char c) => new(c, LineWidth);

    private static string Center(string text)
    {
        text ??= string.Empty;
        if (text.Length >= LineWidth) return text[..LineWidth];
        var totalPad = LineWidth - text.Length;
        var left = totalPad / 2;
        var right = totalPad - left;
        return new string(' ', left) + text + new string(' ', right);
    }

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

    private static string Wrap(string text) => text.Length > LineWidth ? text[..LineWidth] : text;
}
