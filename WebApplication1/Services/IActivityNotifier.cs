namespace WebApplication1.Services;

public interface IActivityNotifier
{
    /// <summary>
    /// Pushes a live notification to everyone watching the given warehouse(s), plus Admin/Manager
    /// who watch every warehouse. Fire-and-forget from the caller's point of view: this runs after
    /// the triggering operation has already been committed to the database.
    /// </summary>
    /// <param name="mentionsPurchasePrices">True when the message contains purchase amounts: it then
    /// only reaches users allowed to see purchase prices (never Sales Officers or Viewers).</param>
    Task NotifyAsync(string title, string message, string icon, IEnumerable<int> warehouseIds, bool mentionsPurchasePrices = false);

    /// <summary>
    /// Pushes a structured data-change event (as opposed to a human-readable toast) to the same
    /// warehouse-scoped audience as <see cref="NotifyAsync"/>, so open pages can patch their own
    /// tables/widgets in place instead of requiring a reload. <paramref name="type"/> is a short
    /// tag ("ProductPrice", "StockChange", "StockTransferDoc") the client dispatches on to decide
    /// what to update; <paramref name="data"/> is serialized as-is into the payload's `data` field.
    /// </summary>
    Task NotifyDataChangeAsync(string type, object data, IEnumerable<int> warehouseIds);

    /// <summary>
    /// Pushes new stock figures ("StockChange") without leaking one warehouse's quantity to another:
    /// each warehouse's group receives only its own quantities, and only Admin/Manager (the
    /// all-warehouses group) also get the other warehouses and the all-warehouse total.
    /// </summary>
    Task NotifyStockChangeAsync(IEnumerable<StockChangeProduct> products);
}

/// <param name="WarehouseStocks">New quantity per affected warehouse id.</param>
public record StockChangeProduct(int ProductId, decimal CurrentStock, IReadOnlyDictionary<int, decimal> WarehouseStocks);
