using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

/// <summary>A tenant's own sign-in link: <c>/t/{code}</c> opens the sign-in page branded for that business and limited to its users.</summary>
[AllowAnonymous]
public class TenantPortalController(ITenantPortalService portal) : Controller
{
    [HttpGet("/t/{code}")]
    public async Task<IActionResult> Index(string code)
    {
        // Unknown, disabled and terminated tenants all get the same neutral answer.
        var tenant = await portal.ResolveAsync(null, code);
        return tenant is null ? NotFound() : RedirectToAction("Login", "Account", new { tenant = tenant.Code });
    }
}
