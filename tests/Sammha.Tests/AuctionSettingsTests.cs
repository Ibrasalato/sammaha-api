using System.Text.Json;

namespace Sammha.Tests;

/// <summary>«إعدادات المزادات»: the admin sets the opening prices (فردي and منقية), the minimum raise and the default auction length.</summary>
public class AuctionSettingsTests(TestApp app) : IClassFixture<TestApp>
{
    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");

    /// <summary>a paid request whose first name the committee accepts; returns that name's candidate id</summary>
    private async Task<string> AcceptedAsync(Api owner, Api committee, string type, string[] names)
    {
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type, names });
        var id = r.GetProperty("id").GetString()!;
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var c = (await committee.Get<JsonElement>($"/api/requests/{id}")).GetProperty("candidates").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = c.Select((x, i) => new { candidateId = x, status = i == 0 ? "accepted" : "rejected", reason = i == 0 ? null : "قُبل شعار آخر" }).ToArray() });
        return c[0];
    }

    [Fact]
    public async Task Defaults_come_from_the_server_configuration()
    {
        var pub = await app.Anonymous().Get<JsonElement>("/api/settings");
        Assert.Equal(50m, pub.GetProperty("openingPrice").GetDecimal());
        Assert.Equal(50m, pub.GetProperty("manqiyaOpeningPrice").GetDecimal());
        Assert.Equal(10m, pub.GetProperty("minIncrement").GetDecimal());
        Assert.Equal(2, pub.GetProperty("auctionDays").GetInt32());
    }

    [Fact]
    public async Task Admin_sets_the_opening_prices_and_the_raise_and_new_auctions_use_them()
    {
        var admin = await Admin();
        var committee = await app.CommitteeAsync();
        Assert.Contains("10,000,000", await admin.Fails(() => admin.Patch<JsonElement>("/api/admin/settings", new { openingPrice = -1 })));
        Assert.Contains("1,000,000", await admin.Fails(() => admin.Patch<JsonElement>("/api/admin/settings", new { minIncrement = 0 })));
        Assert.Contains("60", await admin.Fails(() => admin.Patch<JsonElement>("/api/admin/settings", new { auctionDays = 61 })));
        // the committee cannot change platform settings
        await committee.Fails(() => committee.Patch<JsonElement>("/api/admin/settings", new { minIncrement = 100 }));

        var s = await admin.Patch<JsonElement>("/api/admin/settings", new { openingPrice = 70, manqiyaOpeningPrice = 3000, minIncrement = 25, auctionDays = 4 });
        Assert.Equal(70m, s.GetProperty("openingPrice").GetDecimal());
        Assert.Equal(3000m, s.GetProperty("manqiyaOpeningPrice").GetDecimal());
        Assert.Equal(25m, s.GetProperty("minIncrement").GetDecimal());
        Assert.Equal(4, s.GetProperty("auctionDays").GetInt32());
        Assert.Equal(70m, s.GetProperty("rules").GetProperty("openingPrice").GetDecimal());
        Assert.Equal(3000m, s.GetProperty("rules").GetProperty("manqiyaOpeningPrice").GetDecimal());
        Assert.Equal(25m, s.GetProperty("rules").GetProperty("minIncrement").GetDecimal());
        var pub = await app.Anonymous().Get<JsonElement>("/api/settings");
        Assert.Equal(3000m, pub.GetProperty("manqiyaOpeningPrice").GetDecimal());
        Assert.Equal(4, pub.GetProperty("auctionDays").GetInt32());
        var hist = (await admin.Get<JsonElement>("/api/admin/settings/history")).EnumerateArray().Select(h => h.GetProperty("field").GetString()).ToList();
        Assert.Contains("OpeningPrice", hist);
        Assert.Contains("ManqiyaOpeningPrice", hist);
        Assert.Contains("MinIncrement", hist);
        Assert.Contains("AuctionDays", hist);

        var owner = await app.OwnerAsync();
        var manqiya = await AcceptedAsync(owner, committee, "manqiya", ["غيم الشرقية", "رمال الدهناء", "ظل السدرة"]);
        var single = await AcceptedAsync(owner, committee, "qaadan", ["شهاب الليل", "سيف الصحراء", "درع الوادي"]);
        var now = app.Clock.GetUtcNow();
        var auctions = await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { manqiya, single }, startsAt = now.AddMinutes(-1), endsAt = now.AddDays(4) });
        JsonElement Of(string name) => auctions.EnumerateArray().First(a => a.GetProperty("name").GetString() == name);
        var m = Of("غيم الشرقية");
        Assert.Equal(3000m, m.GetProperty("openingPrice").GetDecimal());
        Assert.Equal(3000m, m.GetProperty("currentPrice").GetDecimal());
        Assert.Equal(25m, m.GetProperty("minIncrement").GetDecimal());
        Assert.Equal(70m, Of("شهاب الليل").GetProperty("openingPrice").GetDecimal());

        // bids follow the new raise
        var bidder = await app.BidderAsync(1);
        var aid = m.GetProperty("id").GetString();
        await bidder.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        Assert.Contains("3025", await bidder.Fails(() => bidder.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 3010 })));
        var view = await bidder.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 3025 });
        Assert.Equal(3025m, view.GetProperty("auction").GetProperty("currentPrice").GetDecimal());

        await admin.Patch<JsonElement>("/api/admin/settings", new { openingPrice = 50, manqiyaOpeningPrice = 50, minIncrement = 10, auctionDays = 2 });
    }
}
