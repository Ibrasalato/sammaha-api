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
    [InlineData("مزن", "مزنة", MatchRelations.Singular)]
    [InlineData("الفاخره", "الفاخر", MatchRelations.Gender)]
    [InlineData("نسيم", "نسمة", MatchRelations.Similar)]
    public void Relation_names_the_kind_of_match(string candidate, string other, string expected) =>
        Assert.Equal(expected, NameMatcher.Relation(candidate, other));

    [Fact]
    public async Task Audit_lists_matches_with_the_highest_price()
    {
        var owner = await app.SignInAsync("0500000005");
        var committee = await app.CommitteeAsync();
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
        Assert.Equal("singular", mazn.GetProperty("matches")[0].GetProperty("relation").GetString());
        Assert.Equal("clear", rows.Single(x => x.GetProperty("name").GetString() == "نسيم الشمال").GetProperty("verdict").GetString());

        await Assert.ThrowsAsync<ApiException>(() => owner.Get<JsonElement>("/api/admin/requests/audit"));
    }
}
