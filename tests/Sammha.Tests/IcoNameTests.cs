using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>The guarantee and payment deadline settings, and «اسم مستثنى»: an unpaid win passes to the International Camel Organization, which can auction it again.</summary>
public class IcoNameTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private async Task<(string Request, string Auction)> LiveAuctionAsync(Api owner, string[] names)
    {
        var committee = await app.CommitteeAsync();
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names });
        var id = r.GetProperty("id").GetString()!;
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var c = (await committee.Get<JsonElement>($"/api/requests/{id}")).GetProperty("candidates").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = c.Select((x, i) => new { candidateId = x, status = i == 0 ? "accepted" : "rejected", reason = i == 0 ? null : "قُبل شعار آخر" }).ToArray() });
        var now = app.Clock.GetUtcNow();
        var a = (await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { c[0] }, startsAt = now.AddMinutes(-1), endsAt = now.AddHours(1) }))[0];
        return (id, a.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Settings_validate_and_log_the_guarantee_and_the_deadline()
    {
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        Assert.Contains("1,000,000", await admin.Fails(() => admin.Patch<JsonElement>("/api/admin/settings", new { depositAmount = -5 })));
        Assert.Contains("720", await admin.Fails(() => admin.Patch<JsonElement>("/api/admin/settings", new { paymentDeadlineHours = 0 })));
        var s = await admin.Patch<JsonElement>("/api/admin/settings", new { depositAmount = 75, paymentDeadlineHours = 48 });
        Assert.Equal(75m, s.GetProperty("rules").GetProperty("depositAmount").GetDecimal());
        Assert.Equal(48, s.GetProperty("paymentDeadlineHours").GetInt32());
        var pub = await app.Anonymous().Get<JsonElement>("/api/settings");
        Assert.Equal(75m, pub.GetProperty("depositAmount").GetDecimal());
        Assert.Equal(48, pub.GetProperty("paymentDeadlineHours").GetInt32());
        var hist = (await admin.Get<JsonElement>("/api/admin/settings/history")).EnumerateArray().ToList();
        Assert.Contains(hist, h => h.GetProperty("field").GetString() == "DepositAmount");
        Assert.Contains(hist, h => h.GetProperty("field").GetString() == "PaymentDeadlineHours");
        var owner = await app.SignInAsync("0500000005");
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "bakkar", names = new[] { "نور الضحى", "مها السراب", "زهرة البر" } });
        Assert.Equal(75m, r.GetProperty("depositAmount").GetDecimal());
        await admin.Patch<JsonElement>("/api/admin/settings", new { depositAmount = 50, paymentDeadlineHours = 72 });
    }

    [Fact]
    public async Task An_unpaid_win_passes_to_the_organization_and_can_be_auctioned_again()
    {
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var owner = await app.OwnerAsync();
        var (req, aid) = await LiveAuctionAsync(owner, ["صقر الفلاة", "نجم البادية", "سهم الريح"]);
        var winner = await app.BidderAsync(1);
        var loser = await app.BidderAsync(2);
        await winner.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        await loser.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        var winnerStart = await winner.Balance();
        var loserStart = await loser.Balance();
        await loser.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 200 });
        await winner.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 500 });

        app.Clock.Advance(TimeSpan.FromHours(2));
        await app.SweepAsync();
        Assert.Equal(loserStart + 50, await loser.Balance()); // the loser's entry guarantee is back
        Assert.Equal("refunded", (await owner.Get<JsonElement>($"/api/requests/{req}")).GetProperty("depositState").GetString()); // the requester lost
        var inv = (await winner.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == aid);
        Assert.Equal(500m, inv.GetProperty("amountDue").GetDecimal());

        // the deadline passes unpaid → cancelled, guarantee forfeited, the name passes to the organization
        app.Clock.Advance(TimeSpan.FromHours(73));
        await app.SweepAsync();
        inv = (await winner.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == aid);
        Assert.Equal("cancelled", inv.GetProperty("status").GetString());
        Assert.Equal(winnerStart, await winner.Balance()); // the 50 stays forfeited
        Assert.Contains("انتهاء مهلة", await winner.Fails(() => winner.Post<JsonElement>($"/api/me/invoices/{inv.GetProperty("id").GetString()}/pay")));
        Assert.DoesNotContain((await winner.Get<JsonElement>("/api/me/names")).EnumerateArray(), n => n.GetProperty("name").GetString() == "صقر الفلاة");
        Assert.Contains((await winner.Get<JsonElement>("/api/me/notifications")).EnumerateArray(), n => n.GetProperty("title").GetString()!.Contains("أُلغي فوزك"));
        var reg = (await app.Anonymous().Get<JsonElement>("/api/names?q=صقر الفلاة")).EnumerateArray().Single();
        Assert.Equal("المنظمة الدولية للإبل", reg.GetProperty("ownerName").GetString());
        Assert.Equal("exception", reg.GetProperty("source").GetString());

        // the organization's names: staff with names.ico only
        var committee = await app.CommitteeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Get<JsonElement>("/api/admin/ico-names")));
        var ico = (await admin.Get<JsonElement>("/api/admin/ico-names")).EnumerateArray().Single(n => n.GetProperty("name").GetString() == "صقر الفلاة");
        Assert.Equal(500m, ico.GetProperty("forfeitedPrice").GetDecimal());
        Assert.Equal(50m, ico.GetProperty("forfeitedDeposit").GetDecimal());
        var icoId = ico.GetProperty("id").GetString();

        // auctioned again: everyone pays the entry guarantee; the new winner pays and gets the name and their guarantee back
        var now = app.Clock.GetUtcNow();
        var a2 = await admin.Post<JsonElement>($"/api/admin/ico-names/{icoId}/auction", new { startsAt = now.AddMinutes(-1), endsAt = now.AddHours(1), openingPrice = 300 });
        var a2id = a2.GetProperty("id").GetString();
        Assert.Contains("قائم", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/ico-names/{icoId}/auction", new { startsAt = now, endsAt = now.AddHours(1) })));
        Assert.Contains("ضمان دخول المزاد", await owner.Fails(() => owner.Post<JsonElement>($"/api/auctions/{a2id}/bids", new { amount = 310 })));
        await owner.Post<JsonElement>($"/api/auctions/{a2id}/deposit");
        var ownerStart = await owner.Balance();
        await owner.Post<JsonElement>($"/api/auctions/{a2id}/bids", new { amount = 310 });
        app.Clock.Advance(TimeSpan.FromHours(2));
        await app.SweepAsync();
        var inv2 = (await owner.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == a2id);
        await owner.PayByCard("invoice", inv2.GetProperty("id").GetString(), $"/api/me/invoices/{inv2.GetProperty("id").GetString()}/pay");
        Assert.Equal(ownerStart - 310 + 50, await owner.Balance());
        Assert.Contains((await owner.Get<JsonElement>("/api/me/names")).EnumerateArray(), n => n.GetProperty("name").GetString() == "صقر الفلاة");
        Assert.DoesNotContain((await admin.Get<JsonElement>("/api/admin/ico-names")).EnumerateArray(), n => n.GetProperty("name").GetString() == "صقر الفلاة");
    }
}
