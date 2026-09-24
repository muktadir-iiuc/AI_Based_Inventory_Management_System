using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;

namespace WebApplication1.Middleware;

// Records when each signed-in user was last seen, so an admin can see who is using the system right
// now (Users > Active Users). It writes at most once a minute per user — every request would be a
// database write otherwise — and never lets a tracking failure break the request itself.
public class UserActivityMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<string, DateTime> LastWrite = new();
    private static readonly TimeSpan WriteEvery = TimeSpan.FromMinutes(1);

    /// <summary>Called on sign-in so the very next request doesn't wait out the throttle.</summary>
    public static void MarkSeen(string userId, DateTime utcNow) => LastWrite[userId] = utcNow;

    /// <summary>Called on sign-out: the user is no longer "active" and their next visit writes at once.</summary>
    public static void Forget(string userId) => LastWrite.TryRemove(userId, out _);

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (ShouldTrack(context))
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var now = DateTime.UtcNow;
            if (userId is not null && (!LastWrite.TryGetValue(userId, out var last) || now - last >= WriteEvery))
            {
                LastWrite[userId] = now;
                try
                {
                    await db.Users.Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActivityAt, now));
                }
                catch
                {
                    // Best effort only: presence is informational.
                }
            }
        }

        await next(context);
    }

    // Real page/API use only: not static files, the SignalR hub (it reconnects on its own), or the
    // Active Users page's own background refresh — that would keep an admin "online" for as long as
    // the page is left open in a tab.
    private static bool ShouldTrack(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
        && !Path.HasExtension(context.Request.Path.Value)
        && !context.Request.Path.StartsWithSegments("/hubs")
        && !context.Request.Path.StartsWithSegments("/Users/OnlineData");
}
