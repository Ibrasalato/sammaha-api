using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sammha.Domain;
using Sammha.Infrastructure.Data;

namespace Sammha.Tests;

/// <summary>«ربط بشريحة»: an owner puts one of their قعدان or بكار names on one of their camels from the registry (KACF).</summary>
public class CamelLinkTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<JsonElement> Name(Api a, string name) =>
        (await a.Get<JsonElement>("/api/me/names")).EnumerateArray().FirstOrDefault(n => n.GetProperty("name").GetString() == name);

    private static async Task<JsonElement> Camel(Api a, Guid id) =>
        (await a.Get<JsonElement>("/api/me/camels")).GetProperty("camels").EnumerateArray().First(c => c.GetProperty("id").GetGuid() == id);

    /// <summary>camels arrive from the registry integration: written straight to the database here</summary>
    private async Task<Dictionary<string, Guid>> SeedCamelsAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SammhaDbContext>();
        var b1 = await db.Users.SingleAsync(u => u.Phone == "0500000003");
        var b2 = await db.Users.SingleAsync(u => u.Phone == "0500000004");
        var now = DateTimeOffset.UtcNow;
        OwnedCamel Mk(Guid user, string chip, string? sex) => new() { UserId = user, ChipNumbers = chip, Sex = sex, Color = "مجهم", Age = "حق", CreatedAt = now, SyncedAt = now };
        var ids = new Dictionary<string, Guid>();
        foreach (var (key, user, chip, sex) in new[]
        {
            ("m1", b1.Id, "9821000000001", (string?)"male"), ("m2", b1.Id, "9821000000002,9821000000012", "male"), ("f1", b1.Id, "9821000000003", "female"),
            ("unknown", b1.Id, "9821000000004", (string?)null), ("other", b2.Id, "9821000000005", "male"),
        })
        {
            var c = Mk(user, chip, sex);
            db.OwnedCamels.Add(c);
            ids[key] = c.Id;
        }
        await db.SaveChangesAsync();
        return ids;
    }

    [Fact]
    public async Task Owner_links_and_unlinks_a_name_and_a_transfer_clears_the_link()
    {
        var camels = await SeedCamelsAsync();
        var owner = await app.BidderAsync(1); // 0500000003: قعدان الفاخر وشامخ، بكار الغيمة، منقية الوسمية
        var fakher = (await Name(owner, "الفاخر")).GetProperty("id").GetGuid();
        var shamikh = (await Name(owner, "شامخ")).GetProperty("id").GetGuid();
        var ghaima = (await Name(owner, "الغيمة")).GetProperty("id").GetGuid();
        var manqiya = (await Name(owner, "الوسمية")).GetProperty("id").GetGuid();
        string Link(string key) => $"/api/me/camels/{camels[key]}/link";

        // قعود on a female, منقية on any chip, someone else's camel: refused
        Assert.Contains("القعدان", await owner.Fails(() => owner.Post<JsonElement>(Link("f1"), new { reservedId = fakher })));
        Assert.Contains("البكار", await owner.Fails(() => owner.Post<JsonElement>(Link("m1"), new { reservedId = ghaima })));
        Assert.Contains("المنقية", await owner.Fails(() => owner.Post<JsonElement>(Link("m1"), new { reservedId = manqiya })));
        var e = await Assert.ThrowsAsync<ApiException>(() => owner.Post<JsonElement>(Link("other"), new { reservedId = fakher }));
        Assert.Equal(HttpStatusCode.NotFound, e.Status);
        // a name the caller does not own
        var someoneElses = (await Name(await app.BidderAsync(2), "سلطان")).GetProperty("id").GetGuid();
        Assert.Contains("مسجل باسمك", await owner.Fails(() => owner.Post<JsonElement>(Link("m1"), new { reservedId = someoneElses })));

        var linked = await owner.Post<JsonElement>(Link("m1"), new { reservedId = fakher });
        Assert.Equal(fakher, linked.GetProperty("reservedId").GetGuid());
        Assert.Equal("الفاخر", linked.GetProperty("reservedName").GetString());
        Assert.Equal("qaadan", linked.GetProperty("reservedType").GetString());
        Assert.True(linked.TryGetProperty("linkedAt", out _));

        // one name per chip and one chip per name
        Assert.Contains("بشريحة أخرى", await owner.Fails(() => owner.Post<JsonElement>(Link("m2"), new { reservedId = fakher })));
        Assert.Contains("باسم آخر", await owner.Fails(() => owner.Post<JsonElement>(Link("m1"), new { reservedId = shamikh })));

        // my names show the chip; the camels page shows the name
        var n = await Name(owner, "الفاخر");
        Assert.Equal("9821000000001", n.GetProperty("camelChip").GetString());
        Assert.Equal(camels["m1"], n.GetProperty("camelId").GetGuid());
        Assert.Equal("الفاخر", (await Camel(owner, camels["m1"])).GetProperty("reservedName").GetString());
        // the public registry never carries the chip
        var pub = (await app.Anonymous().Get<JsonElement>("/api/names?q=الفاخر")).EnumerateArray().First(x => x.GetProperty("id").GetGuid() == fakher);
        Assert.False(pub.TryGetProperty("camelChip", out _));

        // unlink, then a bakkar name on a female and a قعود on a camel of unknown sex
        var free = await owner.Send<JsonElement>(HttpMethod.Delete, Link("m1"), null);
        Assert.False(free.TryGetProperty("reservedId", out _));
        Assert.False((await Name(owner, "الفاخر")).TryGetProperty("camelChip", out _));
        await owner.Post<JsonElement>(Link("f1"), new { reservedId = ghaima });
        await owner.Post<JsonElement>(Link("unknown"), new { reservedId = fakher });

        // the name changes hands: the link goes with the old owner's camel
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        await admin.Patch<JsonElement>("/api/admin/settings", new { transferFee = 0 });
        var t = await owner.Post<JsonElement>("/api/me/transfers", new { reservedId = fakher, phone = "0500000004", reason = "gift" });
        var to = await app.BidderAsync(2);
        await to.Post<JsonElement>($"/api/me/transfers/{t.GetProperty("id").GetGuid()}/respond", new { accept = true });
        Assert.Equal("completed", (await admin.Post<JsonElement>($"/api/admin/transfers/{t.GetProperty("id").GetGuid()}/review", new { approve = true })).GetProperty("status").GetString());
        Assert.False((await Camel(owner, camels["unknown"])).TryGetProperty("reservedId", out _));
        Assert.Equal("الغيمة", (await Camel(owner, camels["f1"])).GetProperty("reservedName").GetString());
        Assert.False((await Name(to, "الفاخر")).TryGetProperty("camelChip", out _));
    }

    [Fact]
    public async Task Linking_is_for_the_signed_in_owner()
    {
        var e = await Assert.ThrowsAsync<ApiException>(() => app.Anonymous().Post<JsonElement>($"/api/me/camels/{Guid.NewGuid()}/link", new { reservedId = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.Unauthorized, e.Status);
        var owner = await app.OwnerAsync();
        var missing = await Assert.ThrowsAsync<ApiException>(() => owner.Post<JsonElement>($"/api/me/camels/{Guid.NewGuid()}/link", new { reservedId = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }
}
