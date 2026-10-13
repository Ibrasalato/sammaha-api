using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Sammha.Tests;

/// <summary>The SignalR hub: live auction updates, the auctions list, personal notifications, stale sessions.</summary>
public class RealtimeTests(TestApp app) : IClassFixture<TestApp>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private HubConnection Hub(string? token = null) => new HubConnectionBuilder()
        .WithUrl(new Uri(app.Server.BaseAddress, "hubs/auctions"), o =>
        {
            o.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling;
            if (token != null) o.AccessTokenProvider = () => Task.FromResult<string?>(token);
        })
        .Build();

    private static Task<JsonElement> Next(HubConnection c, string evt, Func<JsonElement, bool>? when = null)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        c.On<JsonElement>(evt, e => { if (when == null || when(e)) tcs.TrySetResult(e); });
        return tcs.Task.WaitAsync(Wait);
    }

    /// <summary>one accepted name, scheduled to be live now</summary>
    private async Task<(string Id, decimal Min)> LiveAuctionAsync(params string[] names)
    {
        var owner = await app.OwnerAsync();
        var committee = await app.CommitteeAsync();
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "bakkar", names });
        var id = r.GetProperty("id").GetString();
        await owner.TopUp(100);
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var full = await committee.Get<JsonElement>($"/api/requests/{id}");
        var cand = full.GetProperty("candidates")[0].GetProperty("id").GetString();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = full.GetProperty("candidates").EnumerateArray().Select(c => new { candidateId = c.GetProperty("id").GetString(), status = "accepted" }).ToArray() });
        var now = app.Clock.GetUtcNow();
        var a = (await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { cand }, startsAt = now.AddMinutes(-1), endsAt = now.AddHours(2) }))[0];
        return (a.GetProperty("id").GetString()!, a.GetProperty("currentPrice").GetDecimal() + a.GetProperty("minIncrement").GetDecimal());
    }

    [Fact]
    public async Task Auction_page_and_list_get_the_new_price()
    {
        var (aid, min) = await LiveAuctionAsync("سهم الريح", "غيث الربيع", "طيف المساء");
        await using var page = Hub();
        await using var list = Hub();
        var onPage = Next(page, "auction");
        var changed = Next(page, "changed");
        var onList = Next(list, "auction", e => e.GetProperty("id").GetString() == aid);
        await page.StartAsync();
        await list.StartAsync();
        await page.InvokeAsync("Join", aid);
        await list.InvokeAsync("JoinLobby");

        var bidder = await app.BidderAsync(1);
        await bidder.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = min });

        var dto = await onPage;
        Assert.Equal(aid, dto.GetProperty("id").GetString());
        Assert.Equal(min, dto.GetProperty("currentPrice").GetDecimal());
        Assert.Equal(1, dto.GetProperty("bidCount").GetInt32());
        Assert.Equal(aid, (await changed).GetString());
        Assert.Equal(min, (await onList).GetProperty("currentPrice").GetDecimal());
    }

    [Fact]
    public async Task Outbid_notice_reaches_only_the_outbid_bidder()
    {
        var (aid, min) = await LiveAuctionAsync("نجم الوادي", "ورد الخزامى", "سحاب الصيف");
        var b1 = await app.BidderAsync(1);
        var b2 = await app.BidderAsync(2);
        await using var c1 = Hub(b1.Token);
        await using var c2 = Hub(b2.Token);
        var mine = Next(c1, "notification", e => e.GetProperty("kind").GetString() == "outbid");
        var other = Next(c2, "notification", e => e.GetProperty("kind").GetString() == "outbid");
        await c1.StartAsync();
        await c2.StartAsync();

        await b1.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = min });
        await b2.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = min + 50 });

        var n = await mine;
        Assert.False(string.IsNullOrWhiteSpace(n.GetProperty("title").GetString()));
        await Assert.ThrowsAsync<TimeoutException>(() => other.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task A_stale_token_cannot_keep_a_live_channel()
    {
        var user = await app.SignInAsync("0500000005");
        var old = user.Token;
        var r = await user.Post<JsonElement>("/api/auth/password", new { current = "123456", next = "Realtime#2026" });
        user.Token = r.GetProperty("token").GetString();

        await using var stale = Hub(old);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stale.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        try { await stale.StartAsync(); await closed.Task.WaitAsync(Wait); }
        catch (Exception e) when (e is not TimeoutException) { /* refused during the handshake: also fine */ }
        Assert.Equal(HubConnectionState.Disconnected, stale.State);

        await using var fresh = Hub(user.Token);
        await fresh.StartAsync();
        Assert.Equal(HubConnectionState.Connected, fresh.State);
        await user.Post<JsonElement>("/api/auth/password", new { current = "Realtime#2026", next = "12345678" });
    }

    [Fact]
    public async Task Bad_auction_ids_are_rejected()
    {
        await using var c = Hub();
        await c.StartAsync();
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => c.InvokeAsync("Join", "not-a-guid"));
    }
}
