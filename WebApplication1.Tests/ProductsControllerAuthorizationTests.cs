using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using WebApplication1.Controllers;
using WebApplication1.Models.Identity;

namespace WebApplication1.Tests;

// Test 7: unauthorized users must be denied at the controller boundary, not just hidden in the
// UI. These actions can change Product.SalePrice (and therefore create price history and trigger
// Manager emails), so they must carry the same [Authorize(Roles = Roles.PurchaseManagers)] gate
// as the rest of product editing.
public class ProductsControllerAuthorizationTests
{
    public static IEnumerable<object[]> PriceChangingMethods()
    {
        yield return [typeof(ProductsController).GetMethod(nameof(ProductsController.Edit), [typeof(int)])!];
        yield return [typeof(ProductsController).GetMethod(nameof(ProductsController.Edit), [typeof(int), typeof(WebApplication1.Models.Inventory.Product)])!];
        yield return [typeof(ProductsController).GetMethod(nameof(ProductsController.PendingPriceUpdates), [])!];
    }

    [Theory]
    [MemberData(nameof(PriceChangingMethods))]
    public void PriceChangingAction_RequiresPurchaseManagersRole(MethodInfo method)
    {
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>()
            ?? method.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Roles.PurchaseManagers, authorize!.Roles);
    }
}
