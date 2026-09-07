using Microsoft.AspNetCore.SignalR;
using WebApplication1.Hubs;

namespace WebApplication1.Services;

public class ActivityNotifier(IHubContext<NotificationsHub> hub) : IActivityNotifier
{
    public Task NotifyAsync(string title, string message, string icon, IEnumerable<int> warehouseIds)
    {
        var groups = warehouseIds
            .Select(NotificationsHub.WarehouseGroup)
            .Append(NotificationsHub.AllWarehousesGroup)
            .Distinct()
            .ToList();

        var payload = new { title, message, icon, timestamp = DateTime.UtcNow };
        return hub.Clients.Groups(groups).SendAsync("ReceiveNotification", payload);
    }
}
