using System.Net;
using System.Text.Json;
using Sammha.Application.Common;

namespace Sammha.Tests;

/// <summary>Bilingual registration certificates (issue, verify, revoke) and Latin spellings of names.</summary>
public class CertificateTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    [Theory]
    [InlineData("الهيلع", "Al-Hayla'")]
    [InlineData("الصارم", "Al-Sarim")]
    [InlineData("الشامخ", "Al-Shamikh")]
    [InlineData("ريم", "Reem")]
    [InlineData("مزنة", "Muznah")]
    [InlineData("بنت الشمال", "Bint Al-Shamal")]
    [InlineData("كبير", "Kabir")]
    [InlineData("هدية", "Hadiyah")]
    [InlineData("فهد", "Fahd")]
    [InlineData("Already Latin", "Already Latin")]
    public void Transliterates_camel_names(string ar, string latin) => Assert.Equal(latin, Transliterator.ToLatin(ar));

    [Fact]
    public async Task Owner_issues_a_mark_certificate_and_anyone_verifies_it()
    {
        var owner = await app.OwnerAsync();
        var mine = (await owner.Get<JsonElement>("/api/me/names")).EnumerateArray().ToList();
        var mark = mine.First(n => n.GetProperty("name").GetString() == "الشرهات");
        Assert.Equal("Al-Sharahat", mark.GetProperty("latin").GetString());

        // someone else's mark cannot be certified by this owner
        var b1Mark = (await app.Anonymous().Get<JsonElement>("/api/names?q=الفاخر")).EnumerateArray().First().GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Post<JsonElement>("/api/me/certificates", new { scope = "mark", reservedNameId = b1Mark })));
        Assert.Contains("جواز", await owner.Fails(() => owner.Post<JsonElement>("/api/me/certificates", new { scope = "mark", reservedNameId = mark.GetProperty("id").GetString(), ownerNameEn = "سعد" })));

        var c = await owner.Post<JsonElement>("/api/me/certificates", new { scope = "mark", reservedNameId = mark.GetProperty("id").GetString(), ownerNameEn = "Saad Nasser Abdullah Al-Otaibi" });
        var number = c.GetProperty("number").GetString()!;
        var sig = c.GetProperty("signature").GetString()!;
        Assert.Matches(@"^SC-\d{4}-\d{6}$", number);
        Assert.Matches(@"^[A-Z2-9]{4}(-[A-Z2-9]{4}){3}$", sig);
        Assert.Equal("valid", c.GetProperty("status").GetString());
        Assert.Equal("Saad Nasser Abdullah Al-Otaibi", c.GetProperty("ownerNameEn").GetString());
        Assert.EndsWith("6781", c.GetProperty("nationalIdMasked").GetString());
        Assert.DoesNotContain("1023456781", c.GetRawText());
        var item = Assert.Single(c.GetProperty("items").EnumerateArray());
        Assert.Equal("Al-Sharahat", item.GetProperty("latin").GetString());
        Assert.Contains("Manqiya", item.GetProperty("typeLabel").GetString());

        // public verification: the QR carries the signature code
        var anon = app.Anonymous();
        var v = await anon.Get<JsonElement>($"/api/certificates/issued/{number}?s={sig}");
        Assert.Equal("valid", v.GetProperty("status").GetString());
        Assert.True(v.GetProperty("signatureMatches").GetBoolean());
        Assert.False(v.TryGetProperty("nationalId", out _));
        Assert.False(v.TryGetProperty("nationalIdMasked", out _));
        var bad = await anon.Get<JsonElement>($"/api/certificates/issued/{number}?s=AAAA-BBBB-CCCC-DDDD");
        Assert.False(bad.GetProperty("signatureMatches").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await anon.Get<JsonElement>("/api/certificates/issued/SC-2026-999999")).ValueKind);

        // it is listed, notified and opens for the owner
        Assert.Contains((await owner.Get<JsonElement>("/api/me/certificates")).EnumerateArray(), x => x.GetProperty("number").GetString() == number);
        Assert.Contains((await owner.Get<JsonElement>("/api/me/notifications")).EnumerateArray(), n => n.GetProperty("title").GetString()!.Contains(number));
        var other = await app.BidderAsync(1);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => other.Get<JsonElement>($"/api/me/certificates/{number}")));

        // staff revoke it with a reason; verification then says so
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Get<JsonElement>("/api/admin/certificates")));
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var page = await admin.Get<JsonElement>($"/api/admin/certificates?q={number}");
        var id = page.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetString();
        Assert.Contains("سبب", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/certificates/{id}/revoke", new { reason = "" })));
        await admin.Post<JsonElement>($"/api/admin/certificates/{id}/revoke", new { reason = "صدرت ببيانات تحتاج تصحيحًا" });
        var after = await anon.Get<JsonElement>($"/api/certificates/issued/{number}");
        Assert.Equal("revoked", after.GetProperty("status").GetString());
        Assert.Equal("صدرت ببيانات تحتاج تصحيحًا", after.GetProperty("revokeReason").GetString());
    }

    [Fact]
    public async Task Portfolio_certificate_covers_every_mark_and_needs_a_profile()
    {
        var b1 = await app.BidderAsync(1); // عبدالرحمن القحطاني, several legacy marks
        var count = (await b1.Get<JsonElement>("/api/me/names")).EnumerateArray().Count();
        Assert.True(count >= 5);
        var c = await b1.Post<JsonElement>("/api/me/certificates", new { scope = "portfolio" });
        Assert.Equal("portfolio", c.GetProperty("scope").GetString());
        Assert.Equal(count, c.GetProperty("items").GetArrayLength());
        Assert.Equal("Abdulrahman Mohammed Saad Al-Qahtani", c.GetProperty("ownerNameEn").GetString());

        // an older account without a profile has to complete it first
        var nayef = await app.SignInAsync("0500000005");
        Assert.Contains("أكمل بياناتك", await nayef.Fails(() => nayef.Post<JsonElement>("/api/me/certificates", new { scope = "portfolio" })));
    }

    [Fact]
    public async Task Staff_correct_latin_spellings_and_search_finds_them()
    {
        var anon = app.Anonymous();
        Assert.Contains((await anon.Get<JsonElement>("/api/names?q=al-ghaymah")).EnumerateArray(), n => n.GetProperty("name").GetString() == "الغيمة");
        var id = (await anon.Get<JsonElement>("/api/names?q=الغيمة")).EnumerateArray().First().GetProperty("id").GetString();

        var owner = await app.OwnerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Put<JsonElement>($"/api/admin/names/{id}/latin", new { latin = "Al-Ghaima" })));
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        Assert.Contains("لاتينية", await admin.Fails(() => admin.Put<JsonElement>($"/api/admin/names/{id}/latin", new { latin = "الغيمة" })));
        var r = await admin.Put<JsonElement>($"/api/admin/names/{id}/latin", new { latin = "Al-Ghaima" });
        Assert.Equal("Al-Ghaima", r.GetProperty("latin").GetString());
        Assert.True(r.GetProperty("latinOverride").GetBoolean());
        Assert.Contains((await anon.Get<JsonElement>("/api/names?q=ghaima")).EnumerateArray(), n => n.GetProperty("name").GetString() == "الغيمة");
        // clearing goes back to the transliteration
        var back = await admin.Put<JsonElement>($"/api/admin/names/{id}/latin", new { latin = "" });
        Assert.Equal("Al-Ghaymah", back.GetProperty("latin").GetString());
    }
}
