using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>«إبلي»: the owner's camels from the camel registry platform (not linked yet, so empty).</summary>
public class CamelsTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Owner_sees_an_empty_list_until_the_registry_is_linked()
    {
        var owner = await app.OwnerAsync();
        var res = await owner.Get<JsonElement>("/api/me/camels");
        Assert.Empty(res.GetProperty("camels").EnumerateArray());
        Assert.False(res.GetProperty("connected").GetBoolean());
        // null last-sync is left out of the JSON
        Assert.False(res.TryGetProperty("lastSyncedAt", out _));
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        var e = await Assert.ThrowsAsync<ApiException>(() => app.Anonymous().Get<JsonElement>("/api/me/camels"));
        Assert.Equal(HttpStatusCode.Unauthorized, e.Status);
    }
}
