using Microsoft.AspNetCore.SignalR;
using Sammha.Application.Common;
using Sammha.Application.Engine;

namespace Sammha.Api.Infrastructure;

/// <summary>Live auction pages join their auction's group and reload on "changed".</summary>
public class AuctionHub : Hub
{
    public Task Join(string auctionId) => Groups.AddToGroupAsync(Context.ConnectionId, auctionId);
    public Task Leave(string auctionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, auctionId);
}

public class SignalRAuctionRealtime(IHubContext<AuctionHub> hub) : IAuctionRealtime
{
    public Task ChangedAsync(Guid auctionId) => hub.Clients.Group(auctionId.ToString()).SendAsync("changed", auctionId.ToString());
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
