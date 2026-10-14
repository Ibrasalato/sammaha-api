using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Admin platform settings, intake switches, maintenance, security policy and personal settings.</summary>
public class SettingsTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");

    [Fact]
    public async Task Only_admins_change_settings_and_every_change_is_logged()
    {
        var committee = await app.CommitteeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Get<JsonElement>("/api/admin/settings")));
        var owner = await app.OwnerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { requestsOpen = false })));

        var admin = await Admin();
        var s = await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { supportEmail = "help@sammha.sa", announcementOn = true, announcement = "المزاد الكبير يبدأ الخميس", announcementTone = "warn" });
        Assert.Equal("help@sammha.sa", s.GetProperty("supportEmail").GetString());
        Assert.Equal(50, s.GetProperty("rules").GetProperty("depositAmount").GetDecimal());

        var pub = await app.Anonymous().Get<JsonElement>("/api/settings");
        Assert.Equal("المزاد الكبير يبدأ الخميس", pub.GetProperty("announcement").GetString());
        Assert.Equal("warn", pub.GetProperty("announcementTone").GetString());

        var history = await admin.Get<JsonElement>("/api/admin/settings/history");
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("field").GetString() == "SupportEmail" && h.GetProperty("newValue").GetString() == "help@sammha.sa");

        var bad = await admin.Fails(() => admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { passwordMinLength = 4 }));
        Assert.Contains("بين 8 و32", bad);
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { announcementOn = false });
    }

    [Fact]
    public async Task Closed_intakes_reject_new_owner_requests()
    {
        var admin = await Admin();
        var owner = await app.SignInAsync("0500000004");
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new
        {
            requestsOpen = false, requestsClosedNote = "يعاد الفتح بداية الشهر", compoundOpen = false, investmentOpen = false, resaleAuctionOpen = false, instantSaleOpen = false,
        });
        var pub = await app.Anonymous().Get<JsonElement>("/api/settings");
        Assert.False(pub.GetProperty("requestsOpen").GetBoolean());
        Assert.Equal("يعاد الفتح بداية الشهر", pub.GetProperty("requestsClosedNote").GetString());

        var msg = await owner.Fails(() => owner.Post<JsonElement>("/api/me/requests", new { type = "bakkar", names = new[] { "نور الفجر", "سهيل الجنوب", "ريح الصبا" } }));
        Assert.Contains("يعاد الفتح بداية الشهر", msg);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Post<JsonElement>("/api/me/requests/investment", new { type = "bakkar", names = new[] { "نور الفجر" } })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Post<JsonElement>("/api/me/requests/compound", new { reservedId = Guid.NewGuid(), fullName = "x y" })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Post<JsonElement>("/api/me/listings", new { reservedId = Guid.NewGuid(), mode = "instant", price = 1000 })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => owner.Post<JsonElement>("/api/me/listings", new { reservedId = Guid.NewGuid(), mode = "auction", price = 1000 })));

        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { requestsOpen = true, compoundOpen = true, investmentOpen = true, resaleAuctionOpen = true, instantSaleOpen = true });
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "bakkar", names = new[] { "نور الفجر", "سهيل الجنوب", "ريح الصبا" } });
        Assert.Equal("pending_payment", r.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Maintenance_blocks_owner_actions_but_not_staff_or_settings()
    {
        var admin = await Admin();
        var owner = await app.SignInAsync("0500000005");
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { maintenance = true, maintenanceNote = "صيانة حتى الساعة 10" });
        try
        {
            var e = await Assert.ThrowsAsync<ApiException>(() => owner.TopUp(100));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, e.Status);
            Assert.Contains("صيانة حتى الساعة 10", e.Message);
            // reading, personal settings and staff work keep going
            await owner.Get<JsonElement>("/api/me/wallet");
            await owner.Put<JsonElement>("/api/me/preferences", new { notifyOutbid = false, notifyReminders = true, notifyResults = true, notifyRequests = true });
            await admin.Get<JsonElement>("/api/admin/overview");
            Assert.True((await app.Anonymous().Get<JsonElement>("/api/settings")).GetProperty("maintenance").GetBoolean());
        }
        finally { await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { maintenance = false }); }
        await owner.TopUp(100);
    }

    [Fact]
    public async Task Signup_switch_and_password_policy()
    {
        var admin = await Admin();
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { signupOpen = false });
        var anon = app.Anonymous();
        var msg = await anon.Fails(() => anon.SignUp(new { nationalId = "1098765432", firstName = "سعد", fatherName = "محمد", grandfatherName = "عبدالله", familyName = "الدوسري", birthDate = "1985-01-20", gender = "male", nationality = "SA", residenceCountry = "SA", residenceCity = "جدة", phoneCountry = "966", phone = "0557778899", email = "s.d@example.com", password = "longpassword1" }));
        Assert.Contains("متوقف", msg);
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { signupOpen = true, passwordMinLength = 10 });
        msg = await anon.Fails(() => anon.SignUp(new { nationalId = "1098765432", firstName = "سعد", fatherName = "محمد", grandfatherName = "عبدالله", familyName = "الدوسري", birthDate = "1985-01-20", gender = "male", nationality = "SA", residenceCountry = "SA", residenceCity = "جدة", phoneCountry = "966", phone = "0557778899", email = "s.d@example.com", password = "short123" }));
        Assert.Contains("10 أحرف", msg);
        await anon.SignUp(new { nationalId = "1098765432", firstName = "سعد", fatherName = "محمد", grandfatherName = "عبدالله", familyName = "الدوسري", birthDate = "1985-01-20", gender = "male", nationality = "SA", residenceCountry = "SA", residenceCity = "جدة", phoneCountry = "966", phone = "0557778899", email = "s.d@example.com", password = "longpassword1" });
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { passwordMinLength = 8 });
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_sign_in()
    {
        var admin = await Admin();
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { maxLoginAttempts = 3, lockoutMinutes = 10 });
        var anon = app.Anonymous();
        for (var i = 0; i < 3; i++)
            await anon.Fails(() => anon.Post<JsonElement>("/api/auth/signin", new { login = "0500000003", password = "wrong-password", portal = "owner" }));
        var e = await Assert.ThrowsAsync<ApiException>(() => anon.Post<JsonElement>("/api/auth/signin", new { login = "0500000003", password = "123456", portal = "owner" }));
        Assert.Equal((HttpStatusCode)429, e.Status);
        Assert.Contains("دقيقة", e.Message);

        app.Clock.Advance(TimeSpan.FromMinutes(11));
        var ok = await app.SignInAsync("0500000003"); // the lock expired and a correct password clears the counter
        Assert.NotNull(ok.Token);
        await admin.Send<JsonElement>(HttpMethod.Patch, "/api/admin/settings", new { maxLoginAttempts = 5, lockoutMinutes = 15 });
    }

    [Fact]
    public async Task Preferences_sessions_and_revoking_a_device()
    {
        var laptop = await app.SignInAsync("0500000001");
        var phone = await app.SignInAsync("0500000001");

        var prefs = await laptop.Put<JsonElement>("/api/me/preferences", new { notifyOutbid = false, notifyReminders = true, notifyResults = false, notifyRequests = true });
        Assert.False(prefs.GetProperty("notifyOutbid").GetBoolean());
        Assert.False((await phone.Get<JsonElement>("/api/me/preferences")).GetProperty("notifyResults").GetBoolean());

        var sessions = (await laptop.Get<JsonElement>("/api/me/sessions")).EnumerateArray().ToList();
        Assert.True(sessions.Count >= 2);
        Assert.Single(sessions, s => s.GetProperty("current").GetBoolean());
        var other = (await phone.Get<JsonElement>("/api/me/sessions")).EnumerateArray().First(s => s.GetProperty("current").GetBoolean()).GetProperty("id").GetString();

        var msg = await phone.Fails(() => phone.Send<JsonElement>(HttpMethod.Delete, $"/api/me/sessions/{other}", null));
        Assert.Contains("جهازك الحالي", msg);
        var after = await laptop.Send<JsonElement>(HttpMethod.Delete, $"/api/me/sessions/{other}", null);
        Assert.DoesNotContain(after.EnumerateArray(), s => s.GetProperty("id").GetString() == other);
        // the revoked device cannot renew its session
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => phone.Post<JsonElement>("/api/auth/refresh", new { refreshToken = phone.Refresh })));

        var sec = await laptop.Get<JsonElement>("/api/me/security");
        Assert.True(sec.TryGetProperty("lastLoginAt", out _));
        await laptop.Put<JsonElement>("/api/me/preferences", new { notifyOutbid = true, notifyReminders = true, notifyResults = true, notifyRequests = true });
    }

    [Fact]
    public async Task Admin_can_sign_out_everyone_else()
    {
        var admin = await Admin();
        var owner = await app.SignInAsync("0500000004");
        var n = await admin.Post<JsonElement>("/api/admin/settings/sign-out-everyone");
        Assert.True(n.GetProperty("users").GetInt32() > 0);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => owner.Get<JsonElement>("/api/me/wallet")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => owner.Post<JsonElement>("/api/auth/refresh", new { refreshToken = owner.Refresh })));
        await admin.Get<JsonElement>("/api/admin/settings"); // the admin stays signed in
    }
}
