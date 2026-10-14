using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Api.Infrastructure;

/// <summary>
/// Maintenance mode (admin switch): owners and visitors can browse and sign in, but cannot submit requests, bid, pay or sell.
/// Staff, payment webhooks, contact tickets and personal settings keep working.
/// </summary>
public class MaintenanceMiddleware(RequestDelegate next)
{
    private static readonly string[] Blocked = ["/api/me", "/api/auctions", "/api/market", "/api/payments/card"];
    private static readonly string[] Allowed = ["/api/me/preferences", "/api/me/sessions", "/api/me/profile", "/api/me/account", "/api/me/email", "/api/me/phone", "/api/me/notifications"];

    public async Task InvokeAsync(HttpContext ctx, SettingsStore settings)
    {
        var path = ctx.Request.Path.Value ?? "";
        var writes = !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method);
        if (writes && Blocked.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && !Allowed.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            && !(ctx.User.IsInRole(Roles.Admin) || ctx.User.IsInRole(Roles.Committee)))
        {
            var s = await settings.GetAsync(ctx.RequestAborted);
            if (s.Maintenance)
            {
                ctx.Response.StatusCode = 503;
                await ctx.Response.WriteAsJsonAsync(new { message = string.IsNullOrWhiteSpace(s.MaintenanceNote) ? "المنصة في صيانة قصيرة، حاول بعد قليل" : s.MaintenanceNote });
                return;
            }
        }
        await next(ctx);
    }
}
