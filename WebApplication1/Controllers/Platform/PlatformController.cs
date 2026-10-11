using Microsoft.AspNetCore.Mvc;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

[Route("Platform")]
public class PlatformController(IPlatformMetricsService metrics) : PlatformControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await metrics.GetDashboardAsync());
}
