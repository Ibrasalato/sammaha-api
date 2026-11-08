using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>The staff WhatsApp assistant: a secret key, only registered staff numbers, read-only answers limited to the staff member's permissions.</summary>
public class AssistantTests(TestApp app) : IClassFixture<TestApp>
{
    private const string Key = "test-assistant-key-0123456789-abcdefghij";

    private async Task<(HttpStatusCode Status, JsonElement Body)> AskAsync(object body, string? key = Key)
    {
        var http = app.CreateClient();
        if (key != null) http.DefaultRequestHeaders.Add("X-Assistant-Key", key);
        var res = await http.PostAsJsonAsync("/api/assistant/ask", body);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, text.Length > 0 && res.IsSuccessStatusCode ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    [Fact]
    public async Task Without_the_key_nothing_is_answered()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await AskAsync(new { op = "identify", phone = "966500000002" }, null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AskAsync(new { op = "identify", phone = "966500000002" }, "wrong-key-wrong-key-wrong-key-wrong")).Status);
    }

    [Fact]
    public async Task Only_a_staff_number_is_recognised()
    {
        var owner = await AskAsync(new { op = "identify", phone = "966500000001" });
        Assert.False(owner.Body.GetProperty("ok").GetBoolean());
        var unknown = await AskAsync(new { op = "identify", phone = "966512345678" });
        Assert.False(unknown.Body.GetProperty("ok").GetBoolean());

        var staff = await AskAsync(new { op = "identify", phone = "+966 50 000 0002" });
        Assert.True(staff.Body.GetProperty("ok").GetBoolean());
        Assert.True(staff.Body.GetProperty("data").GetProperty("permissions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task A_staff_member_gets_summaries_and_lookups_but_a_stranger_gets_nothing()
    {
        var id = (await AskAsync(new { op = "identify", phone = "966500000002" })).Body.GetProperty("data").GetProperty("staffId").GetGuid();

        var summary = await AskAsync(new { op = "summary", staffId = id });
        Assert.True(summary.Body.GetProperty("ok").GetBoolean());
        Assert.True(summary.Body.GetProperty("data").TryGetProperty("liveAuctions", out _));

        var tickets = await AskAsync(new { op = "tickets", staffId = id, state = "open" });
        Assert.True(tickets.Body.GetProperty("ok").GetBoolean());
        var auctions = await AskAsync(new { op = "auctions", staffId = id, state = "live" });
        Assert.True(auctions.Body.GetProperty("ok").GetBoolean());
        var name = await AskAsync(new { op = "name", staffId = id, q = "مدعث" });
        Assert.True(name.Body.GetProperty("ok").GetBoolean());

        // a made-up staff id, or an owner's id, is refused
        Assert.False((await AskAsync(new { op = "summary", staffId = Guid.NewGuid() })).Body.GetProperty("ok").GetBoolean());
        Assert.False((await AskAsync(new { op = "nope", staffId = id })).Body.GetProperty("ok").GetBoolean());
    }
}
