using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WebApplication1.Extensions;

namespace WebApplication1.Hubs;

/// <summary>
/// Connections join a group per assigned warehouse; Admin/Manager (unscoped) join "all-warehouses"
/// so they see every warehouse's activity. ActivityNotifier picks the right groups when it broadcasts.
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    public const string AllWarehousesGroup = "all-warehouses";

    public static string WarehouseGroup(int warehouseId) => $"warehouse-{warehouseId}";

    public override async Task OnConnectedAsync()
    {
        var warehouseIds = Context.User?.GetWarehouseIds();
        if (warehouseIds is null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AllWarehousesGroup);
        }
        else
        {
            foreach (var id in warehouseIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, WarehouseGroup(id));
            }
        }
        await base.OnConnectedAsync();
    }
}
