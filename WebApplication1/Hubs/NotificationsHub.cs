using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WebApplication1.Extensions;

namespace WebApplication1.Hubs;

/// <summary>
/// Connections join a group per warehouse-scoped user; Admin/Manager (unscoped) join "all-warehouses"
/// so they see every warehouse's activity. ActivityNotifier picks the right groups when it broadcasts.
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    public const string AllWarehousesGroup = "all-warehouses";

    public static string WarehouseGroup(int warehouseId) => $"warehouse-{warehouseId}";

    public override async Task OnConnectedAsync()
    {
        var warehouseId = Context.User?.GetWarehouseId();
        var group = warehouseId.HasValue ? WarehouseGroup(warehouseId.Value) : AllWarehousesGroup;
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }
}
