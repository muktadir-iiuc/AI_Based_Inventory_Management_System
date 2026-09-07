namespace WebApplication1.Services;

public interface IStockService
{
    Task ReceiveStockAsync(int productId, int warehouseId, decimal quantity, string reference, string? notes = null);
    Task IssueStockAsync(int productId, int warehouseId, decimal quantity, string reference, string? notes = null);
    Task ReverseTransactionsForReferenceAsync(string reference);
    Task<decimal> GetStockAsync(int productId, int warehouseId);
}
