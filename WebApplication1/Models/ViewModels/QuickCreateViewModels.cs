using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels;

public class SupplierQuickCreateRequest
{
    [Required(ErrorMessage = "Enter a supplier name."), StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ContactPerson { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    // Not [EmailAddress] here: that attribute only treats a genuinely absent (null) value as
    // "not provided" — an empty string (what a blank optional field submits as) fails it, which
    // is exactly wrong for an optional field. Format is checked manually in the controller,
    // only when non-blank.
    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }
}

public class CustomerQuickCreateRequest
{
    [Required(ErrorMessage = "Enter a customer name."), StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ContactPerson { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    // See SupplierQuickCreateRequest.Email for why this isn't [EmailAddress].
    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }
}

public class ProductQuickCreateRequest
{
    [Required(ErrorMessage = "Enter a product name."), StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Select a category.")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a unit of measure.")]
    public int UnitOfMeasureId { get; set; }

    [Range(0, 9999999999999999.99, ErrorMessage = "Cost price cannot be negative.")]
    public decimal CostPrice { get; set; }

    [Range(0, 9999999999999999.99, ErrorMessage = "Sale price cannot be negative.")]
    public decimal SalePrice { get; set; }

    [Range(0, 9999999999999999.99, ErrorMessage = "Reorder level cannot be negative.")]
    public decimal ReorderLevel { get; set; } = 10;

    [StringLength(500)]
    public string? Description { get; set; }
}
