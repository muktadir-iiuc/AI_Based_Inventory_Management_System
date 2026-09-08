using WebApplication1.Models.Inventory;

namespace WebApplication1.Models.Identity;

public class UserWarehouse
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
}
