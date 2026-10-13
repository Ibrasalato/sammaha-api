using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;

namespace Sammha.Api.Infrastructure;

public static class RealtimeGroups
{
    public static string Auction(Guid id) => $"auction:{id}";
    /// <summary>pages that list auctions (home, /auctions)</summary>
    public const string Lobby = "auctions";
}

/// <summary>
/// /hubs/auctions. Anyone can follow auctions; a signed-in connection (access token in ?access_token=)
/// also receives its own notifications.
/// Server → client events: "auction" (AuctionDto), "changed" (auction id, kept for older clients), "notification" (NotificationDto).
/// </summary>
public class AuctionHub(IAppDbContext db, ILogger<AuctionHub> log) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // a token issued before a password change, role change or suspension must not keep a live channel open
        var sub = Context.UserIdentifier;
        if (sub != null)
        {
            var stamp = Context.User?.FindFirstValue(SammhaClaims.Stamp);
            var ok = Guid.TryParse(sub, out var id) && await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == id && u.Active && u.SecurityStamp == stamp, Context.ConnectionAborted);
            if (!ok)
            {
                log.LogInformation("Hub connection refused for {User}: stale session", sub);
                Context.Abort();
                return;
            }
        }
        await base.OnConnectedAsync();
    }

    public Task Join(string auctionId) => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Auction(Parse(auctionId)));
    public Task Leave(string auctionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Auction(Parse(auctionId)));
    public Task JoinLobby() => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Lobby);
    public Task LeaveLobby() => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Lobby);

    private static Guid Parse(string id) => Guid.TryParse(id, out var g) ? g : throw new HubException("معرّف المزاد غير صحيح");
}

/// <summary>SignalR addresses users by the JWT subject (the user id).</summary>
public class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext c) =>
        c.User?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? c.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}

/// <summary>Pushes after the transaction commits (via IPostCommit). Failures are logged, never thrown back into the request.</summary>
public class SignalRAuctionRealtime(IHubContext<AuctionHub> hub, IServiceScopeFactory scopes, ILogger<SignalRAuctionRealtime> log) : IAuctionRealtime
{
    public async Task ChangedAsync(Guid auctionId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var a = await db.Auctions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == auctionId);
            if (a == null) return;
            var dto = await scope.ServiceProvider.GetRequiredService<Mapper>().AuctionAsync(a, CancellationToken.None);
            var page = hub.Clients.Group(RealtimeGroups.Auction(auctionId));
            await page.SendAsync("auction", dto);
            await page.SendAsync("changed", auctionId.ToString());
            await hub.Clients.Group(RealtimeGroups.Lobby).SendAsync("auction", dto);
        }
        catch (Exception e) { log.LogWarning(e, "Realtime push for auction {Id} failed", auctionId); }
    }

    public async Task NotifyAsync(NotificationDto n)
    {
        try { await hub.Clients.User(n.UserId.ToString()).SendAsync("notification", n); }
        catch (Exception e) { log.LogWarning(e, "Realtime notification to {User} failed", n.UserId); }
    }
}

/// <summary>Runs the sweeper (settlement, proxy bids, reminders, listings) every few seconds.</summary>
public class SweeperHost(IServiceScopeFactory scopes, ILogger<SweeperHost> log, IConfiguration cfg) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var every = TimeSpan.FromSeconds(Math.Max(5, cfg.GetValue("Sweeper:Seconds", 15)));
        using var timer = new PeriodicTimer(every);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Sweeper>().RunAsync(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Sweep failed"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
