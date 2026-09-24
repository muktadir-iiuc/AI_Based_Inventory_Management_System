using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models.Inventory;

public enum StockAdjustmentStatus
{
    Pending = 1,   // requested by a Sales/Purchase officer, waiting for a Manager/Admin
    Approved = 2,  // stock, batches and books have been changed
    Rejected = 3,  // reviewer declined — nothing was changed
    Cancelled = 4  // withdrawn by the requester while still pending
}

public enum StockAdjustmentDirection
{
    Increase = 1,
    Decrease = 2
}

public enum StockAdjustmentReason
{
    [Display(Name = "Damaged")] Damaged = 1,
    [Display(Name = "Expired")] Expired = 2,
    [Display(Name = "Lost / Stolen")] LostOrStolen = 3,
    [Display(Name = "Stock count correction")] CountCorrection = 4,
    [Display(Name = "Found / Surplus")] FoundSurplus = 5,
    [Display(Name = "Other")] Other = 6
}

// A request to correct stock in one warehouse. Nothing touches stock, batches or the ledger
// until a Manager/Admin approves it — see StockAdjustmentsController.Approve.
public class StockAdjustment
{
    public int Id { get; set; }

    public string AdjustmentNumber { get; set; } = string.Empty;

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public StockAdjustmentReason Reason { get; set; }

    public string? Notes { get; set; }

    public StockAdjustmentStatus Status { get; set; } = StockAdjustmentStatus.Pending;

    public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Mandatory when rejecting, optional when approving.
    public string? ReviewNote { get; set; }

    // Two reviewers acting on the same request at once: the second save conflicts and re-reads
    // the (now non-pending) status instead of applying the adjustment twice.
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<StockAdjustmentItem> Items { get; set; } = [];
}

public class StockAdjustmentItem
{
    public int Id { get; set; }

    public int StockAdjustmentId { get; set; }
    public StockAdjustment? StockAdjustment { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public StockAdjustmentDirection Direction { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    // Increase only: what the added units cost, and optionally their sale price. The new batch
    // (and the journal entry) is priced from these. A decrease is always costed from the FIFO
    // batches it actually consumes, never from anything the requester types.
    [Column(TypeName = "decimal(18,2)")]
    public decimal? UnitCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? SalePrice { get; set; }

    // Filled in at approval: Quantity × cost of the batch(es) consumed (decrease) or created
    // (increase). This is the amount that hit the books for this line.
    [Column(TypeName = "decimal(18,2)")]
    public decimal AppliedCostTotal { get; set; }
}
