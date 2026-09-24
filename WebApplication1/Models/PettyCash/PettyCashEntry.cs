using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Common;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Models.PettyCash;

public enum PettyCashType
{
    CashIn = 0,
    CashOut = 1
}

public class PettyCashEntry : BaseEntity
{
    public DateTime Date { get; set; }

    // Each warehouse keeps its own petty cash book (own running balance); users only see and
    // record entries for the warehouses they're assigned to. Names are shared across warehouses.
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int PettyCashNameId { get; set; }
    public PettyCashName? PettyCashName { get; set; }

    public PettyCashType Type { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
}
