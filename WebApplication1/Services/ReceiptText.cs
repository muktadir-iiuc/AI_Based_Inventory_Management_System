using System.Globalization;
using System.Text;
using WebApplication1.Extensions;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

public enum ReceiptLanguage { English = 0, Bangla = 1 }

/// <summary>
/// Everything language-specific about the 80mm sales receipt: the fixed labels, Bangla digits and dates, and the cookie that remembers
/// a user's choice. English output is exactly what the receipt always printed; Bangla replaces the labels with Bangla text and shows
/// amounts, quantities, dates and phone numbers in Bangla digits. Names that were typed in the system (customer, product, address, notes)
/// are printed as entered, whatever script they are in.
/// </summary>
public static class ReceiptText
{
    public const string CookieName = "receipt_lang";
    public const string BanglaFontSetting = "Receipts:BanglaFont";
    public const string DefaultBanglaFont = "Nirmala UI";

    private static readonly char[] BanglaDigits = ['০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯'];

    private static readonly string[] BanglaMonths =
        ["জানুয়ারি", "ফেব্রুয়ারি", "মার্চ", "এপ্রিল", "মে", "জুন", "জুলাই", "আগস্ট", "সেপ্টেম্বর", "অক্টোবর", "নভেম্বর", "ডিসেম্বর"];

