using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Purchase;

namespace WebApplication1.Models.Inventory;

// Where a transfer is in the request/approval flow. Rows that predate approvals are Approved.
public enum StockTransferApprovalStatus
{
    Pending = 1,   // requested by a Sales/Purchase officer — no stock has moved
    Approved = 2,  // a Manager/Admin approved it (or one of them created it directly)
    Rejected = 3,  // reviewer declined — no stock moved
    Withdrawn = 4  // the requester withdrew it while pending
}

public class StockTransfer
{
    public int Id { get; set; }

    public string TransferNumber { get; set; } = string.Empty;

    public int FromWarehouseId { get; set; }
    public Warehouse? FromWarehouse { get; set; }

    public int ToWarehouseId { get; set; }
    public Warehouse? ToWarehouse { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    // Posted means the stock has actually moved. A request that is still Pending, or that was
    // Rejected/Withdrawn, is saved as Cancelled so every "is this transfer live?" check fails
    // closed; it becomes Posted only when approved. Use DisplayStatus for what to show.
    public DocumentStatus Status { get; set; } = DocumentStatus.Posted;

    public StockTransferApprovalStatus ApprovalStatus { get; set; } = StockTransferApprovalStatus.Approved;

    // The requester is CreatedBy.
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Mandatory when rejecting, optional when approving.
    public string? ReviewNote { get; set; }

    // Two reviewers acting at once: the second save conflicts and re-reads the status.
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    [NotMapped]
    public string DisplayStatus => ApprovalStatus == StockTransferApprovalStatus.Approved
        ? Status.ToString()
        : ApprovalStatus.ToString();

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StockTransferItem> Items { get; set; } = [];
}

public class StockTransferItem
{
    public int Id { get; set; }

    public int StockTransferId { get; set; }
    public StockTransfer? StockTransfer { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    // False once this line has been superseded by an in-place Edit of the transfer. Every
    // query that reads Items for display/business logic must filter to IsCurrent.
    public bool IsCurrent { get; set; } = true;
}
