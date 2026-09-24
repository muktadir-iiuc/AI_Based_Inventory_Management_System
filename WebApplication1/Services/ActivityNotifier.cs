using Microsoft.AspNetCore.SignalR;
using WebApplication1.Hubs;

namespace WebApplication1.Services;

public class ActivityNotifier(IHubContext<NotificationsHub> hub) : IActivityNotifier
{
    public Task NotifyAsync(string title, string message, string icon, IEnumerable<int> warehouseIds)
    {
        var payload = new { title, message, icon, timestamp = DateTime.UtcNow };
        return hub.Clients.Groups(ResolveGroups(warehouseIds)).SendAsync("ReceiveNotification", payload);
    }

    public Task NotifyDataChangeAsync(string type, object data, IEnumerable<int> warehouseIds)
    {
        var payload = new { type, data, timestamp = DateTime.UtcNow };
        return hub.Clients.Groups(ResolveGroups(warehouseIds)).SendAsync("ReceiveDataUpdate", payload);
    }

    private static List<string> ResolveGroups(IEnumerable<int> warehouseIds) =>
        warehouseIds
            .Select(NotificationsHub.WarehouseGroup)
            .Append(NotificationsHub.AllWarehousesGroup)
            .Distinct()
            .ToList();
}
