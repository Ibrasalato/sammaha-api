using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Money and ownership safeguards: resale settlement, removed auctions, card payments spent elsewhere, public certificate lookups, delegates.</summary>
public class SecurityMoneyTests(TestApp app) : IClassFixture<TestApp>
{
    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");

    private static async Task<bool> Owns(Api a, string name) =>
        (await a.Get<JsonElement>("/api/me/names")).EnumerateArray().Any(n => n.GetProperty("name").GetString() == name);

    private static async Task<string> NameId(Api a, string name) =>
        (await a.Get<JsonElement>("/api/me/names")).EnumerateArray().First(n => n.GetProperty("name").GetString() == name).GetProperty("id").GetString()!;

    [Fact]
    public async Task Unpaid_resale_win_keeps_the_name_with_the_seller_and_forfeits_after_the_deadline()
    {
        var seller = await app.SignInAsync("0500000004"); // owns سلطان
        var buyer = await app.BidderAsync(1);
        var reserved = await NameId(seller, "سلطان");
        var l = await seller.Post<JsonElement>("/api/me/listings", new { reservedId = reserved, mode = "auction", price = 500 });
        var lid = l.GetProperty("id").GetString();
        var aid = l.GetProperty("auctionId").GetString();

        app.Clock.Advance(TimeSpan.FromHours(73));
        await buyer.TopUp(100);
        await buyer.Post<JsonElement>($"/api/auctions/{aid}/deposit");
        var afterDeposit = await buyer.Balance();
        await buyer.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 700 });

        app.Clock.Advance(TimeSpan.FromDays(3).Add(TimeSpan.FromMinutes(5)));
        await app.SweepAsync();

        // settled, but the name moves only once the buyer pays
        var inv = (await buyer.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == aid);
        Assert.Equal("unpaid", inv.GetProperty("status").GetString());
        var due = inv.GetProperty("dueAt").GetDateTimeOffset();
        Assert.True(due > app.Clock.GetUtcNow());
        Assert.True(await Owns(seller, "سلطان"));
        Assert.False(await Owns(buyer, "سلطان"));
        // and the seller cannot sell it again (or move it) while the buyer's payment is pending
        Assert.Contains("بانتظار سداد", await seller.Fails(() => seller.Post<JsonElement>("/api/me/listings", new { reservedId = reserved, mode = "instant", price = 900 })));

        // the deadline passes: the sale is cancelled, the entry guarantee forfeited, the name stays with the seller
        app.Clock.Advance(due - app.Clock.GetUtcNow() + TimeSpan.FromMinutes(5));
        await app.SweepAsync();
        inv = (await buyer.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("auctionId").GetString() == aid);
        Assert.Equal("cancelled", inv.GetProperty("status").GetString());
        Assert.True(await Owns(seller, "سلطان"));
        Assert.False(await Owns(buyer, "سلطان"));
        Assert.Equal(afterDeposit, await buyer.Balance());
        var mine = (await seller.Get<JsonElement>("/api/me/listings")).EnumerateArray().First(x => x.GetProperty("id").GetString() == lid);
        Assert.Equal("unsold", mine.GetProperty("status").GetString());
        Assert.Contains("أُلغيت", await buyer.Fails(() => buyer.Post<JsonElement>($"/api/me/invoices/{inv.GetProperty("id").GetString()}/pay")));
    }

    [Fact]
    public async Task Cancelling_a_scheduled_resale_returns_the_entry_guarantees_already_paid()
    {
        var seller = await app.SignInAsync("0500000003"); // owns الغيمة
        var bidder = await app.BidderAsync(3); // 0500000005
        var l = await seller.Post<JsonElement>("/api/me/listings", new { reservedId = await NameId(seller, "الغيمة"), mode = "auction", price = 800 });
        var aid = l.GetProperty("auctionId").GetString();

        await bidder.TopUp(100);
        var before = await bidder.Balance();
        await bidder.Post<JsonElement>($"/api/auctions/{aid}/deposit"); // allowed before the start
        Assert.True(await bidder.Balance() < before);

        var cancelled = await seller.Post<JsonElement>($"/api/me/listings/{l.GetProperty("id").GetString()}/cancel");
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(before, await bidder.Balance());
        var txs = (await bidder.Get<JsonElement>("/api/me/wallet")).GetProperty("txs").EnumerateArray();
        Assert.Contains(txs, t => t.GetProperty("kind").GetString() == "refund" && t.GetProperty("note").GetString()!.Contains("الغيمة"));
    }

    [Fact]
    public async Task A_card_payment_spent_from_the_wallet_meanwhile_cannot_complete_its_purpose()
    {
        var seller = await app.OwnerAsync(); // owns الشرهات
        var buyer = await app.BidderAsync(3); // 0500000005
        var admin = await Admin();
        var l = await seller.Post<JsonElement>("/api/me/listings", new { reservedId = await NameId(seller, "الشرهات"), mode = "instant", price = 1000 });
        var lid = l.GetProperty("id").GetString();

        // an empty wallet, then the card payment for the purchase lands in it
        var uid = (await buyer.Get<JsonElement>("/api/auth/me")).GetProperty("id").GetString();
        var start = await buyer.Balance();
        if (start > 0) await admin.Post<JsonElement>($"/api/admin/wallets/{uid}/adjust", new { amount = -start, reason = "تصفير المحفظة للاختبار" });
        Assert.Equal(0, await buyer.Balance());
        await buyer.Post<JsonElement>("/api/payments/card", new { purpose = "listing", @ref = lid, amount = 0, callback = "https://sammha.test/cb" });
        Assert.Equal(1000, await buyer.Balance());

        // part of it is spent on a guarantee before the purchase completes
        var r = await buyer.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names = new[] { "زهر الربيع", "غيث السحاب", "ندى الصباح" } });
        await buyer.Post<JsonElement>($"/api/me/requests/{r.GetProperty("id").GetString()}/pay");
        var left = await buyer.Balance();
        Assert.True(left < 1000);

        Assert.Contains("لم يعد متاحًا", await buyer.Fails(() => buyer.Post<JsonElement>($"/api/market/{lid}/buy")));
        Assert.Equal(left, await buyer.Balance()); // never below what is really there
        Assert.True(await Owns(seller, "الشرهات"));
        await seller.Post<JsonElement>($"/api/me/listings/{lid}/cancel");
    }

    [Fact]
    public async Task Certificate_lookup_without_the_printed_code_shows_only_its_status()
    {
        var owner = await app.BidderAsync(1); // has a profile and several marks
        var c = await owner.Post<JsonElement>("/api/me/certificates", new { scope = "portfolio" });
        var number = c.GetProperty("number").GetString();
        var sig = c.GetProperty("signature").GetString();
        var anon = app.Anonymous();

        var bare = await anon.Get<JsonElement>($"/api/certificates/issued/{number}");
        Assert.Equal(number, bare.GetProperty("number").GetString());
        Assert.False(string.IsNullOrEmpty(bare.GetProperty("status").GetString()));
        Assert.Equal("", bare.GetProperty("ownerNameAr").GetString());
        Assert.Equal(0, bare.GetProperty("items").GetArrayLength());

        var wrong = await anon.Get<JsonElement>($"/api/certificates/issued/{number}?s=AAAA-BBBB-CCCC-DDDD");
        Assert.False(wrong.GetProperty("signatureMatches").GetBoolean());
        Assert.Equal(0, wrong.GetProperty("items").GetArrayLength());

        var full = await anon.Get<JsonElement>($"/api/certificates/issued/{number}?s={sig}");
        Assert.True(full.GetProperty("signatureMatches").GetBoolean());
        Assert.NotEqual("", full.GetProperty("ownerNameAr").GetString());
        Assert.Equal(c.GetProperty("items").GetArrayLength(), full.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_delegate_with_the_payments_scope_cannot_pay_for_a_purchase_or_a_transfer()
    {
        var owner = await app.OwnerAsync();
        var d = await owner.Post<JsonElement>("/api/me/delegations", new
        {
            fullName = "فهد عبدالله حمد الدوسري", nationalId = "1045678903", phoneCountry = "966", phone = "0500000004", email = "fahad.d@example.com",
            capacity = "family", scopes = new[] { "payments" }, permanent = true,
        });
        var fahad = await app.SignInAsync("0500000004");
        var res = await fahad.Post<JsonElement>("/api/auth/act-as", new { delegationId = d.GetProperty("id").GetString() });
        var acting = app.Anonymous();
        acting.Token = res.GetProperty("token").GetString();

        var listing = await acting.Fails(() => acting.Post<JsonElement>("/api/payments/card", new { purpose = "listing", @ref = Guid.NewGuid(), amount = 0, callback = "https://sammha.test/cb" }));
        Assert.Contains("المزايدة والشراء", listing);
        var transfer = await acting.Fails(() => acting.Post<JsonElement>("/api/payments/card", new { purpose = "transfer", @ref = Guid.NewGuid(), amount = 0, callback = "https://sammha.test/cb" }));
        Assert.Contains("لصاحب الحساب", transfer);
    }
}
