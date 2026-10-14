using System.Text.Json;

namespace Sammha.Tests;

/// <summary>«استثمار الاسم» and «بيع اسم»</summary>
public class ServicesTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Investment_fee_is_kept_proposer_cannot_bid_and_gets_30_percent()
    {
        var owner = await app.SignInAsync("0500000004");
        var committee = await app.CommitteeAsync();
        var b1 = await app.BidderAsync(1);

        var msg = await owner.Fails(() => owner.Post<JsonElement>("/api/me/requests/investment", new { type = "bakkar", names = new[] { "أ", "ب", "ج", "د" } }));
        Assert.NotEmpty(msg);
        var r = await owner.Post<JsonElement>("/api/me/requests/investment", new { type = "bakkar", names = new[] { "وهج الضحى", "طيف المساء" } });
        var id = r.GetProperty("id").GetString();
        Assert.Equal(100, r.GetProperty("depositAmount").GetDecimal());
        var before = await owner.Balance();
        Assert.Contains("قنوات الدفع", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/requests/{id}/pay")));
        await owner.PayByCard("deposit", id, $"/api/me/requests/{id}/pay");
        Assert.Equal(before, await owner.Balance()); // the card paid the fee, the wallet is untouched

        var cands = (await committee.Get<JsonElement>($"/api/requests/{id}")).GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = new object[] { new { candidateId = cands[0], status = "accepted" }, new { candidateId = cands[1], status = "rejected", reason = "لا يحقق التفرد" } } });
        Assert.Equal(before - 100, await owner.Balance()); // fee not refunded for the rejected name

        var now = app.Clock.GetUtcNow();
        var a = (await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { cands[0] }, startsAt = now.AddMinutes(-1), endsAt = now.AddHours(1) }))[0];
        var aid = a.GetProperty("id").GetString();
        Assert.True(a.GetProperty("investment").GetBoolean());
        msg = await owner.Fails(() => owner.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 100 }));
        Assert.Contains("استثمار الاسم", msg);
        await b1.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 1150 });

        app.Clock.Advance(TimeSpan.FromHours(2));
        await app.SweepAsync();
        var inv = (await b1.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == aid);
        Assert.Equal(300, inv.GetProperty("ownerShare").GetDecimal()); // (1150 − 150 VAT) × 30%
        var ownerBefore = await owner.Balance();
        await b1.TopUp(1150);
        await b1.PayByCard("invoice", inv.GetProperty("id").GetString(), $"/api/me/invoices/{inv.GetProperty("id").GetString()}/pay");
        Assert.Equal(ownerBefore + 300, await owner.Balance());
    }

    [Fact]
    public async Task Instant_sale_pays_the_seller_after_15_percent()
    {
        var seller = await app.SignInAsync("0500000003"); // owns الغيمة in the demo data
        var buyer = await app.SignInAsync("0500000005");
        var names = await seller.Get<JsonElement>("/api/me/names");
        var reserved = names.EnumerateArray().First(n => n.GetProperty("name").GetString() == "الغيمة").GetProperty("id").GetString();
        var l = await seller.Post<JsonElement>("/api/me/listings", new { reservedId = reserved, mode = "instant", price = 2000 });
        Assert.Equal("live", l.GetProperty("status").GetString());
        var dup = await seller.Fails(() => seller.Post<JsonElement>("/api/me/listings", new { reservedId = reserved, mode = "instant", price = 900 }));
        Assert.Equal("الاسم معروض للبيع حاليًا", dup);

        var market = await buyer.Get<JsonElement>("/api/market");
        Assert.Contains(market.EnumerateArray(), x => x.GetProperty("name").GetString() == "الغيمة");
        var sellerBefore = await seller.Balance();
        await buyer.TopUp(2000);
        var sold = await buyer.PayByCard("listing", l.GetProperty("id").GetString(), $"/api/market/{l.GetProperty("id").GetString()}/buy");
        Assert.Equal("sold", sold.GetProperty("status").GetString());
        Assert.Equal(sellerBefore + 1700, await seller.Balance());
        Assert.Contains((await buyer.Get<JsonElement>("/api/me/names")).EnumerateArray(), n => n.GetProperty("name").GetString() == "الغيمة");
    }

    [Fact]
    public async Task Resale_auction_needs_a_guarantee_refunds_losers_and_keeps_10_percent()
    {
        var seller = await app.SignInAsync("0500000004"); // owns سلطان
        var b1 = await app.BidderAsync(1);
        var b3 = await app.SignInAsync("0500000001");
        var reserved = (await seller.Get<JsonElement>("/api/me/names")).EnumerateArray().First(n => n.GetProperty("name").GetString() == "سلطان").GetProperty("id").GetString();
        var l = await seller.Post<JsonElement>("/api/me/listings", new { reservedId = reserved, mode = "auction", price = 500 });
        Assert.Equal("scheduled", l.GetProperty("status").GetString());
        var aid = l.GetProperty("auctionId").GetString();

        app.Clock.Advance(TimeSpan.FromHours(73)); // announced 72 h ahead
        var msg = await b1.Fails(() => b1.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 600 }));
        Assert.Contains("ضمان دخول المزاد", msg);
        msg = await seller.Fails(() => seller.Post<JsonElement>($"/api/auctions/{aid}/deposit"));
        Assert.Equal("لا يمكنك المزايدة على اسم تبيعه", msg);

        var b1Start = await b1.Balance();
        await b1.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        await b1.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 600 });
        await b3.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        await b3.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 1000 });

        app.Clock.Advance(TimeSpan.FromDays(3));
        await app.SweepAsync();
        Assert.Equal(b1Start, await b1.Balance()); // guarantee back
        var inv = (await b3.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == aid);
        Assert.Equal(50, inv.GetProperty("depositApplied").GetDecimal());
        Assert.Equal(950, inv.GetProperty("amountDue").GetDecimal());
        var sellerBefore = await seller.Balance();
        await b3.TopUp(950);
        await b3.PayByCard("invoice", inv.GetProperty("id").GetString(), $"/api/me/invoices/{inv.GetProperty("id").GetString()}/pay");
        Assert.Equal(sellerBefore + 900, await seller.Balance());
        var mine = (await seller.Get<JsonElement>("/api/me/listings")).EnumerateArray().First(x => x.GetProperty("id").GetString() == l.GetProperty("id").GetString());
        Assert.True(mine.GetProperty("payoutPaid").GetBoolean());
    }
}
