using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Account delegation (scopes, period, switching in and out, revoking) and the activity log.</summary>
public class DelegationTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private static object Fahad(string[] scopes, bool permanent = false, DateTimeOffset? ends = null) => new
    {
        fullName = "فهد عبدالله حمد الدوسري", nationalId = "1045678903", phoneCountry = "966", phone = "0500000004", email = "fahad.d@example.com",
        capacity = "family", scopes, permanent, endsAt = ends,
    };

    [Fact]
    public async Task Delegate_acts_within_the_granted_scopes()
    {
        var owner = await app.OwnerAsync();
        Assert.Contains("نفسك", await owner.Fails(() => owner.Post<JsonElement>("/api/me/delegations", new { fullName = "سعد ناصر عبدالله العتيبي", nationalId = "1023456781", phoneCountry = "966", phone = "0511111111", email = "x@example.com", capacity = "agent", permanent = true })));
        Assert.Contains("نهاية", await owner.Fails(() => owner.Post<JsonElement>("/api/me/delegations", Fahad(["bids"]))));
        var d = await owner.Post<JsonElement>("/api/me/delegations", Fahad(["bids", "tickets"], ends: app.Clock.GetUtcNow().AddDays(7)));
        Assert.True(d.GetProperty("linked").GetBoolean());
        Assert.Equal("active", d.GetProperty("status").GetString());
        var id = d.GetProperty("id").GetString();

        var fahad = await app.SignInAsync("0500000004");
        var mine = (await fahad.Get<JsonElement>("/api/me/delegated")).EnumerateArray().ToList();
        Assert.Contains(mine, x => x.GetProperty("id").GetString() == id && x.GetProperty("ownerName").GetString() == "سعد بن ناصر العتيبي");
        var acting = app.Anonymous();
        var res = await fahad.Post<JsonElement>("/api/auth/act-as", new { delegationId = id });
        acting.Token = res.GetProperty("token").GetString(); acting.Refresh = res.GetProperty("refreshToken").GetString();
        var me = await acting.Get<JsonElement>("/api/auth/me");
        Assert.Equal("سعد بن ناصر العتيبي", me.GetProperty("fullName").GetString());
        Assert.Equal("فهد عبدالله الدوسري", me.GetProperty("acting").GetProperty("actorName").GetString());

        // within scope: watching an auction; viewing is allowed
        var auction = (await app.Anonymous().Get<JsonElement>("/api/auctions")).EnumerateArray().First().GetProperty("id").GetString();
        await acting.Post<JsonElement>($"/api/auctions/{auction}/watch");
        await acting.Get<JsonElement>("/api/me/wallet");
        // outside scope or owner-only
        Assert.Contains("المحفظة والفواتير", await acting.Fails(() => acting.TopUp(100)));
        Assert.Contains("لصاحب الحساب", await acting.Fails(() => acting.Post<JsonElement>("/api/auth/password", new { current = "123456", next = "Hacked#2026" })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => acting.Get<JsonElement>("/api/me/bank-account")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => acting.Post<JsonElement>("/api/me/delegations", Fahad(["payments"], true))));

        // the log names the delegate, on the owner's behalf
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var log = (await admin.Get<JsonElement>("/api/admin/activity?category=auctions")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(log, a => a.GetProperty("actorName").GetString() == "فهد عبدالله الدوسري" && a.GetProperty("onBehalfOfName").GetString() == "سعد بن ناصر العتيبي" && a.GetProperty("action").GetString() == "متابعة مزاد");
        var failed = (await admin.Get<JsonElement>("/api/admin/activity?failed=true")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(failed, a => a.GetProperty("action").GetString() == "شحن المحفظة" && a.GetProperty("error").GetString()!.Contains("التفويض لا يشمل"));
        Assert.DoesNotContain(failed.Concat(log), a => (a.GetProperty("details").GetString() ?? "").Contains("Hacked"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Get<JsonElement>("/api/admin/activity")));

        // the owner's own log shows the delegate's actions on the account; the delegate sees his side; neither sees the other's private log
        var ownerLog = (await owner.Get<JsonElement>("/api/me/activity?who=delegates")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(ownerLog, a => a.GetProperty("action").GetString() == "متابعة مزاد" && a.GetProperty("actorName").GetString() == "فهد عبدالله الدوسري");
        Assert.All(ownerLog, a => Assert.Equal("سعد بن ناصر العتيبي", a.GetProperty("onBehalfOfName").GetString()));
        var fahadLog = (await fahad.Get<JsonElement>("/api/me/activity?who=as_delegate")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(fahadLog, a => a.GetProperty("action").GetString() == "شحن المحفظة" && !a.GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => acting.Get<JsonElement>("/api/me/activity")));

        // back to his own account
        var back = await acting.Post<JsonElement>("/api/auth/act-as/stop");
        Assert.Equal("فهد عبدالله الدوسري", back.GetProperty("user").GetProperty("fullName").GetString());

        // revoking ends the delegated session at once
        var again = await fahad.Post<JsonElement>("/api/auth/act-as", new { delegationId = id });
        acting.Token = again.GetProperty("token").GetString(); acting.Refresh = again.GetProperty("refreshToken").GetString();
        await owner.Post<JsonElement>($"/api/me/delegations/{id}/revoke");
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => acting.Get<JsonElement>("/api/me/wallet")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => acting.Post<JsonElement>("/api/auth/refresh", new { refreshToken = acting.Refresh })));
        Assert.Contains("ملغى", await fahad.Fails(() => fahad.Post<JsonElement>("/api/auth/act-as", new { delegationId = id })));
    }

    [Fact]
    public async Task Expired_and_pending_delegations()
    {
        var owner = await app.SignInAsync("0500000003");
        // a person without an account yet: linked when they sign up with the same ID and mobile
        var d = await owner.Post<JsonElement>("/api/me/delegations", new { fullName = "ماجد سعيد علي الشهري", nationalId = "1077777777", phoneCountry = "966", phone = "0577777777", email = "majed@example.com", capacity = "employee", scopes = new[] { "requests" }, permanent = true });
        Assert.False(d.GetProperty("linked").GetBoolean());
        Assert.Equal("awaiting_account", d.GetProperty("status").GetString());
        var anon = app.Anonymous();
        var r = await anon.SignUp(new { nationalId = "1077777777", firstName = "ماجد", fatherName = "سعيد", grandfatherName = "علي", familyName = "الشهري", birthDate = "1990-01-01", gender = "male", nationality = "SA", residenceCountry = "SA", residenceCity = "أبها", phoneCountry = "966", phone = "0577777777", email = "majed@example.com", password = "Majed#2026" });
        anon.Token = r.GetProperty("token").GetString();
        var list = (await anon.Get<JsonElement>("/api/me/delegated")).EnumerateArray().ToList();
        Assert.Equal("active", list.Single().GetProperty("status").GetString());

        // a temporary delegation stops working when it ends
        var t = await owner.Post<JsonElement>("/api/me/delegations", Fahad(["bids"], ends: app.Clock.GetUtcNow().AddHours(2)));
        var fahad = await app.SignInAsync("0500000004");
        var s = await fahad.Post<JsonElement>("/api/auth/act-as", new { delegationId = t.GetProperty("id").GetString() });
        var acting = app.Anonymous(); acting.Token = s.GetProperty("token").GetString();
        await acting.Get<JsonElement>("/api/me/wallet");
        app.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Contains("انتهت", await fahad.Fails(() => fahad.Post<JsonElement>("/api/auth/act-as", new { delegationId = t.GetProperty("id").GetString() })));
    }
}
