using System.Net;

namespace Sammha.Tests;

/// <summary>The mobile app's WebView calls the API from https://localhost (Android) or capacitor://localhost (iOS).</summary>
public class AppCorsTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData("https://localhost")]
    [InlineData("capacitor://localhost")]
    public async Task App_origins_may_call_the_api(string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/auth/signin");
        req.Headers.Add("Origin", origin);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        var res = await app.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(origin, res.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task A_stranger_origin_gets_no_cors_header()
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/auth/signin");
        req.Headers.Add("Origin", "https://evil.example");
        req.Headers.Add("Access-Control-Request-Method", "POST");
        var res = await app.CreateClient().SendAsync(req);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
