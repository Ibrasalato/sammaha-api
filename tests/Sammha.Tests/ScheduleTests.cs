using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Opening auctions, with marks released one after another.</summary>
public class ScheduleTests(TestApp app) : IClassFixture<TestApp>
{
    private async Task<string> AcceptedAsync(Api owner, Api committee, string type, string[] names)
    {
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type, names });
        var id = r.GetProperty("id").GetString();
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var c = (await committee.Get<JsonElement>($"/api/requests/{id}")).GetProperty("candidates").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = c.Select((x, i) => new { candidateId = x, status = i == 0 ? "accepted" : "rejected", reason = i == 0 ? null : "قُبل شعار آخر" }).ToArray() });
        return c[0];
    }

    [Fact]
    public async Task Staggered_release_spaces_the_marks()
    {
        var committee = await app.CommitteeAsync();
        var a = await AcceptedAsync(await app.SignInAsync("0500000003"), committee, "bakkar", ["ريم الصحراء", "ضوء القمر", "سنا البرق"]);
        var b = await AcceptedAsync(await app.SignInAsync("0500000004"), committee, "bakkar", ["مها الوادي", "نسمة الفجر", "درة الشمال"]);
        var c = await AcceptedAsync(await app.SignInAsync("0500000005"), committee, "bakkar", ["غيمة الصيف", "لؤلؤة البحر", "ورد الربيع"]);
        var start = app.Clock.GetUtcNow().AddHours(1);
        var end = start.AddHours(2);
        Assert.Contains("1440", await committee.Fails(() => committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { a }, startsAt = start, endsAt = end, staggerMinutes = 5000 })));

        // released in the order chosen: c, a, b — 15 minutes apart, each closing 15 minutes after the previous
        var list = (await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { c, a, b }, startsAt = start, endsAt = end, staggerMinutes = 15 })).EnumerateArray().ToList();
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { c, a, b }, list.Select(x => x.GetProperty("candidateId").GetString()).ToArray());
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(start.AddMinutes(15 * i), list[i].GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(end.AddMinutes(15 * i), list[i].GetProperty("endsAt").GetDateTimeOffset());
        }
    }
}
