using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Regression tests for the authentication, delegation and staff-permission hardening.</summary>
public class SecurityAuthTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");

    private async Task<(Api Owner, Api Delegate, Api Acting)> ActingAsync()
    {
        var owner = await app.OwnerAsync();
        var mine = (await owner.Get<JsonElement>("/api/me/delegations")).EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("status").GetString() == "active");
        var id = mine.ValueKind == JsonValueKind.Object ? mine.GetProperty("id").GetString() : (await owner.Post<JsonElement>("/api/me/delegations", new
        {
            fullName = "فهد عبدالله حمد الدوسري", nationalId = "1045678903", phoneCountry = "966", phone = "0500000004", email = "fahad.d@mail.example.sa",
            capacity = "family", scopes = new[] { "bids" }, permanent = true,
        })).GetProperty("id").GetString();
        var fahad = await app.SignInAsync("0500000004");
        var res = await fahad.Post<JsonElement>("/api/auth/act-as", new { delegationId = id });
        var acting = app.Anonymous();
        acting.Token = res.GetProperty("token").GetString();
        acting.Refresh = res.GetProperty("refreshToken").GetString();
        return (owner, fahad, acting);
    }

    [Fact]
    public async Task A_delegate_signing_out_everywhere_ends_only_the_delegated_session()
    {
        var (owner, fahad, acting) = await ActingAsync();
        await acting.Post<JsonElement>("/api/auth/signout", new { refreshToken = acting.Refresh, everywhere = true });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => acting.Post<JsonElement>("/api/auth/refresh", new { refreshToken = acting.Refresh })));
        // the owner's own devices and the delegate's own account are untouched
        await owner.Get<JsonElement>("/api/me/wallet");
        var o = await owner.Post<JsonElement>("/api/auth/refresh", new { refreshToken = owner.Refresh });
        Assert.False(string.IsNullOrEmpty(o.GetProperty("token").GetString()));
        await fahad.Post<JsonElement>("/api/auth/refresh", new { refreshToken = fahad.Refresh });
        // a leftover access token of the ended delegated session cannot be swapped for a fresh session of the delegate
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => acting.Post<JsonElement>("/api/auth/act-as/stop")));
    }

    [Fact]
    public async Task Signing_the_delegate_out_everywhere_also_ends_their_delegated_sessions()
    {
        var (_, fahad, acting) = await ActingAsync();
        await fahad.Post<JsonElement>("/api/auth/signout", new { refreshToken = fahad.Refresh, everywhere = true });
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => acting.Post<JsonElement>("/api/auth/refresh", new { refreshToken = acting.Refresh })));
    }

    [Fact]
    public async Task A_delegate_does_not_see_the_owners_identity_number_birth_date_or_address()
    {
        var (owner, _, acting) = await ActingAsync();
        var own = await owner.Get<JsonElement>("/api/auth/me");
        Assert.Equal("1023456781", own.GetProperty("nationalId").GetString());
        var seen = await acting.Get<JsonElement>("/api/auth/me");
        Assert.NotEqual("1023456781", seen.GetProperty("nationalId").GetString());
        Assert.True(seen.TryGetProperty("acting", out _));
        Assert.False(seen.GetProperty("profile").TryGetProperty("birthDate", out _));
        // back in his own account: his own details, no acting block
        var back = await acting.Post<JsonElement>("/api/auth/act-as/stop");
        Assert.Equal("1045678903", back.GetProperty("user").GetProperty("nationalId").GetString());
        Assert.False(back.GetProperty("user").TryGetProperty("acting", out _));
    }

    [Fact]
    public async Task A_user_manager_cannot_create_a_staff_member_stronger_than_themselves()
    {
        var admin = await Admin();
        var role = await admin.Post<JsonElement>("/api/admin/roles", new { name = "منشئ الحسابات", permissions = new[] { "users.view", "users.create" } });
        var roleId = role.GetProperty("id").GetString();
        await admin.Post<JsonElement>("/api/admin/users", new { fullName = "منشئ حسابات", phone = "0500000061", email = "c61@example.com", password = "Staff#2026x", role = "committee", staffRoleId = roleId });
        var manager = await app.SignInAsync("0500000061", "Staff#2026x", "staff");
        var system = (await admin.Get<JsonElement>("/api/admin/roles")).EnumerateArray().First(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetString();

        // the default committee role (no role chosen) and the system role both hold permissions the manager lacks
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => manager.Post<JsonElement>("/api/admin/users", new { fullName = "موظف أقوى", phone = "0500000062", email = "c62@example.com", password = "Staff#2026x", role = "committee" })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => manager.Post<JsonElement>("/api/admin/users", new { fullName = "موظف أقوى", phone = "0500000062", email = "c62@example.com", password = "Staff#2026x", role = "committee", staffRoleId = system })));
        // the platform password policy applies to staff passwords too
        Assert.Contains("أحرف على الأقل", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/users", new { fullName = "موظف جديد", phone = "0500000063", email = "c63@example.com", password = "abc", role = "committee" })));
        // within their own permissions it works
        var ok = await manager.Post<JsonElement>("/api/admin/users", new { fullName = "موظف مماثل", phone = "0500000062", email = "c62@example.com", password = "Staff#2026x", role = "committee", staffRoleId = roleId });
        Assert.Equal("committee", ok.GetProperty("role").GetString());
    }

    [Fact]
    public async Task The_user_list_hides_identity_numbers_and_contact_details_without_owners_full()
    {
        var committee = await app.CommitteeAsync();
        var users = (await committee.Get<JsonElement>("/api/admin/users")).EnumerateArray().ToList();
        var owner = Assert.Single(users, u => u.GetProperty("fullName").GetString() == "سعد بن ناصر العتيبي");
        Assert.NotEqual("1023456781", owner.GetProperty("nationalId").GetString());
        Assert.NotEqual("0500000001", owner.GetProperty("phone").GetString());
        Assert.NotEqual("owner@demo.sa", owner.GetProperty("email").GetString());
        Assert.False(owner.TryGetProperty("profile", out _));

        var admin = await Admin();
        var full = Assert.Single((await admin.Get<JsonElement>("/api/admin/users")).EnumerateArray(), u => u.GetProperty("fullName").GetString() == "سعد بن ناصر العتيبي");
        Assert.Equal("0500000001", full.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Resending_a_code_is_capped_per_hour()
    {
        var api = app.Anonymous();
        var ch = await api.Post<JsonElement>("/api/auth/signin", new { login = "0500000005", password = "123456", portal = "owner" });
        var challenge = ch.GetProperty("challenge").GetString();
        for (var i = 0; i < 4; i++)
            challenge = (await api.Post<JsonElement>("/api/auth/otp/resend", new { challenge })).GetProperty("challenge").GetString();
        var e = await Assert.ThrowsAsync<ApiException>(() => api.Post<JsonElement>("/api/auth/otp/resend", new { challenge }));
        Assert.Equal((HttpStatusCode)429, e.Status);
        // the refused resend did not burn the current code
        var ok = await api.Post<JsonElement>("/api/auth/signin/complete", new { challenge, code = "4321" });
        Assert.False(string.IsNullOrEmpty(ok.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task A_huge_email_is_rejected_at_once()
    {
        var owner = await app.SignInAsync("0500000003");
        var watch = Stopwatch.StartNew();
        var msg = await owner.Fails(() => owner.Post<JsonElement>("/api/me/email", new { email = "a@" + string.Concat(Enumerable.Repeat("b@", 15000)) }));
        Assert.Contains("البريد الإلكتروني غير صحيح", msg);
        msg = await owner.Fails(() => owner.Post<JsonElement>("/api/me/email", new { email = "a@" + new string('b', 30000) }));
        Assert.Contains("البريد الإلكتروني غير صحيح", msg);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        // ordinary addresses, subdomains included, still pass
        var ch = await owner.Post<JsonElement>("/api/me/email", new { email = "a.q@mail.example.com.sa" });
        Assert.False(string.IsNullOrEmpty(ch.GetProperty("challenge").GetString()));
    }

    [Fact]
    public async Task Event_links_must_be_web_addresses()
    {
        var admin = await Admin();
        var ev = new { id = "", title = "مهرجان تجريبي", titleEn = "", kind = "festival", startsOn = "2027-01-01", endsOn = "2027-01-05", approx = false, city = "الرياض", country = "SA", description = "", url = "javascript:alert(1)" };
        Assert.Contains("رابط", await admin.Fails(() => admin.Put<JsonElement>("/api/admin/events", ev)));
        Assert.Contains("رابط", await admin.Fails(() => admin.Put<JsonElement>("/api/admin/events", ev with { url = "https://example.com/" + new string('a', 600) })));
        var saved = await admin.Put<JsonElement>("/api/admin/events", ev with { url = "https://example.com/event" });
        Assert.Equal("https://example.com/event", saved.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        var res = await app.CreateClient().GetAsync("/health");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
    }
}
