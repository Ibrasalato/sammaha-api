using System.Text.Json;

namespace Sammha.Tests;

/// <summary>request → guarantee → committee → auction → bids (manual and automatic) → settlement → invoice → certificate</summary>
public class AuctionFlowTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Full_cycle()
    {
        var owner = await app.OwnerAsync();
        var committee = await app.CommitteeAsync();
        var b1 = await app.BidderAsync(1);
        var b2 = await app.BidderAsync(2);

        // exact match is blocked, similar is flagged
        var checks = await owner.Post<JsonElement>("/api/names/check", new { names = new[] { "الفاخر", "الفاخره", "نسيم الصبا" } });
        Assert.Equal("exact", checks[0].GetProperty("verdict").GetString());
        Assert.Equal("available", checks[2].GetProperty("verdict").GetString());

        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "manqiya", names = new[] { "نسيم الصبا", "برق الشمال", "ضي القمر" } });
        var id = r.GetProperty("id").GetString();
        Assert.Equal("pending_payment", r.GetProperty("status").GetString());
        var before = await owner.Balance();
        r = await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        Assert.Equal("under_review", r.GetProperty("status").GetString());
        Assert.Equal(before - 50, await owner.Balance());

        var full = await committee.Get<JsonElement>($"/api/requests/{id}");
        var cands = full.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        // policy: one name accepted per logo request
        var two = await committee.Fails(() => committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new
        {
            decisions = new object[]
            {
                new { candidateId = cands[0], status = "accepted" },
                new { candidateId = cands[1], status = "accepted" },
                new { candidateId = cands[2], status = "rejected", reason = "مشابه" },
            },
        }));
        Assert.Contains("شعار واحد", two);
        r = await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new
        {
            decisions = new object[]
            {
                new { candidateId = cands[0], status = "accepted" },
                new { candidateId = cands[1], status = "rejected", reason = "قُبل شعار آخر من الطلب" },
                new { candidateId = cands[2], status = "rejected", reason = "مشابه" },
            },
        });
        Assert.Equal("accepted", r.GetProperty("status").GetString());

        // a second request whose accepted name gets no bids
        var r2 = await owner.Post<JsonElement>("/api/me/requests", new { type = "manqiya", names = new[] { "برق الشمال", "سحاب الصيف", "نجم الوادي" } });
        var id2 = r2.GetProperty("id").GetString();
        await owner.Post<JsonElement>($"/api/me/requests/{id2}/pay");
        var c2 = (await committee.Get<JsonElement>($"/api/requests/{id2}")).GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        await committee.Post<JsonElement>($"/api/admin/requests/{id2}/review", new { decisions = new object[] { new { candidateId = c2[0], status = "accepted" }, new { candidateId = c2[1], status = "rejected", reason = "قُبل شعار آخر" }, new { candidateId = c2[2], status = "rejected", reason = "قُبل شعار آخر" } } });

        var now = app.Clock.GetUtcNow();
        var auctions = await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { cands[0], c2[0] }, startsAt = now.AddMinutes(-1), endsAt = now.AddHours(2) });
        string? IdOf(string name) => auctions.EnumerateArray().First(a => a.GetProperty("name").GetString() == name).GetProperty("id").GetString();
        var a1 = IdOf("نسيم الصبا");
        var a2 = IdOf("برق الشمال");

        // proxy bidding: b1 sets a ceiling, b2 bids, the platform answers for b1
        await b1.Put<JsonElement>($"/api/auctions/{a1}/autobid", new { max = 300 });
        var view = await b2.Post<JsonElement>($"/api/auctions/{a1}/bids", new { amount = 150 });
        Assert.Equal(160, view.GetProperty("auction").GetProperty("currentPrice").GetDecimal());
        view = await b2.Post<JsonElement>($"/api/auctions/{a1}/bids", new { amount = 400 });
        Assert.Equal(400, view.GetProperty("auction").GetProperty("currentPrice").GetDecimal());
        var msg = await b2.Fails(() => b2.Post<JsonElement>($"/api/auctions/{a1}/bids", new { amount = 500 }));
        Assert.Equal("أنت صاحب أعلى مزايدة حاليًا", msg);

        // end both; a1 sold to b2, a2 had no bids → the requester gets it at the opening price, guarantee applied
        app.Clock.Advance(TimeSpan.FromHours(3));
        await app.SweepAsync();
        var settled = await b2.Get<JsonElement>($"/api/auctions/{a1}");
        Assert.True(settled.GetProperty("auction").GetProperty("settled").GetBoolean());
        var inv = (await b2.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == a1);
        Assert.Equal(400, inv.GetProperty("total").GetDecimal());
        Assert.Equal(52.17m, inv.GetProperty("vat").GetDecimal());
        await b2.TopUp(400);
        inv = await b2.Post<JsonElement>($"/api/me/invoices/{inv.GetProperty("id").GetString()}/pay");
        Assert.Equal("paid", inv.GetProperty("status").GetString());

        var ownerInv = (await owner.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == a2);
        Assert.Equal(50, ownerInv.GetProperty("depositApplied").GetDecimal());
        Assert.Equal("paid", ownerInv.GetProperty("status").GetString());

        var names = await b2.Get<JsonElement>("/api/me/names");
        Assert.Contains(names.EnumerateArray(), n => n.GetProperty("name").GetString() == "نسيم الصبا");
        // the first request's only name went to another bidder, so its guarantee came back
        var txs = (await owner.Get<JsonElement>("/api/me/wallet")).GetProperty("txs").EnumerateArray();
        Assert.Contains(txs, t => t.GetProperty("kind").GetString() == "refund" && t.GetProperty("note").GetString()!.Contains(r.GetProperty("number").GetString()!));
    }

    [Fact]
    public async Task Owner_can_cancel_and_get_the_guarantee_back()
    {
        var owner = await app.SignInAsync("0500000005");
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names = new[] { "صقر الجبل", "نمر البر", "ليث الوادي" } });
        var id = r.GetProperty("id").GetString();
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var before = await owner.Balance();
        var res = await owner.Post<JsonElement>($"/api/me/requests/{id}/cancel", new { reason = "تغيير الأسماء" });
        Assert.Equal(50, res.GetProperty("refunded").GetDecimal());
        Assert.Equal(before + 50, await owner.Balance());
    }
}
