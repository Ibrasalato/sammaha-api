using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Sammha.Api.Infrastructure;
using Sammha.Application.Engine;

namespace Sammha.Tests;

/// <summary>
/// The real API on a real SQL Server (connection from SAMMHA_TEST_DB; each fixture gets its own database),
/// with a controllable clock and the background sweeper turned off (tests run it explicitly).
/// </summary>
public class TestApp : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    private readonly string _db = "SammhaTest_" + Guid.NewGuid().ToString("N")[..8];

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        var server = Environment.GetEnvironmentVariable("SAMMHA_TEST_DB") ?? "Server=localhost,1433;User Id=sa;Password=Sammha_dev_2026!;TrustServerCertificate=True";
        b.UseEnvironment("Development");
        b.UseSetting("ConnectionStrings:Sammha", $"{server};Database={_db}");
        b.UseSetting("Seed:Demo", "true");
        b.UseSetting("RateLimit:AuthPerMinute", "100000");
        b.UseSetting("RateLimit:CheckPerMinute", "100000");
        b.UseSetting("RateLimit:RefreshPerMinute", "100000");
        b.UseSetting("Payments:Simulate", "true");
        b.UseSetting("Integrations:TicketWebhookUrl", "");
        b.UseSetting("Integrations:NotifyWebhookUrl", "");
        b.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            var sweeper = s.FirstOrDefault(d => d.ImplementationType == typeof(SweeperHost));
            if (sweeper != null) s.Remove(sweeper);
        });
    }

    public async Task SweepAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Sweeper>().RunAsync(CancellationToken.None);
    }

    public override async ValueTask DisposeAsync()
    {
        // drop the test database
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Sammha.Infrastructure.Data.SammhaDbContext>();
            await db.Database.EnsureDeletedAsync();
        }
        catch { /* best effort */ }
        await base.DisposeAsync();
    }

    public Api Anonymous() => new(CreateClient());

    public async Task<Api> SignInAsync(string login, string password = "123456", string portal = "owner")
    {
        var api = Anonymous();
        var ch = await api.Post<JsonElement>("/api/auth/signin", new { login, password, portal });
        var res = await api.Post<JsonElement>("/api/auth/signin/complete", new { challenge = ch.GetProperty("challenge").GetString(), code = "4321" });
        api.Token = res.GetProperty("token").GetString();
        api.Refresh = res.GetProperty("refreshToken").GetString();
        return api;
    }

    public Task<Api> OwnerAsync() => SignInAsync("0500000001");
    public Task<Api> CommitteeAsync() => SignInAsync("0500000002", portal: "staff");
    public Task<Api> BidderAsync(int n) => SignInAsync($"050000000{2 + n}"); // 1 → 0500000003 …
}

public class ApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

public class Api(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string? Token { get; set; }
    public string? Refresh { get; set; }

    private async Task<T> Send<T>(HttpMethod method, string url, object? body)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = JsonContent.Create(body);
        if (Token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var res = await http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            var msg = text;
            try { msg = JsonDocument.Parse(text).RootElement.GetProperty("message").GetString() ?? text; } catch { }
            throw new ApiException(res.StatusCode, msg);
        }
        if (string.IsNullOrEmpty(text)) return default!;
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    public Task<T> Get<T>(string url) => Send<T>(HttpMethod.Get, url, null);
    public Task<T> Post<T>(string url, object? body = null) => Send<T>(HttpMethod.Post, url, body ?? new { });
    public Task<T> Put<T>(string url, object body) => Send<T>(HttpMethod.Put, url, body);

    public async Task<string> Fails(Func<Task> call)
    {
        var e = await Assert.ThrowsAsync<ApiException>(call);
        return e.Message;
    }

    /// <summary>simulated card payment, credits the wallet</summary>
    public Task TopUp(decimal amount) => Post<JsonElement>("/api/me/wallet/topup", new { amount });
    public async Task<decimal> Balance() => (await Get<JsonElement>("/api/me/wallet")).GetProperty("balance").GetDecimal();
}
