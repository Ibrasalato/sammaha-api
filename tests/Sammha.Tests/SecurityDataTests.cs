using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Sammha.Tests;

/// <summary>
/// Public projections must not carry user ids: the anonymous registry maps an owner id to a full name, so an id on a bid,
/// a leader or a winner would unmask the bidder. Viewer-relative flags replace them.
/// </summary>
public class SecurityDataTests(TestApp app) : IClassFixture<TestApp>
{
    private static readonly string[] IdFields = ["userId", "leaderId", "winnerId", "requesterId", "ownerId"];

    private static async Task<(string Id, string FullName)> MeAsync(Api api)
    {
        var me = await api.Get<JsonElement>("/api/auth/me");
        return (me.GetProperty("id").GetString()!, me.GetProperty("fullName").GetString()!);
    }

    /// <summary>no id property anywhere in the document, and none of the given user ids in its text</summary>
    private static void AssertNoUserIds(JsonElement doc, IEnumerable<string> userIds)
    {
        var text = doc.GetRawText();
        foreach (var id in userIds) Assert.DoesNotContain(id, text, StringComparison.OrdinalIgnoreCase);
        Walk(doc);

        static void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    Assert.DoesNotContain(p.Name, IdFields);
                    Walk(p.Value);
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Walk(x);
        }
    }

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>one accepted name, live now</summary>
    private async Task<string> LiveAuctionAsync(Api owner, params string[] names)
    {
        var committee = await app.CommitteeAsync();
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "bakkar", names });
        var id = r.GetProperty("id").GetString();
        await owner.TopUp(100);
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var full = await committee.Get<JsonElement>($"/api/requests/{id}");
        var cand = full.GetProperty("candidates")[0].GetProperty("id").GetString();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = full.GetProperty("candidates").EnumerateArray().Select((c, i) => new { candidateId = c.GetProperty("id").GetString(), status = i == 0 ? "accepted" : "rejected", reason = i == 0 ? null : "قُبل شعار آخر" }).ToArray() });
        var now = app.Clock.GetUtcNow();
        var a = (await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { cand }, startsAt = now.AddMinutes(-1), endsAt = now.AddHours(2) }))[0];
        return a.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Public_auction_and_registry_carry_no_user_ids()
    {
        var owner = await app.OwnerAsync();
        var b1 = await app.BidderAsync(1);
        var b2 = await app.BidderAsync(2);
        var (ownerId, _) = await MeAsync(owner);
        var (b1Id, _) = await MeAsync(b1);
        var (b2Id, b2Name) = await MeAsync(b2);
        string[] ids = [ownerId, b1Id, b2Id];

        var aid = await LiveAuctionAsync(owner, "درة الصحراء", "سنا البرق", "ريم الفلاة");
        await b1.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        await b2.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        var view = await b1.Get<JsonElement>($"/api/auctions/{aid}");
        var min = view.GetProperty("auction").GetProperty("currentPrice").GetDecimal() + view.GetProperty("auction").GetProperty("minIncrement").GetDecimal();
        await b1.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = min });
        await b2.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = min + 100 });

        var anon = app.Anonymous();
        AssertNoUserIds(await anon.Get<JsonElement>($"/api/auctions/{aid}"), ids);
        AssertNoUserIds(await anon.Get<JsonElement>("/api/auctions"), ids);

        // a signed-in bidder learns only about themselves
        var mine = await b2.Get<JsonElement>($"/api/auctions/{aid}");
        AssertNoUserIds(mine, ids);
        Assert.True(Flag(mine.GetProperty("auction"), "isLeader"));
        Assert.True(Flag(mine.GetProperty("bids")[0], "mine"));
        Assert.False(Flag(mine.GetProperty("bids")[1], "mine"));
        var other = await b1.Get<JsonElement>($"/api/auctions/{aid}");
        Assert.False(Flag(other.GetProperty("auction"), "isLeader"));
        Assert.True(Flag(other.GetProperty("bids")[1], "mine"));
        Assert.True(Flag((await owner.Get<JsonElement>($"/api/auctions/{aid}")).GetProperty("auction"), "isRequester"));

        // settled: the winner is masked like the leader, and the registry entry has no owner id
        app.Clock.Advance(TimeSpan.FromHours(3));
        await app.SweepAsync();
        var ended = await anon.Get<JsonElement>($"/api/auctions/{aid}");
        AssertNoUserIds(ended, ids);
        var auction = ended.GetProperty("auction");
        Assert.True(auction.GetProperty("settled").GetBoolean());
        Assert.NotEqual(b2Name, auction.GetProperty("winnerName").GetString());
        Assert.True(Flag((await b2.Get<JsonElement>($"/api/auctions/{aid}")).GetProperty("auction"), "isWinner"));

        var names = await anon.Get<JsonElement>("/api/names");
        Assert.Contains(names.EnumerateArray(), n => n.GetProperty("name").GetString() == "درة الصحراء");
        AssertNoUserIds(names, ids);
    }

    [Fact]
    public async Task Staff_auction_list_keeps_the_ids()
    {
        var owner = await app.OwnerAsync();
        var (ownerId, _) = await MeAsync(owner);
        var aid = await LiveAuctionAsync(owner, "شذى الربيع", "لمع السيف", "وهج الضحى");
        var committee = await app.CommitteeAsync();
        var list = await committee.Get<JsonElement>("/api/admin/auctions?state=all");
        var a = list.EnumerateArray().First(x => x.GetProperty("id").GetString() == aid);
        Assert.Equal(ownerId, a.GetProperty("requesterId").GetString());
    }

    [Fact]
    public async Task Lobby_push_carries_no_ids_or_the_bidders_flags()
    {
        var owner = await app.OwnerAsync();
        var aid = await LiveAuctionAsync(owner, "نور الفجر", "طل الندى", "مزن الخريف");
        await using var lobby = new HubConnectionBuilder()
            .WithUrl(new Uri(app.Server.BaseAddress, "hubs/auctions"), o =>
            {
                o.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lobby.On<JsonElement>("auction", e => { if (e.GetProperty("id").GetString() == aid && e.GetProperty("bidCount").GetInt32() > 0) tcs.TrySetResult(e); });
        await lobby.StartAsync();
        await lobby.InvokeAsync("JoinLobby");

        var bidder = await app.BidderAsync(1);
        var (bidderId, _) = await MeAsync(bidder);
        await bidder.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        var view = await bidder.Get<JsonElement>($"/api/auctions/{aid}");
        var min = view.GetProperty("auction").GetProperty("currentPrice").GetDecimal() + view.GetProperty("auction").GetProperty("minIncrement").GetDecimal();
        await bidder.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = min });

        var pushed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        AssertNoUserIds(pushed, [bidderId]);
        Assert.False(Flag(pushed, "isLeader"));
        Assert.False(Flag(pushed, "isRequester"));
    }

    [Fact]
    public async Task Name_check_hides_names_from_other_owners_requests()
    {
        var owner = await app.SignInAsync("0500000005");
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names = new[] { "صهيل المجد", "عز الوادي", "فخر النفود" } });
        await owner.TopUp(100);
        await owner.Post<JsonElement>($"/api/me/requests/{r.GetProperty("id").GetString()}/pay");

        var check = (await app.Anonymous().Post<JsonElement>("/api/names/check", new { names = new[] { "صهيل المجد" } }))[0];
        Assert.Equal("exact", check.GetProperty("verdict").GetString());
        var hits = check.GetProperty("hits").EnumerateArray().Where(h => h.GetProperty("source").GetString() == "request").ToList();
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("", h.GetProperty("name").GetString()));
    }
}
