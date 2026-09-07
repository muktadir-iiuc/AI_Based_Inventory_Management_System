using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels;

public class PurchaseInvoiceCreateViewModel
{
    [Required(ErrorMessage = "Please select a supplier.")]
    public int SupplierId { get; set; }

    [Required(ErrorMessage = "Please select a warehouse.")]
    public int WarehouseId { get; set; }

    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    public string? Notes { get; set; }

    public List<InvoiceLineInput> Items { get; set; } = [];
}

public class SalesInvoiceCreateViewModel
{
    [Display(Name = "Customer"), Required(ErrorMessage = "Please select a customer.")]
    public int CustomerId { get; set; }

    [Display(Name = "Warehouse"), Required(ErrorMessage = "Please select a warehouse.")]
    public int WarehouseId { get; set; }

    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    public string? Notes { get; set; }

    public List<InvoiceLineInput> Items { get; set; } = [];
}

public class InvoiceLineInput
{
    [Required]
    public int ProductId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }
}

public class StockTransferCreateViewModel
{
    [Required(ErrorMessage = "Please select the source warehouse.")]
    public int FromWarehouseId { get; set; }

    [Required(ErrorMessage = "Please select the destination warehouse.")]
    public int ToWarehouseId { get; set; }

    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    public string? Notes { get; set; }

    public List<TransferLineInput> Items { get; set; } = [];
}

public class TransferLineInput
{
    [Required]
    public int ProductId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; }
}
