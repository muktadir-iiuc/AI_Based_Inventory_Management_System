using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WebApplication1.Extensions;

namespace WebApplication1.Hubs;

/// <summary>
/// Connections join a group per assigned warehouse; Admin/Manager (unscoped) join "all-warehouses"
/// so they see every warehouse's activity. Users allowed to see purchase prices also join a parallel
/// "cost:" copy of each group, which carries notifications that mention purchase amounts.
/// ActivityNotifier picks the right groups when it broadcasts.
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    // Every group name carries the tenant id so one business's events never reach another's clients.
    public static string AllWarehousesGroup(int tenantId) => $"t{tenantId}:all-warehouses";

    public static string WarehouseGroup(int tenantId, int warehouseId) => $"t{tenantId}:warehouse-{warehouseId}";

    public static string CostGroup(string group) => $"cost:{group}";

    public override async Task OnConnectedAsync()
    {
        if (!int.TryParse(Context.User?.FindFirst(Services.Tenancy.TenantContext.ClaimType)?.Value, out var tenantId) || tenantId <= 0)
        {
            Context.Abort();
            return;
        }

        var warehouseIds = Context.User?.GetWarehouseIds();
        var groups = warehouseIds is null ? [AllWarehousesGroup(tenantId)] : warehouseIds.Select(id => WarehouseGroup(tenantId, id)).ToList();
        var canSeeCost = Context.User?.CanSeePurchasePrice() == true;
        foreach (var group in groups)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
            if (canSeeCost)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, CostGroup(group));
            }
        }
        await base.OnConnectedAsync();
    }
}
