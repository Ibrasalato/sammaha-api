using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>«الحظر»: staff with bans.manage bar an owner for a period; a banned owner cannot bid, buy, sell or send new requests, but can sign in and pay.</summary>
public class BanTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
    private static string Day(int offset) => DateTime.UtcNow.AddHours(3).Date.AddDays(offset).ToString("yyyy-MM-dd");

    [Fact]
    public async Task A_banned_owner_cannot_take_part_until_it_is_lifted()
    {
        var admin = await Admin();
        var owner = await app.BidderAsync(2);
        Assert.Equal(HttpStatusCode.NoContent, await StatusOf(async () =>
        {
            var r = await app.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/me/ban") { Headers = { { "Authorization", $"Bearer {owner.Token}" } } });
            if (r.StatusCode != HttpStatusCode.OK || (await r.Content.ReadAsStringAsync()).Length == 0) throw new ApiException(HttpStatusCode.NoContent, "");
        }));

        Assert.Contains("سبب", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/bans", new { owner = "0500000004", reason = "", startsOn = Day(0) })));
        Assert.Contains("قبل", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/bans", new { owner = "0500000004", reason = "تلاعب بالمزايدات", startsOn = Day(0), endsOn = Day(-1) })));
        var ban = await admin.Post<JsonElement>("/api/admin/bans", new { owner = "0500000004", reason = "تلاعب بالمزايدات", startsOn = Day(0), endsOn = Day(30) });
        Assert.Equal("active", ban.GetProperty("status").GetString());
        var id = ban.GetProperty("id").GetString();

        // the owner is told, sees the ban, and is stopped from taking part
        Assert.Contains((await owner.Get<JsonElement>("/api/me/notifications")).EnumerateArray(), n => n.GetProperty("kind").GetString() == "ban");
        Assert.Equal("تلاعب بالمزايدات", (await owner.Get<JsonElement>("/api/me/ban")).GetProperty("reason").GetString());
        var msg = await owner.Fails(() => owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names = new[] { "اسم للحظر" } }));
        Assert.Contains("محظور", msg);
        // signing in and reading still work
        await owner.Get<JsonElement>("/api/me/wallet");

        // staff list and filter
        var page = await admin.Get<JsonElement>("/api/admin/bans?status=active&q=0500000004");
        Assert.Equal(1, page.GetProperty("total").GetInt32());

        // a view-only role cannot ban; the default committee role cannot even see the list
        var committee = await app.CommitteeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Get<JsonElement>("/api/admin/bans")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>("/api/admin/bans", new { owner = "0500000001", reason = "بلا صلاحية", startsOn = Day(0) })));

        // moved to start tomorrow: not in force today
        var later = await admin.Put<JsonElement>($"/api/admin/bans/{id}", new { reason = "تلاعب بالمزايدات", startsOn = Day(1), endsOn = Day(30) });
        Assert.Equal("scheduled", later.GetProperty("status").GetString());
        Assert.DoesNotContain("محظور", await owner.Fails(() => owner.Post<JsonElement>("/api/me/requests", new { type = "manqiya", names = Array.Empty<string>() })));

        // lifted with a reason; cannot be lifted twice
        Assert.Contains("سبب", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/bans/{id}/lift", new { reason = "" })));
        var lifted = await admin.Post<JsonElement>($"/api/admin/bans/{id}/lift", new { reason = "انتهى التحقيق" });
        Assert.Equal("lifted", lifted.GetProperty("status").GetString());
        Assert.Contains("مسبقًا", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/bans/{id}/lift", new { reason = "مرة ثانية" })));
    }

    [Fact]
    public async Task A_ban_by_national_id_catches_the_account_with_that_id()
    {
        var admin = await Admin();
        // someone not registered: a national ID and a name are enough
        Assert.Contains("لا يوجد مالك", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/bans", new { owner = "0599999999", reason = "اختبار", startsOn = Day(0) })));
        var outsider = await admin.Post<JsonElement>("/api/admin/bans", new { nationalId = "2888888888", name = "شخص غير مسجل", reason = "مستبعد بقرار اللجنة", startsOn = Day(0) });
        Assert.False(outsider.TryGetProperty("ownerId", out var oid) && oid.ValueKind == JsonValueKind.String);
        Assert.Equal("active", outsider.GetProperty("status").GetString());

        // a national ID of a registered owner links the ban to the account
        var linked = await admin.Post<JsonElement>("/api/admin/bans", new { nationalId = "1023456781", name = "", reason = "مستبعد بقرار اللجنة", startsOn = Day(0), endsOn = Day(2) });
        Assert.Equal(JsonValueKind.String, linked.GetProperty("ownerId").ValueKind);
        var owner = await app.OwnerAsync();
        Assert.Contains("محظور", await owner.Fails(() => owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names = new[] { "اسم بالهوية" } })));
        await admin.Post<JsonElement>($"/api/admin/bans/{linked.GetProperty("id").GetString()}/lift", new { reason = "انتهى الاختبار" });
        Assert.DoesNotContain("محظور", await owner.Fails(() => owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names = Array.Empty<string>() })));
    }
}
