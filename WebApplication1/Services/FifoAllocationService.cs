using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Inventory;

namespace WebApplication1.Services;

// Implements FIFO stock consumption: batches are always drawn oldest-purchase-first
// (PurchaseDate, then Id as a deterministic tiebreaker). Never trusts Product.CurrentStock or
// ProductWarehouseStock (those are caches kept in sync alongside batches) — the authority for
// "how much is available" is always a fresh SUM(ProductBatch.RemainingQuantity) read here.
//
// Concurrency: ProductBatch.RowVersion makes EF Core's optimistic concurrency check fire on
// SaveChangesAsync if two requests decrement the same batch. This service only computes the
// allocation and mutates tracked entities; callers (SalesInvoicesController etc.) wrap their
// SaveChangesAsync in a retry loop that re-runs AllocateAsync against freshly-loaded batches on
// DbUpdateConcurrencyException, so a losing request re-reads the current RemainingQuantity
// rather than silently overselling or failing outright.
public class FifoAllocationService(ApplicationDbContext db) : IFifoAllocationService
{
    public async Task<List<BatchAllocation>> PreviewAsync(int productId, int warehouseId, decimal quantity)
    {
        var batches = await LoadAvailableBatchesAsync(productId, warehouseId);
        return Allocate(batches, quantity, mutate: false);
    }

    public async Task<List<BatchAllocation>> AllocateAsync(int productId, int warehouseId, decimal quantity)
    {
        var batches = await LoadAvailableBatchesAsync(productId, warehouseId);
        return Allocate(batches, quantity, mutate: true);
    }

    private async Task<List<ProductBatch>> LoadAvailableBatchesAsync(int productId, int warehouseId)
    {
        return await db.ProductBatches
            .Where(b => b.ProductId == productId && b.WarehouseId == warehouseId
                        && b.IsActive && b.RemainingQuantity > 0)
            .OrderBy(b => b.PurchaseDate).ThenBy(b => b.Id)
            .ToListAsync();
    }

    private static List<BatchAllocation> Allocate(List<ProductBatch> batches, decimal quantity, bool mutate)
    {
        var available = batches.Sum(b => b.RemainingQuantity);
        if (quantity > available)
        {
            throw new InsufficientStockException(available, quantity);
        }

        var allocations = new List<BatchAllocation>();
        var remaining = quantity;

        foreach (var batch in batches)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(batch.RemainingQuantity, remaining);
            allocations.Add(new BatchAllocation { Batch = batch, Quantity = take });

            if (mutate)
            {
                batch.RemainingQuantity -= take;
            }

            remaining -= take;
        }

        return allocations;
    }
}
