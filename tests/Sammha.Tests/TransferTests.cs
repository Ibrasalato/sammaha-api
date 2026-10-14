using System.Text.Json;

namespace Sammha.Tests;

public class TransferTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<JsonElement> Name(Api a, string name) =>
        (await a.Get<JsonElement>("/api/me/names")).EnumerateArray().FirstOrDefault(n => n.GetProperty("name").GetString() == name);

    [Fact]
    public async Task Owner_recipient_and_committee_move_a_name()
    {
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        await admin.Patch<JsonElement>("/api/admin/settings", new { transferFee = 100 });

        var owner = await app.OwnerAsync();
        var to = await app.BidderAsync(1); // 0500000003
        var id = (await Name(owner, "الشرهات")).GetProperty("id").GetGuid();

        var before = await owner.Balance();
        var who = await owner.Post<JsonElement>("/api/me/transfers/recipient", new { phone = "0500000003" });
        Assert.EndsWith("…", who.GetProperty("maskedName").GetString());
        Assert.Contains("حسابك", await owner.Fails(() => owner.Post<JsonElement>("/api/me/transfers/recipient", new { phone = "0500000001" })));

        // the fee is paid by card only: the wallet cannot pay it
        var t = await owner.Post<JsonElement>("/api/me/transfers", new { reservedId = id, phone = "0500000003", reason = "gift" });
        Assert.Equal("pending_payment", t.GetProperty("status").GetString());
        await owner.TopUp(500);
        Assert.Contains("قنوات الدفع", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/transfers/{t.GetProperty("id").GetString()}/pay")));
        Assert.Equal(0, (await to.Get<JsonElement>("/api/me/transfers")).GetProperty("incoming").GetArrayLength());
        t = await owner.PayByCard("transfer", t.GetProperty("id").GetString(), $"/api/me/transfers/{t.GetProperty("id").GetString()}/pay");
        Assert.Equal("awaiting_recipient", t.GetProperty("status").GetString());
        Assert.Equal(before + 500, await owner.Balance());
        var tid = t.GetProperty("id").GetGuid();
        Assert.Contains("قائم", await owner.Fails(() => owner.Post<JsonElement>("/api/me/transfers", new { reservedId = id, phone = "0500000004", reason = "sale" })));
        Assert.Contains("بانتظار", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/transfers/{tid}/review", new { approve = true })));

        var incoming = (await to.Get<JsonElement>("/api/me/transfers")).GetProperty("incoming");
        Assert.Equal(tid, incoming[0].GetProperty("id").GetGuid());
        Assert.Equal("under_review", (await to.Post<JsonElement>($"/api/me/transfers/{tid}/respond", new { accept = true })).GetProperty("status").GetString());

        // the committee role has no names.transfer
        var committee = await app.CommitteeAsync();
        await committee.Fails(() => committee.Get<JsonElement>("/api/admin/transfers"));
        Assert.Contains("سبب", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/transfers/{tid}/review", new { approve = false })));
        var list = await admin.Get<JsonElement>("/api/admin/transfers?status=under_review");
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == tid && x.GetProperty("toPhone").GetString() == "0500000003");
        Assert.Equal("completed", (await admin.Post<JsonElement>($"/api/admin/transfers/{tid}/review", new { approve = true })).GetProperty("status").GetString());

        Assert.Equal(JsonValueKind.Undefined, (await Name(owner, "الشرهات")).ValueKind);
        var got = await Name(to, "الشرهات");
        Assert.Equal("transfer", got.GetProperty("source").GetString());

        // declined: the fee comes back
        var b = await to.Balance();
        var t2 = await to.Post<JsonElement>("/api/me/transfers", new { reservedId = id, phone = "0500000004", reason = "sale" });
        t2 = await to.PayByCard("transfer", t2.GetProperty("id").GetString(), $"/api/me/transfers/{t2.GetProperty("id").GetString()}/pay");
        var other = await app.BidderAsync(2);
        Assert.Equal("declined", (await other.Post<JsonElement>($"/api/me/transfers/{t2.GetProperty("id").GetGuid()}/respond", new { accept = false })).GetProperty("status").GetString());
        Assert.Equal(b + 100, await to.Balance()); // the refunded fee lands in the wallet (withdrawable to the bank)

        // cancelled by the sender: refunded too
        var t3 = await to.Post<JsonElement>("/api/me/transfers", new { reservedId = id, phone = "0500000004", reason = "other", note = "تسوية عائلية" });
        Assert.Equal("cancelled", (await to.Post<JsonElement>($"/api/me/transfers/{t3.GetProperty("id").GetGuid()}/cancel", new { })).GetProperty("status").GetString());
        Assert.Equal(b + 100, await to.Balance());
    }

    [Fact]
    public async Task Services_can_be_closed_or_coming_soon()
    {
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var owner = await app.BidderAsync(3); // 0500000005
        var name = (await owner.Get<JsonElement>("/api/me/names")).EnumerateArray().First();
        var body = new { reservedId = name.GetProperty("id").GetGuid(), phone = "0500000004", reason = "gift" };
        Assert.Contains("خدمة غير معروفة", await admin.Fails(() => admin.Patch<JsonElement>("/api/admin/settings", new { comingSoon = new[] { "teleport" } })));
        await admin.Patch<JsonElement>("/api/admin/settings", new { comingSoon = new[] { "transfer", "investment" } });
        var pub = await app.Anonymous().Get<JsonElement>("/api/settings");
        Assert.Equal("investment,transfer", string.Join(",", pub.GetProperty("comingSoon").EnumerateArray().Select(x => x.GetString())));
        Assert.Contains("قريبًا", await owner.Fails(() => owner.Post<JsonElement>("/api/me/transfers", body)));
        await admin.Patch<JsonElement>("/api/admin/settings", new { comingSoon = Array.Empty<string>(), transferOpen = false, transferClosedNote = "صيانة" });
        Assert.Contains("صيانة", await owner.Fails(() => owner.Post<JsonElement>("/api/me/transfers", body)));
        await admin.Patch<JsonElement>("/api/admin/settings", new { transferOpen = true });
    }
}
