using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

public class SwaggerTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task OpenApi_document_lists_the_endpoints_and_the_bearer_scheme()
    {
        var res = await app.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var paths = doc.GetProperty("paths");
        foreach (var p in new[] { "/api/auth/signin", "/api/auth/refresh", "/api/auctions", "/api/admin/overview" })
            Assert.True(paths.TryGetProperty(p, out _), $"missing {p}");
        Assert.Equal("bearer", doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Swagger_ui_is_served()
    {
        var res = await app.CreateClient().GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("swagger-ui", await res.Content.ReadAsStringAsync());
    }
}
