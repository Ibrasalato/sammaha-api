using System.Text.Json;
using Sammha.Domain;

namespace Sammha.Tests;

/// <summary>The committee's audit of requested names: plural/singular forms, «ال», and the highest price of a similar name.</summary>
public class AuditTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData("صقر", "صقور", true)]
    [InlineData("نجم", "نجوم", true)]
    [InlineData("غزال", "غزلان", true)]
    [InlineData("شاهين", "شواهين", true)]
    [InlineData("غزالة", "غزالات", true)]
    [InlineData("سلطان", "سلاطين", true)]
    [InlineData("الريم", "رومي", false)]
    [InlineData("برق", "ورق", false)]
    public void Singular_and_plural_forms_meet(string a, string b, bool same) =>
        Assert.Equal(same, NameMatcher.SameForms(NameMatcher.Normalize(a), NameMatcher.Normalize(b)));

    [Theory]
    [InlineData("الهدباء", "هدباء", MatchRelations.Article)]
    [InlineData("الفاخر", "الفاخر", MatchRelations.Exact)]
    [InlineData("سلاطين", "سلطان", MatchRelations.Plural)]
    [InlineData("مزن", "مزنة", MatchRelations.Gender)]
    [InlineData("صقر", "صقور", MatchRelations.Singular)]
    [InlineData("الفاخره", "الفاخر", MatchRelations.Gender)]
    [InlineData("الريم", "الرين", MatchRelations.Similar)]
    public void Relation_names_the_kind_of_match(string candidate, string other, string expected) =>
        Assert.Equal(expected, NameMatcher.Relation(candidate, other));

    [Fact]
    public async Task Audit_lists_matches_with_the_highest_price()
    {
        var owner = await app.SignInAsync("0500000005");
        var committee = await app.CommitteeAsync();
        // the audit is its own permission: the default committee role does not have it until the admin adds it
        await Assert.ThrowsAsync<ApiException>(() => committee.Get<JsonElement>("/api/admin/requests/audit"));
        committee = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "bakkar", names = new[] { "سلاطين", "مزن", "نسيم الشمال" } });
        var id = r.GetProperty("id").GetString();
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");

        var rows = (await committee.Get<JsonElement>("/api/admin/requests/audit?status=under_review")).EnumerateArray().Where(x => x.GetProperty("requestId").GetString() == id).ToList();
        Assert.Equal(3, rows.Count);
        var sultan = rows.Single(x => x.GetProperty("name").GetString() == "سلاطين");
        Assert.Equal("similar", sultan.GetProperty("verdict").GetString());
        var m = sultan.GetProperty("matches").EnumerateArray().First(x => x.GetProperty("name").GetString() == "سلطان");
        Assert.Equal("plural", m.GetProperty("relation").GetString());
        Assert.Equal(12500, sultan.GetProperty("topPrice").GetProperty("price").GetDecimal());

        var mazn = rows.Single(x => x.GetProperty("name").GetString() == "مزن");
        Assert.Equal("مزنة", mazn.GetProperty("topPrice").GetProperty("name").GetString());
        Assert.Equal("gender", mazn.GetProperty("matches")[0].GetProperty("relation").GetString());
        Assert.Equal("clear", rows.Single(x => x.GetProperty("name").GetString() == "نسيم الشمال").GetProperty("verdict").GetString());

        await Assert.ThrowsAsync<ApiException>(() => owner.Get<JsonElement>("/api/admin/requests/audit"));
    }

    [Fact]
    public async Task Customers_page_masks_ids_without_full_access()
    {
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var all = (await admin.Get<JsonElement>("/api/admin/owners")).EnumerateArray().ToList();
        var wasm = all.Single(o => o.GetProperty("fullName").GetString() == "مزرعة الوسم للإبل");
        Assert.Equal("0500000003", wasm.GetProperty("phone").GetString());
        Assert.False(wasm.GetProperty("masked").GetBoolean());
        Assert.Contains(wasm.GetProperty("names").EnumerateArray(), n => n.GetProperty("name").GetString() == "الفاخر" && n.GetProperty("type").GetString() == "qaadan");
        Assert.True(wasm.GetProperty("totalValue").GetDecimal() >= 4200);

        var role = await admin.Post<JsonElement>("/api/admin/roles", new { name = "علاقات العملاء", permissions = new[] { "owners.view" } });
        await admin.Post<JsonElement>("/api/admin/users", new { fullName = "موظف العملاء", phone = "0500000081", email = "crm81@example.com", password = "Staff#2026x", role = "committee", staffRoleId = role.GetProperty("id").GetString() });
        var crm = await app.SignInAsync("0500000081", "Staff#2026x", "staff");
        var masked = (await crm.Get<JsonElement>("/api/admin/owners")).EnumerateArray().Single(o => o.GetProperty("fullName").GetString() == "مزرعة الوسم للإبل");
        Assert.Equal("05•••••003", masked.GetProperty("phone").GetString());
        Assert.True(masked.GetProperty("masked").GetBoolean());
        await Assert.ThrowsAsync<ApiException>(() => crm.Post<JsonElement>("/api/admin/owners/report", new { kind = "print", count = 3 }));

        await admin.Post<JsonElement>("/api/admin/owners/report", new { kind = "csv", count = all.Count, filter = "قعدان" });
        var history = await admin.Get<JsonElement>("/api/admin/settings/history");
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("field").GetString() == "OwnersReport");
    }
}
