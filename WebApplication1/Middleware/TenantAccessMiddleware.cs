using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Services.Saas;

namespace WebApplication1.Middleware;

// Keeps the two kinds of signed-in user in their own lanes:
//  - Platform staff (no tenant) only reach the Platform area, account pages and the platform-only
//    database backup. The inventory screens would show nothing anyway (tenant 0 matches no rows),
//    so they are redirected to the Platform dashboard instead of rendering empty/erroring pages.
//  - Tenant users must carry a TenantId claim. Cookies issued before multi-tenancy do not, and are
//    signed out so the user logs in again and receives one.
public class TenantAccessMiddleware(RequestDelegate next)
{
    public const string AccessItemKey = "SaasAccess";

    // A suspended/expired tenant can still pay: billing, account pages (sign out, password) and the logo.
    private static readonly string[] BillingPaths = ["/Billing", "/Account", "/Language", "/DataAgreement", "/CompanySettings/Logo", "/Home/Error", "/Payments/Callback"];

    private static readonly string[] PlatformPaths = ["/Platform", "/DatabaseBackup", "/Account", "/Language", "/DataAgreement", "/CompanySettings/Logo", "/Home/Error"];

    public async Task InvokeAsync(HttpContext context, ISubscriptionService subscriptions)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true && !IsStaticOrHub(context.Request.Path))
        {
            // A platform admin issued a temporary password: only the change-password page (and sign-out) work until it is replaced.
            if (user.HasClaim("MustChangePassword", "1")
                && !context.Request.Path.StartsWithSegments("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase)
                && !context.Request.Path.StartsWithSegments("/Account/Logout", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/Account/ChangePassword");
                return;
            }

            if (user.IsPlatformStaff())
            {
                if (!PlatformPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.Redirect("/Platform");
                    return;
                }
            }
            else if (user.GetTenantId() == 0)
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Redirect("/Account/Login");
                return;
            }
            else
            {
                // Subscription / tenant state, evaluated against the clock on every request (briefly cached),
                // so a suspended, expired or disabled tenant is restricted even if no background job has run yet.
                var access = await subscriptions.GetAccessAsync(user.GetTenantId());
                context.Items[AccessItemKey] = access;

                if (access.Level == Models.Saas.AccessLevel.Blocked)
                {
                    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                    context.Response.Redirect("/Account/Login");
                    return;
                }

                if (access.Level == Models.Saas.AccessLevel.BillingOnly
                    && !BillingPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.Redirect("/Billing/Restricted");
                    return;
                }
            }
        }

        await next(context);
    }

    private static bool IsStaticOrHub(PathString path) =>
        Path.HasExtension(path.Value) || path.StartsWithSegments("/hubs");
}