    /// <summary>"bn" -> Bangla, anything else -> English. Case-insensitive; null/unknown keeps the default.</summary>
    public static ReceiptLanguage? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "bn" or "bangla" or "bengali" => ReceiptLanguage.Bangla,
        "en" or "english" => ReceiptLanguage.English,
        _ => null
    };

    public static string Code(ReceiptLanguage language) => language == ReceiptLanguage.Bangla ? "bn" : "en";

    /// <summary>Explicit choice (query) first, then the user's remembered choice (cookie), then English.</summary>
    public static ReceiptLanguage Resolve(string? requested, string? remembered) => Parse(requested) ?? Parse(remembered) ?? ReceiptLanguage.English;

    // ------------------------------------------------------------------ labels

    /// <summary>The fixed texts printed on the receipt, in one language.</summary>
    public record Labels(
        string Title, string InvoiceNo, string Date, string Warehouse, string Customer, string ServedBy,
        string Total, string Paid, string BalanceDue, string TotalDue,
        string ThankYou, string KeepReceipt,
        string Address, string Phone, string Item, string Amount, string Subtotal, string Discount, string Note, string Qty);

    private static readonly Labels English = new(
        Title: "SALES RECEIPT", InvoiceNo: "Invoice #: ", Date: "Date: ", Warehouse: "Warehouse: ", Customer: "Customer: ", ServedBy: "Served By: ",
        Total: "Total", Paid: "Paid", BalanceDue: "Balance Due", TotalDue: "Total Due",
        ThankYou: "Thank You For Your Business!", KeepReceipt: "Please keep this receipt for any returns or exchanges.",
        Address: "Address", Phone: "Phone", Item: "ITEM", Amount: "AMOUNT", Subtotal: "Subtotal", Discount: "Discount", Note: "Note: ", Qty: "x");

    private static readonly Labels Bangla = new(
        Title: "বিক্রয় রসিদ", InvoiceNo: "ইনভয়েস নং: ", Date: "তারিখ: ", Warehouse: "গুদাম: ", Customer: "ক্রেতা: ", ServedBy: "বিক্রয়কর্মী: ",
        Total: "মোট", Paid: "পরিশোধিত", BalanceDue: "বাকি", TotalDue: "মোট বাকি",
        ThankYou: "আপনার ব্যবসার জন্য ধন্যবাদ!", KeepReceipt: "ফেরত বা বদলের জন্য এই রসিদটি সংরক্ষণ করুন।",
        Address: "ঠিকানা", Phone: "ফোন", Item: "পণ্য", Amount: "টাকা", Subtotal: "উপমোট", Discount: "ছাড়", Note: "নোট: ", Qty: "×");

    public static Labels For(ReceiptLanguage language) => language == ReceiptLanguage.Bangla ? Bangla : English;

    // ------------------------------------------------------------------ digits and dates

    /// <summary>Replaces 0-9 with Bangla digits; every other character (commas, dots, letters, Bangla text) is untouched.</summary>
    public static string Digits(string? text, ReceiptLanguage language)
    {
        if (string.IsNullOrEmpty(text) || language != ReceiptLanguage.Bangla) return text ?? string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c is >= '0' and <= '9' ? BanglaDigits[c - '0'] : c);
        }
        return sb.ToString();
    }

    /// <summary>Same date and time the English receipt prints (document date + time of entry), in the chosen language.</summary>
    public static string Date(DateTime date, DateTime createdAtUtc, ReceiptLanguage language)
    {
        if (language != ReceiptLanguage.Bangla) return date.ToDateTimeText(createdAtUtc);

        var when = date.WithTime(createdAtUtc);
        var period = when.Hour < 12 ? "পূর্বাহ্ণ" : "অপরাহ্ণ";
        var hour12 = when.Hour % 12 == 0 ? 12 : when.Hour % 12;
        return Digits($"{when.Day} {BanglaMonths[when.Month - 1]} {when.Year}, {hour12:00}:{when.Minute:00} {period}", language);
    }

    // ------------------------------------------------------------------ the whole receipt model

    /// <summary>Converts the pre-formatted header/lines for printing in the chosen language. English is returned unchanged.</summary>
    public static void Localize(SalesInvoiceReportHeader header, IEnumerable<SalesInvoiceReportLine> lines, ReceiptLanguage language)
    {
        if (language != ReceiptLanguage.Bangla) return;

        var labels = For(language);
        header.CompanyPhone = string.IsNullOrWhiteSpace(header.CompanyPhone)
            ? string.Empty
            : labels.Phone + ": " + Digits(header.CompanyPhone.Replace("Phone: ", string.Empty), language);
        header.CustomerPhone = Digits(header.CustomerPhone, language);
        header.SubtotalAmount = Digits(header.SubtotalAmount, language);
        header.DiscountAmount = Digits(header.DiscountAmount, language);
        header.TotalAmount = Digits(header.TotalAmount, language);
        header.PaidAmount = Digits(header.PaidAmount, language);
        header.DueAmount = Digits(header.DueAmount, language);
        header.CustomerOutstandingDue = Digits(header.CustomerOutstandingDue, language);

        foreach (var line in lines)
        {
            line.Quantity = Digits(line.Quantity, language);
            line.UnitPrice = Digits(line.UnitPrice, language);
            line.LineTotal = Digits(line.LineTotal, language);
            // SKU and invoice number are identifiers (they must match the system / barcodes): left as they are.
        }
    }

    /// <summary>
    /// Swaps the report template's fixed English labels (and, for Bangla, its fonts) for the chosen language. The .rdlc stays English;
    /// this is a plain text substitution of known literals, so the English receipt is byte-for-byte what it always was.
    /// </summary>
    public static string ApplyToTemplate(string rdlc, ReceiptLanguage language, string banglaFont)
    {
        if (language != ReceiptLanguage.Bangla) return rdlc;

        var l = Bangla;
        string Value(string text) => $"<Value>{System.Security.SecurityElement.Escape(text)}</Value>";
        var font = System.Security.SecurityElement.Escape(string.IsNullOrWhiteSpace(banglaFont) ? DefaultBanglaFont : banglaFont);

        return rdlc
            .Replace("<Value>SALES RECEIPT</Value>", Value(l.Title))
            .Replace("<Value>Invoice #: </Value>", Value(l.InvoiceNo))
            .Replace("<Value>Date: </Value>", Value(l.Date))
            .Replace("<Value>Warehouse: </Value>", Value(l.Warehouse))
            .Replace("<Value>Customer: </Value>", Value(l.Customer))
            .Replace("<Value>Served By: </Value>", Value(l.ServedBy))
            .Replace("<Value>Total Due</Value>", Value(l.TotalDue))
            .Replace("<Value>Balance Due</Value>", Value(l.BalanceDue))
            .Replace("<Value>Paid</Value>", Value(l.Paid))
            .Replace("<Value>Total</Value>", Value(l.Total))
            .Replace("<Value>Thank You For Your Business!</Value>", Value(l.ThankYou))
            .Replace("<Value>Please keep this receipt for any returns or exchanges.</Value>", Value(l.KeepReceipt))
            // Arial and the monospaced detail font have no Bangla glyphs: use a Bangla-capable font throughout.
            .Replace("<FontFamily>Arial</FontFamily>", $"<FontFamily>{font}</FontFamily>")
            .Replace("<FontFamily>Consolas</FontFamily>", $"<FontFamily>{font}</FontFamily>");
    }
}
