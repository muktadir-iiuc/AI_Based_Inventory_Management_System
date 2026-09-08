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

    public List<PurchaseLineInput> Items { get; set; } = [];
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

    public List<SalesLineInput> Items { get; set; } = [];
}

public class PurchaseLineInput
{
    [Required]
    public int ProductId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    // The price the resulting batch will sell at (see ProductBatch) — set once at purchase
    // time and frozen on that batch from then on, independent of any other batch's price.
    [Range(0, double.MaxValue)]
    public decimal SalePrice { get; set; }
}

// A sale only asks for Product + Quantity: FIFO determines which batch(es) it draws from and
// therefore its price, so the salesperson never enters a unit price directly.
public class SalesLineInput
{
    [Required]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    // Optional override of the FIFO batch's sale price for this line — null/0 falls back to
    // each allocated batch's own SalePrice, same as before this was editable.
    [Range(0, double.MaxValue)]
    public decimal? UnitPrice { get; set; }
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
