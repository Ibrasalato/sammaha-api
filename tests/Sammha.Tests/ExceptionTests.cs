using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>«استثناء اسم من المزاد»: an accepted name is registered to its requester for a fee, without an auction.</summary>
public class ExceptionTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private static async Task<(string Request, string Candidate)> AcceptedAsync(Api owner, Api committee, string[] names)
    {
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names });
        var id = r.GetProperty("id").GetString()!;
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var c = (await committee.Get<JsonElement>($"/api/requests/{id}")).GetProperty("candidates").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = c.Select((x, i) => new { candidateId = x, status = i == 0 ? "accepted" : "rejected", reason = i == 0 ? null : "قُبل شعار آخر" }).ToArray() });
        return (id, c[0]);
    }

    private static async Task<decimal> Balance(Api a) => (await a.Get<JsonElement>("/api/me/wallet")).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task Committee_registers_a_name_without_auction_for_a_fee()
    {
        var committee = await app.CommitteeAsync();
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var owner = await app.SignInAsync("0500000003");
        var (req, cand) = await AcceptedAsync(owner, committee, ["الفارس الأول", "نجم السهيل", "برق الشمال"]);
        var depositPaid = (await owner.Get<JsonElement>($"/api/requests/{req}")).GetProperty("depositAmount").GetDecimal();

        // the built-in committee role does not have the permission; the admin does
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>($"/api/admin/candidates/{cand}/exception", new { fee = 3000, reason = "اسم تاريخي معروف لصاحبه" })));
        Assert.Contains("سبب", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/candidates/{cand}/exception", new { fee = 3000, reason = "" })));

        var before = await Balance(owner);
        var r = await admin.Post<JsonElement>($"/api/admin/candidates/{cand}/exception", new { fee = 3000, reason = "اسم تاريخي معروف لصاحبه" });
        var c = r.GetProperty("candidates").EnumerateArray().First(x => x.GetProperty("id").GetString() == cand);
        Assert.Equal(3000m, c.GetProperty("exceptionFee").GetDecimal());
        Assert.False(r.GetProperty("cancellable").GetBoolean());
        Assert.Equal("applied", r.GetProperty("depositState").GetString());
        Assert.Equal(before, await Balance(owner)); // the whole guarantee went towards the fee

        // registered at once, with an invoice for the fee less the guarantee
        var name = (await owner.Get<JsonElement>("/api/me/names")).EnumerateArray().Single(n => n.GetProperty("name").GetString() == "الفارس الأول");
        Assert.Equal("exception", name.GetProperty("source").GetString());
        var inv = (await owner.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("name").GetString() == "الفارس الأول");
        Assert.Equal(3000m, inv.GetProperty("total").GetDecimal());
        Assert.Equal(depositPaid, inv.GetProperty("depositApplied").GetDecimal());

        // it never reaches the auction schedule, cannot be excepted twice, and the request cannot be cancelled
        Assert.DoesNotContain((await admin.Get<JsonElement>("/api/admin/schedule")).EnumerateArray(), x => x.GetProperty("id").GetString() == cand);
        Assert.Contains("من قبل", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/candidates/{cand}/exception", new { fee = 3000, reason = "مرة ثانية للتجربة" })));
        Assert.Contains("استثناءً", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/requests/{req}/cancel", new { reason = "تجربة الإلغاء" })));
        Assert.Contains((await owner.Get<JsonElement>("/api/me/notifications")).EnumerateArray(), n => n.GetProperty("title").GetString()!.Contains("دون مزاد"));
        var log = (await admin.Get<JsonElement>("/api/admin/activity?q=استثناء")).GetProperty("items").EnumerateArray();
        Assert.Contains(log, a => a.GetProperty("action").GetString() == "استثناء اسم من المزاد");
    }

    [Fact]
    public async Task A_fee_below_the_guarantee_returns_the_rest_and_settles_the_invoice()
    {
        var committee = await app.CommitteeAsync();
        var admin = await app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
        var owner = await app.SignInAsync("0500000004");
        var (req, cand) = await AcceptedAsync(owner, committee, ["سيف البادية", "ريح الصبا", "غيث المطر"]);
        var deposit = (await owner.Get<JsonElement>($"/api/requests/{req}")).GetProperty("depositAmount").GetDecimal();
        var fee = Math.Max(0, deposit - 10);
        var before = await Balance(owner);
        await admin.Post<JsonElement>($"/api/admin/candidates/{cand}/exception", new { fee, reason = "قرار اللجنة رقم 12" });
        Assert.Equal(before + (deposit - fee), await Balance(owner));
        var inv = (await owner.Get<JsonElement>("/api/me/invoices")).EnumerateArray().First(i => i.GetProperty("name").GetString() == "سيف البادية");
        Assert.Equal("paid", inv.GetProperty("status").GetString());
    }
}
