using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels;

public class SalesReturnCreateViewModel
{
    public int SalesInvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    public string? Notes { get; set; }

    public List<SalesReturnLineInput> Items { get; set; } = [];
}

public class SalesReturnLineInput
{
    public int SalesInvoiceItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? BatchNumber { get; set; }
    public decimal MaxReturnable { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }

    // Credit per returned unit after the invoice discount's share (display only).
    public decimal CreditPerUnit { get; set; }

    [Range(0, double.MaxValue)]
    public decimal ReturnQuantity { get; set; }
}

public class PurchaseReturnCreateViewModel
{
    public int PurchaseInvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    public string? Notes { get; set; }

    public List<PurchaseReturnLineInput> Items { get; set; } = [];
}

public class PurchaseReturnLineInput
{
    public int PurchaseInvoiceItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? BatchNumber { get; set; }

    // The batch's current RemainingQuantity — already-sold stock can't be returned to the
    // supplier, so this (not the original purchased quantity) is the real ceiling.
    public decimal MaxReturnable { get; set; }
    public decimal UnitCost { get; set; }

    [Range(0, double.MaxValue)]
    public decimal ReturnQuantity { get; set; }
}
