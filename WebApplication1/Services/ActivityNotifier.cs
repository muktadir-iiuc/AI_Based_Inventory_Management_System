using Microsoft.AspNetCore.SignalR;
using WebApplication1.Hubs;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Services;

public class ActivityNotifier(IHubContext<NotificationsHub> hub, ITenantContext tenant) : IActivityNotifier
{
    public Task NotifyAsync(string title, string message, string icon, IEnumerable<int> warehouseIds, bool mentionsPurchasePrices = false)
    {
        var payload = new { title, message, icon, timestamp = DateTime.UtcNow };
        var groups = ResolveGroups(warehouseIds);
        if (mentionsPurchasePrices)
        {
            groups = groups.Select(NotificationsHub.CostGroup).ToList();
        }
        return hub.Clients.Groups(groups).SendAsync("ReceiveNotification", payload);
    }

    public Task NotifyDataChangeAsync(string type, object data, IEnumerable<int> warehouseIds)
    {
        var payload = new { type, data, timestamp = DateTime.UtcNow };
        return hub.Clients.Groups(ResolveGroups(warehouseIds)).SendAsync("ReceiveDataUpdate", payload);
    }

    public async Task NotifyStockChangeAsync(IEnumerable<StockChangeProduct> products)
    {
        var list = products.ToList();
        var timestamp = DateTime.UtcNow;

        // Admin/Manager: everything, including the total across all warehouses.
        await hub.Clients.Group(NotificationsHub.AllWarehousesGroup(tenant.TenantId)).SendAsync("ReceiveDataUpdate", new
        {
            type = "StockChange",
            data = new
            {
                products = list.Select(p => new
                {
                    productId = p.ProductId,
                    currentStock = p.CurrentStock,
                    warehouseStocks = p.WarehouseStocks.ToDictionary(s => s.Key.ToString(), s => s.Value)
                })
            },
            timestamp
        });

        // Each warehouse's users: only that warehouse's own quantities, no total.
        foreach (var warehouseId in list.SelectMany(p => p.WarehouseStocks.Keys).Distinct())
        {
            await hub.Clients.Group(NotificationsHub.WarehouseGroup(tenant.TenantId, warehouseId)).SendAsync("ReceiveDataUpdate", new
            {
                type = "StockChange",
                data = new
                {
                    products = list.Where(p => p.WarehouseStocks.ContainsKey(warehouseId)).Select(p => new
                    {
                        productId = p.ProductId,
                        warehouseStocks = new Dictionary<string, decimal> { [warehouseId.ToString()] = p.WarehouseStocks[warehouseId] }
                    })
                },
                timestamp
            });
        }
    }

    private List<string> ResolveGroups(IEnumerable<int> warehouseIds) =>
        warehouseIds
            .Select(id => NotificationsHub.WarehouseGroup(tenant.TenantId, id))
            .Append(NotificationsHub.AllWarehousesGroup(tenant.TenantId))
            .Distinct()
            .ToList();
}
