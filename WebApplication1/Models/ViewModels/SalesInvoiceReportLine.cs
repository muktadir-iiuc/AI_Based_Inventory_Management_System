namespace WebApplication1.Models.ViewModels;

// The RDLC renderer (AspNetCore.Reporting) needs a real VB.NET compiler for any expression
// beyond a bare "=Fields!X.Value" (Format(), First(), string concatenation with "&" all trigger
// compilation), which isn't available on this .NET runtime. So every value shown on the report
// is pre-formatted to its final display string here, keeping every RDLC expression a plain
// field reference.
public class SalesInvoiceReportHeader
{
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;

    // The RDLC prints this as a "Database" image, which needs the bytes plus their content type.
    // A blank logo is sent as a 1x1 transparent PNG so the report never has to test for one
    // (any such test would be a VB expression, which this renderer cannot compile).
    public byte[] CompanyLogo { get; set; } = TransparentPixel;
    public string CompanyLogoMimeType { get; set; } = "image/png";

    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string ServedBy { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string SubtotalAmount { get; set; } = string.Empty;
    public string DiscountAmount { get; set; } = string.Empty;
    public string TotalAmount { get; set; } = string.Empty;
    public string PaidAmount { get; set; } = string.Empty;
    public string DueAmount { get; set; } = string.Empty;

    // Customer's total outstanding balance across all their sales invoices (this one included),
    // shown on the thermal receipt below the per-invoice Balance Due.
    public string CustomerOutstandingDue { get; set; } = string.Empty;

    public static readonly byte[] TransparentPixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
}

public class SalesInvoiceReportLine
{
    public string SL { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    // "Brand - Size" — printed on its own line by the thermal receipt, whose fixed-width item
    // column would otherwise truncate it off the end of a long name. The A4 invoice puts brand and
    // size straight into ProductName instead.
    public string Details { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public string UnitPrice { get; set; } = string.Empty;
    public string LineTotal { get; set; } = string.Empty;
}

// One pre-formatted, fixed-width monospaced line of an 80mm thermal receipt.
// Built entirely in code (see ThermalReceiptFormatter) so the RDLC only ever
// needs a bare "=Fields!Text.Value" binding.
public class SalesInvoiceThermalLine
{
    public string Text { get; set; } = string.Empty;
}

public record ThermalReceiptPreviewViewModel(int SalesInvoiceId, string InvoiceNumber);
