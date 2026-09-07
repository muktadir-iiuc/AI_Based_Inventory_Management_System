using System.ComponentModel.DataAnnotations;
using WebApplication1.Models.Common;

namespace WebApplication1.Models.Inventory;

public class UnitOfMeasure : BaseEntity
{
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(10)]
    public string Symbol { get; set; } = string.Empty;

    public ICollection<Product> Products { get; set; } = [];
}
