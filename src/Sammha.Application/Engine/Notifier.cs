using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Engine;

/// <summary>In-app notifications, pushed live over SignalR and forwarded to the n8n webhook (SMS / WhatsApp) once the transaction commits.</summary>
public class Notifier(IAppDbContext db, TimeProvider clock, IPostCommit postCommit, IWebhookPublisher webhooks, IAuctionRealtime realtime, IOptions<IntegrationOptions> opt)
{
    public async Task NotifyAsync(Guid userId, string kind, string title, string body, string? link = null, CancellationToken ct = default)
    {
        var n = new Notification { UserId = userId, Kind = kind, Title = title, Body = body, Link = link, CreatedAt = clock.GetUtcNow() };
        db.Notifications.Add(n);
        var dto = Mapper.Notification(n);
        postCommit.Enqueue(() => realtime.NotifyAsync(dto));
        var url = opt.Value.NotifyWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        var u = await db.Users.FindAsync([userId], ct);
        if (u != null && !Wants(u, kind)) return; // the user turned off SMS / WhatsApp for this kind (the in-app notice stays)
        var payload = new { kind, title, body, link, userId, phone = u?.Phone, email = u?.Email, name = u?.FullName, createdAt = n.CreatedAt };
        postCommit.Enqueue(() => webhooks.PublishAsync(url, payload));
    }

    /// <summary>notification categories the user can switch off in their settings</summary>
    public static bool Wants(User u, string kind) => kind switch
    {
        "outbid" => u.NotifyOutbid,
        "reminder" or "auction_opened" or "auction_update" => u.NotifyReminders,
        "won" or "lost" or "refund" => u.NotifyResults,
        "reviewed" => u.NotifyRequests,
        _ => true, // tickets, payments and account notices always go out
    };
}
