using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

public class LogoTests(TestApp app) : IClassFixture<TestApp>
{
    // a 1×1 PNG
    private static readonly string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Owner_uploads_and_committee_approves_a_logo()
    {
        var owner = await app.OwnerAsync();
        var name = (await owner.Get<JsonElement>("/api/me/names")).EnumerateArray().First(n => n.GetProperty("name").GetString() == "الشرهات");
        var id = name.GetProperty("id").GetString();
        var svg = Convert.ToBase64String("<svg onload=alert(1)></svg>"u8.ToArray());
        Assert.Contains("PNG", await owner.Fails(() => owner.Put<JsonElement>($"/api/me/names/{id}/logo", new { fileName = "x.svg", base64 = svg })));
        Assert.Contains("مسجل باسمك", await owner.Fails(() => owner.Put<JsonElement>($"/api/me/names/{Guid.NewGuid()}/logo", new { fileName = "l.png", base64 = Png })));

        var r = await owner.Put<JsonElement>($"/api/me/names/{id}/logo", new { fileName = "logo.png", base64 = Png });
        Assert.Equal("pending", r.GetProperty("logoStatus").GetString());
        var web = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync($"/api/names/{id}/logo")).StatusCode);

        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var committee = await app.CommitteeAsync();
        await committee.Fails(() => committee.Get<JsonElement>("/api/admin/logos"));
        var pending = (await admin.Get<JsonElement>("/api/admin/logos?status=pending")).EnumerateArray().Single(l => l.GetProperty("reservedId").GetString() == id);
        var lid = pending.GetProperty("id").GetString();
        Assert.Contains("سبب", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/logos/{lid}/review", new { approve = false })));
        Assert.Equal("approved", (await admin.Post<JsonElement>($"/api/admin/logos/{lid}/review", new { approve = true })).GetProperty("status").GetString());

        var img = await web.GetAsync($"/api/names/{id}/logo");
        Assert.Equal(HttpStatusCode.OK, img.StatusCode);
        Assert.Equal("image/png", img.Content.Headers.ContentType?.MediaType);
        var listed = (await owner.Get<JsonElement>("/api/me/names")).EnumerateArray().First(n => n.GetProperty("id").GetString() == id);
        Assert.StartsWith($"/api/names/{id}/logo?v=", listed.GetProperty("logo").GetString());

        // a replacement waits for approval again and the old one stops showing
        await owner.Put<JsonElement>($"/api/me/names/{id}/logo", new { fileName = "logo2.png", base64 = Png });
        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync($"/api/names/{id}/logo")).StatusCode);
        await owner.Send<JsonElement>(HttpMethod.Delete, $"/api/me/names/{id}/logo", new { });
        Assert.DoesNotContain((await admin.Get<JsonElement>("/api/admin/logos")).EnumerateArray(), l => l.GetProperty("reservedId").GetString() == id);
    }
}
