using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Staff roles and permissions, ticket routing, and the administration's auction controls.</summary>
public class StaffTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");

    private async Task<(Api Api, string Id)> NewStaffAsync(Api admin, string phone, string email, string? roleId)
    {
        var u = await admin.Post<JsonElement>("/api/admin/users", new { fullName = $"موظف {phone[^3..]}", phone, email, password = "Staff#2026x", role = "committee", staffRoleId = roleId });
        return (await app.SignInAsync(phone, "Staff#2026x", "staff"), u.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Roles_decide_what_each_staff_member_can_do()
    {
        var admin = await Admin();
        var catalog = await admin.Get<JsonElement>("/api/admin/permissions");
        Assert.Contains(catalog.EnumerateArray(), p => p.GetProperty("key").GetString() == "auctions.manage" && p.GetProperty("label").GetString() == "التحكم في المزادات");
        Assert.Contains(catalog.EnumerateArray(), p => p.GetProperty("key").GetString() == "users.create" && p.GetProperty("label").GetString() == "إنشاء مستخدم");

        var role = await admin.Post<JsonElement>("/api/admin/roles", new { name = "الدعم الفني", description = "الرد على العملاء", permissions = new[] { "tickets.view", "tickets.reply", "bogus.permission" } });
        Assert.Equal(2, role.GetProperty("permissions").GetArrayLength());
        var roleId = role.GetProperty("id").GetString()!;

        var (support, supportId) = await NewStaffAsync(admin, "0500000071", "support71@example.com", roleId);
        var me = await support.Get<JsonElement>("/api/auth/me");
        Assert.Equal("الدعم الفني", me.GetProperty("staffRoleName").GetString());
        Assert.Contains("tickets.reply", me.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));

        await support.Get<JsonElement>("/api/admin/tickets");
        var denied = await support.Fails(() => support.Get<JsonElement>("/api/admin/requests"));
        Assert.Contains("عرض الطلبات", denied);

        // changing the role's permissions applies at once
        await admin.Put<JsonElement>($"/api/admin/roles/{roleId}", new { name = "الدعم الفني", permissions = new[] { "tickets.view", "tickets.reply", "requests.view" } });
        await support.Get<JsonElement>("/api/admin/requests");

        // the default committee keeps its old abilities but cannot control auctions or manage roles
        var committee = await app.CommitteeAsync();
        await committee.Get<JsonElement>("/api/admin/requests");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>("/api/admin/auctions/pause", new { scope = new { all = true } })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>("/api/admin/roles", new { name = "x y", permissions = new[] { "overview.view" } })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>("/api/admin/users", new { fullName = "حساب جديد", phone = "0500000072", email = "n72@example.com", password = "Staff#2026x", role = "committee" })));

        // a role in use or the built-in role cannot be deleted; an unused role can
        Assert.Contains("انقل الموظفين", await admin.Fails(() => admin.Send<JsonElement>(HttpMethod.Delete, $"/api/admin/roles/{roleId}", null)));
        var roles = await admin.Get<JsonElement>("/api/admin/roles");
        var system = roles.EnumerateArray().First(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetString();
        Assert.Contains("لا يُحذف", await admin.Fails(() => admin.Send<JsonElement>(HttpMethod.Delete, $"/api/admin/roles/{system}", null)));
        await admin.Put<JsonElement>($"/api/admin/users/{supportId}/staff-role", new { staffRoleId = system });
        await admin.Send<JsonElement>(HttpMethod.Delete, $"/api/admin/roles/{roleId}", null);
    }

    [Fact]
    public async Task A_user_manager_cannot_create_admins_or_grant_more_than_they_have()
    {
        var admin = await Admin();
        var role = await admin.Post<JsonElement>("/api/admin/roles", new { name = "مسؤول الحسابات", permissions = new[] { "users.view", "users.create", "users.roles" } });
        var (manager, _) = await NewStaffAsync(admin, "0500000073", "m73@example.com", role.GetProperty("id").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => manager.Post<JsonElement>("/api/admin/users", new { fullName = "مدير جديد", phone = "0500000074", email = "a74@example.com", password = "Staff#2026x", role = "admin" })));
        var u = await manager.Post<JsonElement>("/api/admin/users", new { fullName = "موظف جديد", phone = "0500000075", email = "s75@example.com", password = "Staff#2026x", role = "committee" });
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => manager.Put<JsonElement>($"/api/admin/users/{u.GetProperty("id").GetString()}/role", new { role = "admin" })));
        // the system committee role includes permissions this manager does not hold
        var system = (await admin.Get<JsonElement>("/api/admin/roles")).EnumerateArray().First(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => manager.Put<JsonElement>($"/api/admin/users/{u.GetProperty("id").GetString()}/staff-role", new { staffRoleId = system })));
    }

    [Fact]
    public async Task Tickets_are_routed_to_their_handlers_and_emailed_to_their_inbox()
    {
        var admin = await Admin();
        var role = await admin.Post<JsonElement>("/api/admin/roles", new { name = "فريق الشكاوى", permissions = new[] { "tickets.view", "tickets.reply", "tickets.manage" } });
        var (handler, handlerId) = await NewStaffAsync(admin, "0500000076", "h76@example.com", role.GetProperty("id").GetString());

        var bad = await admin.Fails(() => admin.Put<JsonElement>("/api/admin/ticket-routing", new { restrictToHandlers = true, handlers = new Dictionary<string, string[]>(), emails = new Dictionary<string, string> { ["complaint"] = "not-an-email" } }));
        Assert.Contains("البريد غير صحيح", bad);
        var routing = await admin.Put<JsonElement>("/api/admin/ticket-routing", new
        {
            restrictToHandlers = true,
            handlers = new Dictionary<string, string[]> { ["complaint"] = [handlerId] },
            emails = new Dictionary<string, string> { ["complaint"] = "complaints@ico.org.sa, quality@ico.org.sa", ["report"] = "security@ico.org.sa" },
        });
        Assert.Equal("complaints@ico.org.sa, quality@ico.org.sa", routing.GetProperty("emails").GetProperty("complaint").GetString());
        Assert.Contains(handlerId, routing.GetProperty("handlers").GetProperty("complaint").EnumerateArray().Select(x => x.GetString()));

        var ticket = await app.Anonymous().Post<JsonElement>("/api/tickets", new { kind = "complaint", topic = "مزاد", subject = "تأخر رد الضمان", name = "مالك", phone = "0551234567", message = "لم يصلني رد مبلغ الضمان بعد انتهاء المزاد." });
        var all = await admin.Get<JsonElement>("/api/admin/tickets");
        var t = all.EnumerateArray().First(x => x.GetProperty("number").GetString() == ticket.GetProperty("number").GetString());
        Assert.Equal(handlerId, t.GetProperty("assigneeId").GetString());
        var tid = t.GetProperty("id").GetString();
        var notes = await handler.Get<JsonElement>("/api/me/notifications");
        Assert.Contains(notes.EnumerateArray(), n => n.GetProperty("title").GetString()!.Contains("مُسند إليك"));

        // only the complaint team (and admins) may reply while the restriction is on
        var committee = await app.CommitteeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>($"/api/admin/tickets/{tid}/reply", new { body = "نراجع طلبك", @internal = false })));
        await handler.Post<JsonElement>($"/api/admin/tickets/{tid}/reply", new { body = "نراجع طلبك الآن", @internal = false });
        await admin.Put<JsonElement>("/api/admin/ticket-routing", new { restrictToHandlers = false, handlers = new Dictionary<string, string[]>() });
    }

    private async Task<string> LiveAuctionAsync(params string[] names)
    {
        var owner = await app.OwnerAsync();
        var committee = await app.CommitteeAsync();
        await owner.TopUp(100);
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "bakkar", names });
        var id = r.GetProperty("id").GetString();
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");
        var full = await committee.Get<JsonElement>($"/api/requests/{id}");
        var cands = full.GetProperty("candidates").EnumerateArray().Select((c, i) => new { candidateId = c.GetProperty("id").GetString(), status = i == 0 ? "accepted" : "rejected", reason = i == 0 ? null : "قُبل شعار آخر" }).ToArray();
        await committee.Post<JsonElement>($"/api/admin/requests/{id}/review", new { decisions = cands });
        var now = app.Clock.GetUtcNow();
        var a = (await committee.Post<JsonElement>("/api/admin/schedule", new { candidateIds = new[] { cands[0].candidateId }, startsAt = now.AddMinutes(-1), endsAt = now.AddHours(2) }))[0];
        return a.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Pause_resume_extend_and_cancel()
    {
        var admin = await Admin();
        var aid = await LiveAuctionAsync("غيم الشمال", "برق الجنوب", "نسمة الصبح");
        var bidder = await app.BidderAsync(1);
        await bidder.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 100 });
        var endsAt = (await admin.Get<JsonElement>($"/api/auctions/{aid}")).GetProperty("auction").GetProperty("endsAt").GetDateTimeOffset();

        var res = await admin.Post<JsonElement>("/api/admin/auctions/pause", new { scope = new { ids = new[] { aid } }, reason = "مراجعة شكوى على المزاد" });
        Assert.Equal(1, res.GetProperty("affected").GetInt32());
        var view = await bidder.Get<JsonElement>($"/api/auctions/{aid}");
        Assert.True(view.GetProperty("auction").GetProperty("paused").GetBoolean());
        Assert.Contains("متوقف مؤقتًا", await bidder.Fails(() => bidder.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 200 })));

        // a paused auction is not settled even after its end time
        app.Clock.Advance(TimeSpan.FromHours(3));
        await app.SweepAsync();
        Assert.False((await admin.Get<JsonElement>($"/api/auctions/{aid}")).GetProperty("auction").GetProperty("settled").GetBoolean());

        // resuming gives the paused time back
        res = await admin.Post<JsonElement>("/api/admin/auctions/resume", new { scope = new { ids = new[] { aid } } });
        Assert.Equal(1, res.GetProperty("affected").GetInt32());
        var resumed = (await admin.Get<JsonElement>($"/api/auctions/{aid}")).GetProperty("auction");
        Assert.InRange((resumed.GetProperty("endsAt").GetDateTimeOffset() - endsAt).TotalMinutes, 179, 181);
        var bidder2 = await app.BidderAsync(2);
        await bidder2.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 200 });

        res = await admin.Post<JsonElement>("/api/admin/auctions/extend", new { scope = new { ids = new[] { aid } }, minutes = 60 });
        Assert.Equal(1, res.GetProperty("affected").GetInt32());
        Assert.InRange((await admin.Get<JsonElement>($"/api/auctions/{aid}")).GetProperty("auction").GetProperty("endsAt").GetDateTimeOffset() - resumed.GetProperty("endsAt").GetDateTimeOffset(), TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));

        Assert.Contains("سبب الإلغاء", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/auctions/cancel", new { scope = new { ids = new[] { aid } }, reason = " " })));
        res = await admin.Post<JsonElement>("/api/admin/auctions/cancel", new { scope = new { ids = new[] { aid } }, reason = "خطأ في بيانات الاسم" });
        Assert.Equal(1, res.GetProperty("affected").GetInt32());
        var cancelled = (await admin.Get<JsonElement>($"/api/auctions/{aid}")).GetProperty("auction");
        Assert.True(cancelled.GetProperty("cancelled").GetBoolean());
        Assert.Equal("خطأ في بيانات الاسم", cancelled.GetProperty("cancelReason").GetString());
        Assert.Contains("أُلغي", await bidder.Fails(() => bidder.Post<JsonElement>($"/api/auctions/{aid}/bids", new { amount = 300 })));
        // the name goes back to the committee's scheduling list, and leaves the public list
        var awaiting = await admin.Get<JsonElement>("/api/admin/schedule");
        Assert.Contains(awaiting.EnumerateArray(), c => c.GetProperty("name").GetString() == "غيم الشمال");
        var pub = await app.Anonymous().Get<JsonElement>("/api/auctions");
        Assert.DoesNotContain(pub.EnumerateArray(), x => x.GetProperty("id").GetString() == aid);
        var notes = await bidder.Get<JsonElement>("/api/me/notifications");
        Assert.Contains(notes.EnumerateArray(), n => n.GetProperty("title").GetString() == "أُلغي مزاد «غيم الشمال»");

        // the change history records every action
        var history = await admin.Get<JsonElement>("/api/admin/settings/history");
        foreach (var f in new[] { "AuctionPause", "AuctionResume", "AuctionExtend", "AuctionCancel" })
            Assert.Contains(history.EnumerateArray(), h => h.GetProperty("field").GetString() == f);
    }

    [Fact]
    public async Task Pause_all_and_resume_all()
    {
        var admin = await Admin();
        await LiveAuctionAsync("رعد الوادي", "شهب الليل", "زهر البر");
        var res = await admin.Post<JsonElement>("/api/admin/auctions/pause", new { scope = new { all = true }, reason = "صيانة" });
        Assert.True(res.GetProperty("affected").GetInt32() >= 1);
        var paused = await admin.Get<JsonElement>("/api/admin/auctions?state=paused");
        Assert.Equal(res.GetProperty("affected").GetInt32(), paused.GetArrayLength());
        // a second pause skips them with a reason
        var again = await admin.Post<JsonElement>("/api/admin/auctions/pause", new { scope = new { all = true } });
        Assert.Equal(0, again.GetProperty("affected").GetInt32());
        Assert.Contains(again.GetProperty("skipped").EnumerateArray(), s => s.GetProperty("reason").GetString() == "متوقف مسبقًا");
        res = await admin.Post<JsonElement>("/api/admin/auctions/resume", new { scope = new { all = true } });
        Assert.Equal(paused.GetArrayLength(), res.GetProperty("affected").GetInt32());
        Assert.Equal(0, (await admin.Get<JsonElement>("/api/admin/auctions?state=paused")).GetArrayLength());
    }
}
