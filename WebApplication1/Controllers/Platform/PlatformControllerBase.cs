using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models.Identity;

namespace WebApplication1.Controllers;

/// <summary>
/// Everything under /Platform. Reachable only by platform staff (no tenant); a tenant's own Admin/Manager roles
/// grant nothing here. Individual actions tighten this to <see cref="Roles.PlatformAdmin"/> where money, plans,
/// settings or deletion are involved.
/// </summary>
[Authorize(Roles = Roles.PlatformStaff)]
public abstract class PlatformControllerBase : Controller
{
    protected const int PageSize = 20;

    protected string Actor => User.Identity?.Name ?? "unknown";
    protected bool IsPlatformAdmin => User.IsInRole(Roles.PlatformAdmin);

    protected IActionResult Back(string? fallbackAction = null, object? routeValues = null) =>
        Request.Headers.Referer.ToString() is { Length: > 0 } referer && Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == Request.Host.Host
            ? Redirect(uri.PathAndQuery)
            : RedirectToAction(fallbackAction ?? "Index", routeValues);

    protected static string Csv(IEnumerable<string?> cells) =>
        string.Join(",", cells.Select(c =>
        {
            var v = c ?? string.Empty;
            // Defuse spreadsheet formula injection from user-supplied text, then quote.
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
            return $"\"{v.Replace("\"", "\"\"")}\"";
        }));
}
