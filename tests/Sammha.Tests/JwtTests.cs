using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Access tokens, refresh-token rotation and revocation.</summary>
public class JwtTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    [Fact]
    public async Task Sign_in_returns_a_short_access_token_and_a_refresh_token()
    {
        var api = await app.SignInAsync("0500000003");
        Assert.NotNull(api.Refresh);
        var parts = api.Token!.Split('.');
        Assert.Equal(3, parts.Length);
        var payload = JsonDocument.Parse(Convert.FromBase64String(Pad(parts[1]))).RootElement;
        Assert.True(payload.TryGetProperty("sub", out _));
        Assert.Equal("owner", payload.GetProperty("http://schemas.microsoft.com/ws/2008/06/identity/claims/role").GetString());
        Assert.True(payload.TryGetProperty("stamp", out _));
        var exp = DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64());
        Assert.InRange(exp - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(13), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public async Task Refresh_rotates_and_a_reused_token_ends_the_whole_session()
    {
        var api = await app.SignInAsync("0500000004");
        var first = api.Refresh;
        var r = await api.Post<JsonElement>("/api/auth/refresh", new { refreshToken = first });
        var second = r.GetProperty("refreshToken").GetString();
        Assert.NotEqual(first, second);
        api.Token = r.GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.OK, await StatusOf(() => api.Get<JsonElement>("/api/me/wallet")));

        // the rotated token is presented again (a stolen copy): rejected, and the family is revoked
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => api.Post<JsonElement>("/api/auth/refresh", new { refreshToken = first })));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => api.Post<JsonElement>("/api/auth/refresh", new { refreshToken = second })));
    }

    [Fact]
    public async Task Sign_out_revokes_the_refresh_token()
    {
        var api = await app.SignInAsync("0500000005");
        await api.Post<JsonElement>("/api/auth/signout", new { refreshToken = api.Refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => api.Post<JsonElement>("/api/auth/refresh", new { refreshToken = api.Refresh })));
    }

    [Fact]
    public async Task Password_change_signs_out_other_devices_immediately()
    {
        var phone = "0500000001";
        var laptop = await app.SignInAsync(phone);
        var mobile = await app.SignInAsync(phone);
        var r = await laptop.Post<JsonElement>("/api/auth/password", new { current = "123456", next = "NewPass#2026" });
        laptop.Token = r.GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.OK, await StatusOf(() => laptop.Get<JsonElement>("/api/me/wallet")));
        // the other device's access token carries the old stamp, and its refresh token is revoked
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => mobile.Get<JsonElement>("/api/me/wallet")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => mobile.Post<JsonElement>("/api/auth/refresh", new { refreshToken = mobile.Refresh })));
        await laptop.Post<JsonElement>("/api/auth/password", new { current = "NewPass#2026", next = "12345678" });
    }

    [Fact]
    public async Task Suspending_an_account_ends_its_sessions()
    {
        var owner = await app.SignInAsync("0500000003");
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var me = await owner.Get<JsonElement>("/api/auth/me");
        await admin.Put<JsonElement>($"/api/admin/users/{me.GetProperty("id").GetString()}/active", new { active = false });
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => owner.Get<JsonElement>("/api/me/wallet")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => owner.Post<JsonElement>("/api/auth/refresh", new { refreshToken = owner.Refresh })));
        await admin.Put<JsonElement>($"/api/admin/users/{me.GetProperty("id").GetString()}/active", new { active = true });
    }

    [Fact]
    public async Task Tampered_tokens_are_rejected()
    {
        var api = await app.SignInAsync("0500000004");
        var parts = api.Token!.Split('.');
        var payload = Convert.FromBase64String(Pad(parts[1]));
        var forged = System.Text.Encoding.UTF8.GetString(payload).Replace("\"owner\"", "\"admin\"");
        api.Token = $"{parts[0]}.{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(forged)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{parts[2]}";
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => api.Get<JsonElement>("/api/admin/overview")));
    }

    private static string Pad(string b64url)
    {
        var s = b64url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }
}
