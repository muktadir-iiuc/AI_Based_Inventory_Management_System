using WebApplication1.Models.Inventory;

namespace WebApplication1.Services;

public class BatchAllocation
{
    public ProductBatch Batch { get; set; } = null!;
    public decimal Quantity { get; set; }
}

// Thrown when a product doesn't have enough stock (summed across its active batches in the
// given warehouse) to cover a requested quantity. Callers show Available/Requested to the user.
public class InsufficientStockException(decimal available, decimal requested) : Exception(
    $"Insufficient stock. Available quantity: {available:0.##}, requested quantity: {requested:0.##}.")
{
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
}

public interface IFifoAllocationService
{
    /// <summary>
    /// Determines which batches a sale/consumption of <paramref name="quantity"/> units of
    /// <paramref name="productId"/> in <paramref name="warehouseId"/> would draw from, oldest
    /// purchase first. Read-only — does not modify RemainingQuantity. Throws
    /// <see cref="InsufficientStockException"/> if the warehouse doesn't have enough stock.
    /// </summary>
    Task<List<BatchAllocation>> PreviewAsync(int productId, int warehouseId, decimal quantity);

    /// <summary>
    /// Same allocation as <see cref="PreviewAsync"/>, but decrements each consumed batch's
    /// RemainingQuantity on the tracked entities (caller still owns SaveChangesAsync). Callers
    /// mutating ProductBatch this way must be prepared to retry on DbUpdateConcurrencyException
    /// — see FifoAllocationService remarks.
    /// </summary>
    Task<List<BatchAllocation>> AllocateAsync(int productId, int warehouseId, decimal quantity);
}
