using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>
/// «طلب استثناء المشاركة بدون اسم»: the owner asks for one chip at one festival, staff set the fine for not naming it,
/// the owner pays it by card, and a signed exemption document anyone can verify is issued.
/// </summary>
public class ExemptionTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");

    private async Task<Api> StaffAsync(Api admin, string phone, params string[] permissions)
    {
        var role = await admin.Post<JsonElement>("/api/admin/roles", new { name = $"استثناءات {phone[^3..]}", permissions });
        await admin.Post<JsonElement>("/api/admin/users", new { fullName = $"دارس {phone[^3..]}", phone, email = $"x{phone[^3..]}@example.com", password = "Staff#2026x", role = "committee", staffRoleId = role.GetProperty("id").GetString() });
        return await app.SignInAsync(phone, "Staff#2026x", "staff");
    }

    private static async Task<string> FestivalAsync(Api admin, string name, bool active = true) =>
        (await admin.Post<JsonElement>("/api/admin/festivals", new { name, year = DateTime.UtcNow.Year, active })).GetProperty("id").GetString()!;

    [Fact]
    public async Task Owner_asks_staff_set_the_fine_owner_pays_and_the_document_verifies()
    {
        var admin = await Admin();
        var festival = await FestivalAsync(admin, "مهرجان ولي العهد للهجن — اختبار الاستثناء");
        var closed = await FestivalAsync(admin, "مهرجان موقوف — اختبار الاستثناء", active: false);

        // the owner sees active festivals only
        var owner = await app.OwnerAsync();
        var options = await owner.Get<JsonElement>("/api/me/exemptions/festivals");
        Assert.Contains(options.EnumerateArray(), f => f.GetProperty("id").GetString() == festival);
        Assert.DoesNotContain(options.EnumerateArray(), f => f.GetProperty("id").GetString() == closed);

        // validation
        Assert.Contains("شريحة", await owner.Fails(() => owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "12", camelType = "qaadan", festivalId = festival })));
        Assert.Contains("قعود أو بكرة", await owner.Fails(() => owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "900115000777001", camelType = "manqiya", festivalId = festival })));
        Assert.Contains("غير متاح", await owner.Fails(() => owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "900115000777001", camelType = "qaadan", festivalId = closed })));

        var e = await owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = " 900115000777001 ", camelType = "bakkar", festivalId = festival, race = "شوط سيف الملك", note = "البكرة مسجلة حديثًا" });
        Assert.Equal("pending", e.GetProperty("status").GetString());
        Assert.Equal("900115000777001", e.GetProperty("camelChip").GetString());
        Assert.StartsWith($"EX-{DateTime.UtcNow.Year}-", e.GetProperty("number").GetString());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("signature").ValueKind);
        var id = e.GetProperty("id").GetString();
        var number = e.GetProperty("number").GetString();
        // one open request per chip and festival
        Assert.Contains("طلب استثناء قائم", await owner.Fails(() => owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "900115000777001", camelType = "bakkar", festivalId = festival })));
        // nothing to pay before staff set the fine
        Assert.Contains("غير متاح", await owner.Fails(() => owner.Post<JsonElement>("/api/payments/card", new { purpose = "exemption", @ref = id, amount = 0, callback = "https://sammha.test/cb" })));
        Assert.Contains("قيد الدراسة", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/exemptions/{id}/pay")));

        // staff with exemptions.manage set the fine; the owner is notified
        var officer = await StaffAsync(admin, "0500000091", "exemptions.manage", "exemptions.view");
        var page = await officer.Get<JsonElement>($"/api/admin/exemptions?status=pending&q={number}");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Contains("•", page.GetProperty("items")[0].GetProperty("ownerPhone").GetString()); // masked without owners.full
        Assert.Contains("صفر", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/exemptions/{id}/price", new { amount = 0 })));
        await officer.Post<JsonElement>($"/api/admin/exemptions/{id}/price", new { amount = 4000 });
        var priced = await officer.Post<JsonElement>($"/api/admin/exemptions/{id}/price", new { amount = 5000, note = "غرامة عدم تسمية فردي بكار" });
        Assert.Equal("priced", priced.GetProperty("status").GetString());
        Assert.Equal(5000, priced.GetProperty("amount").GetDecimal());
        Assert.Contains((await owner.Get<JsonElement>("/api/me/notifications")).EnumerateArray(), n => n.GetProperty("kind").GetString() == "exemption");
        var mine = (await owner.Get<JsonElement>("/api/me/exemptions")).EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        Assert.False(mine.TryGetProperty("pricedByName", out var by) && by.ValueKind == JsonValueKind.String);

        // card only, like violations; no tax invoice
        await owner.TopUp(6000);
        var before = await owner.Balance();
        Assert.Contains("قنوات الدفع", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/exemptions/{id}/pay")));
        var paid = await owner.PayByCard("exemption", id, $"/api/me/exemptions/{id}/pay");
        Assert.Equal("paid", paid.GetProperty("status").GetString());
        Assert.Equal(before, await owner.Balance());
        Assert.Contains((await owner.Get<JsonElement>("/api/me/wallet")).GetProperty("txs").EnumerateArray(), t => t.GetProperty("kind").GetString() == "fine" && t.GetProperty("amount").GetDecimal() == -5000);
        var sig = paid.GetProperty("signature").GetString()!;
        Assert.Matches("^[A-Z0-9]{4}(-[A-Z0-9]{4})+$", sig);
        Assert.Equal("سعد بن ناصر العتيبي", (await owner.Get<JsonElement>($"/api/me/exemptions/{number}")).GetProperty("ownerName").GetString());
        // paying twice changes nothing; a closed request cannot be priced or cancelled
        Assert.Equal("paid", (await owner.Post<JsonElement>($"/api/me/exemptions/{id}/pay")).GetProperty("status").GetString());
        Assert.Contains("سُدّدت", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/exemptions/{id}/price", new { amount = 100 })));
        Assert.Contains("سُدّدت", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/exemptions/{id}/cancel")));
        // a paid chip at the same festival is not asked twice
        Assert.Contains("صدرت لهذه الشريحة", await owner.Fails(() => owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "900115000777001", camelType = "bakkar", festivalId = festival })));

        // anyone verifies: the number alone gives the status only, the printed code gives the details
        var anon = app.Anonymous();
        var bare = await anon.Get<JsonElement>($"/api/exemptions/issued/{number}");
        Assert.Equal("valid", bare.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, bare.GetProperty("camelChip").ValueKind);
        var full = await anon.Get<JsonElement>($"/api/exemptions/issued/{number}?s={sig}");
        Assert.True(full.GetProperty("signatureMatches").GetBoolean());
        Assert.Equal("900115000777001", full.GetProperty("camelChip").GetString());
        Assert.Equal("مهرجان ولي العهد للهجن — اختبار الاستثناء", full.GetProperty("festivalName").GetString());
        Assert.False((await anon.Get<JsonElement>($"/api/exemptions/issued/{number}?s=AAAA-BBBB-CCCC-DDDD")).GetProperty("signatureMatches").GetBoolean());

        // staff revoke the document with a reason: verification shows it revoked
        Assert.Contains("سبب", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/exemptions/{id}/revoke", new { reason = "" })));
        await officer.Post<JsonElement>($"/api/admin/exemptions/{id}/revoke", new { reason = "صدرت لشريحة خاطئة" });
        Assert.Equal("revoked", (await anon.Get<JsonElement>($"/api/exemptions/issued/{number}?s={sig}")).GetProperty("status").GetString());
        Assert.Contains("مسبقًا", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/exemptions/{id}/revoke", new { reason = "مرة ثانية للتجربة" })));
        // once revoked, the chip can be asked for again
        var again = await owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "900115000777001", camelType = "bakkar", festivalId = festival });
        Assert.Equal("pending", again.GetProperty("status").GetString());

        // staff reject with a reason; the owner withdraws another
        Assert.Contains("سبب الرفض", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/exemptions/{again.GetProperty("id").GetString()}/reject", new { reason = "" })));
        var rejected = await officer.Post<JsonElement>($"/api/admin/exemptions/{again.GetProperty("id").GetString()}/reject", new { reason = "الشريحة غير مسجلة باسمك" });
        Assert.Equal("rejected", rejected.GetProperty("status").GetString());
        var other = await owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "900115000777002", camelType = "qaadan", festivalId = festival });
        Assert.Equal("cancelled", (await owner.Post<JsonElement>($"/api/me/exemptions/{other.GetProperty("id").GetString()}/cancel")).GetProperty("status").GetString());
        Assert.Contains("سُحب", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/exemptions/{other.GetProperty("id").GetString()}/price", new { amount = 100 })));

        var counts = (await officer.Get<JsonElement>("/api/admin/exemptions?q=900115000777")).GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("paid").GetInt32());
        Assert.Equal(1, counts.GetProperty("rejected").GetInt32());
        Assert.Equal(1, counts.GetProperty("cancelled").GetInt32());
    }

    [Fact]
    public async Task Exemptions_are_private_and_need_their_permissions()
    {
        var admin = await Admin();
        var festival = await FestivalAsync(admin, "مهرجان الصلاحيات — اختبار الاستثناء");
        var owner = await app.BidderAsync(1);
        var e = await owner.Post<JsonElement>("/api/me/exemptions", new { camelChip = "900115000888001", camelType = "qaadan", festivalId = festival });
        var id = e.GetProperty("id").GetString();
        var number = e.GetProperty("number").GetString();

        // the default committee role has neither permission
        var committee = await app.CommitteeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Get<JsonElement>("/api/admin/exemptions")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>($"/api/admin/exemptions/{id}/price", new { amount = 5000 })));

        // view only: lists, cannot price or reject
        var viewer = await StaffAsync(admin, "0500000092", "exemptions.view");
        Assert.Equal(1, (await viewer.Get<JsonElement>($"/api/admin/exemptions?q={number}")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => viewer.Post<JsonElement>($"/api/admin/exemptions/{id}/price", new { amount = 5000 })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => viewer.Post<JsonElement>($"/api/admin/exemptions/{id}/reject", new { reason = "لا صلاحية" })));

        // another owner sees nothing of it and cannot act on it
        var stranger = await app.OwnerAsync();
        Assert.DoesNotContain((await stranger.Get<JsonElement>("/api/me/exemptions")).EnumerateArray(), x => x.GetProperty("id").GetString() == id);
        Assert.Equal(HttpStatusCode.NotFound, await StatusOf(() => stranger.Post<JsonElement>($"/api/me/exemptions/{id}/cancel")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => stranger.Get<JsonElement>($"/api/me/exemptions/{number}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Get<JsonElement>("/api/admin/exemptions")));

        // an unpaid request is not verifiable
        Assert.Equal(HttpStatusCode.NoContent, await StatusOf(async () =>
        {
            var r = await app.CreateClient().GetAsync($"/api/exemptions/issued/{number}");
            if (r.StatusCode != HttpStatusCode.OK) throw new ApiException(r.StatusCode, "");
            if ((await r.Content.ReadAsStringAsync()).Length == 0) throw new ApiException(HttpStatusCode.NoContent, "");
        }));
    }

    [Fact]
    public void Legacy_chip_numbers_are_read_from_free_text()
    {
        Assert.Equal("900115000123456", Sammha.Infrastructure.Legacy.LegacyImporter.ChipIn("الشريحة 900115000123456"));
        Assert.Equal("982000412345678", Sammha.Infrastructure.Legacy.LegacyImporter.ChipIn("رقم: 982000412345678 - بكرة"));
        Assert.Null(Sammha.Infrastructure.Legacy.LegacyImporter.ChipIn("بدون رقم 123"));
        Assert.Null(Sammha.Infrastructure.Legacy.LegacyImporter.ChipIn(null));
    }
}
