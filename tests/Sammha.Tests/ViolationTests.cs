using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>«المخالفات»: types in the settings, issued by staff with violations.manage, paid by the owner through the card only.</summary>
public class ViolationTests(TestApp app) : IClassFixture<TestApp>
{
    // a 1×1 PNG
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static async Task<HttpStatusCode> StatusOf(Func<Task> call)
    {
        try { await call(); return HttpStatusCode.OK; }
        catch (ApiException e) { return e.Status; }
    }

    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");

    private async Task<Api> StaffAsync(Api admin, string phone, params string[] permissions)
    {
        var role = await admin.Post<JsonElement>("/api/admin/roles", new { name = $"مخالفات {phone[^3..]}", permissions });
        await admin.Post<JsonElement>("/api/admin/users", new { fullName = $"مراقب {phone[^3..]}", phone, email = $"v{phone[^3..]}@example.com", password = "Staff#2026x", role = "committee", staffRoleId = role.GetProperty("id").GetString() });
        return await app.SignInAsync(phone, "Staff#2026x", "staff");
    }

    [Fact]
    public async Task Staff_issue_a_fine_and_the_owner_pays_it_by_card()
    {
        var admin = await Admin();
        Assert.Contains("الغرامة", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/violation-types", new { name = "مخالفة سالبة", amount = -5 })));
        var fine = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "تأخير سداد قيمة شعار", nameEn = "Late mark payment", amount = 500, active = true });
        var warning = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "تنبيه استخدام شعار", amount = 0, active = true });
        Assert.Contains("بهذا الاسم", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/violation-types", new { name = "تأخير سداد قيمة شعار", amount = 100 })));
        var fineId = fine.GetProperty("id").GetString();

        var officer = await StaffAsync(admin, "0500000081", "violations.manage", "violations.view");
        var who = await officer.Post<JsonElement>("/api/admin/violations/owner", new { q = "0500000001" });
        Assert.Equal("سعد بن ناصر العتيبي", who.GetProperty("fullName").GetString());
        Assert.Contains("•", who.GetProperty("phone").GetString()); // masked without owners.full
        Assert.Contains("10", await officer.Fails(() => officer.Post<JsonElement>("/api/admin/violations", new { owner = "0500000001", typeId = fineId, description = "قصير" })));
        var v = await officer.Post<JsonElement>("/api/admin/violations", new
        {
            owner = "0500000001", typeId = fineId, description = "لم يُسدَّد طلب السداد خلال المهلة المحددة",
            attachment = new { fileName = "evidence.png", contentType = "image/png", dataBase64 = Png },
        });
        Assert.Equal("unpaid", v.GetProperty("status").GetString());
        Assert.Equal(500, v.GetProperty("amount").GetDecimal());
        Assert.StartsWith($"VL-{DateTime.UtcNow.Year}-", v.GetProperty("number").GetString());
        Assert.Equal("image/png", v.GetProperty("attachmentType").GetString());
        var id = v.GetProperty("id").GetString();
        var svg = Convert.ToBase64String("<svg onload=alert(1)></svg>"u8.ToArray());
        Assert.Contains("PDF", await officer.Fails(() => officer.Post<JsonElement>("/api/admin/violations", new { owner = "0500000001", typeId = fineId, description = "مرفق بصيغة غير مقبولة للتجربة", attachment = new { fileName = "x.svg", dataBase64 = svg } })));
        var penalty = await officer.Post<JsonElement>("/api/admin/violations", new { owner = "1023456781", typeId = warning.GetProperty("id").GetString(), description = "استخدام الشعار قبل اعتماده من اللجنة" });
        Assert.Equal("recorded", penalty.GetProperty("status").GetString());

        // the owner sees both, with the attachment, and is notified
        var owner = await app.OwnerAsync();
        var mine = await owner.Get<JsonElement>("/api/me/violations");
        Assert.Equal(2, mine.GetArrayLength());
        Assert.False(mine[0].TryGetProperty("issuedByName", out _));
        Assert.Contains((await owner.Get<JsonElement>("/api/me/notifications")).EnumerateArray(), n => n.GetProperty("kind").GetString() == "violation");
        Assert.Equal(Png, (await owner.Get<JsonElement>($"/api/me/violations/{id}/attachment")).GetProperty("base64").GetString());

        // the wallet cannot pay a fine: card only
        await owner.TopUp(600);
        var before = await owner.Balance();
        Assert.Contains("قنوات الدفع", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/violations/{id}/pay")));
        Assert.Contains("غير متاحة", await owner.Fails(() => owner.Post<JsonElement>("/api/payments/card", new { purpose = "violation", @ref = penalty.GetProperty("id").GetString(), amount = 0, callback = "https://sammha.test/cb" })));
        var paid = await owner.PayByCard("violation", id, $"/api/me/violations/{id}/pay");
        Assert.Equal("paid", paid.GetProperty("status").GetString());
        Assert.True(paid.TryGetProperty("paidAt", out _));
        Assert.True(paid.TryGetProperty("paymentId", out _));
        Assert.Equal(before, await owner.Balance()); // in by card, straight out to the fine
        // no tax invoice for a fine
        Assert.DoesNotContain((await owner.Get<JsonElement>("/api/me/invoices")).EnumerateArray(), i => i.GetProperty("total").GetDecimal() == 500);
        Assert.Contains((await owner.Get<JsonElement>("/api/me/wallet")).GetProperty("txs").EnumerateArray(), t => t.GetProperty("kind").GetString() == "fine" && t.GetProperty("amount").GetDecimal() == -500);

        // staff filters and search
        var all = await officer.Get<JsonElement>("/api/admin/violations?status=all&q=0500000001");
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal(1, all.GetProperty("counts").GetProperty("paid").GetInt32());
        Assert.Equal(1, all.GetProperty("counts").GetProperty("recorded").GetInt32());
        Assert.Equal(id, (await officer.Get<JsonElement>("/api/admin/violations?status=paid&q=0500000001")).GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(0, (await officer.Get<JsonElement>("/api/admin/violations?status=unpaid&q=0500000001")).GetProperty("total").GetInt32());
        Assert.Equal(2, (await officer.Get<JsonElement>($"/api/admin/violations?q={Uri.EscapeDataString("العتيبي")}")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await officer.Get<JsonElement>($"/api/admin/violations?q={v.GetProperty("number").GetString()}")).GetProperty("total").GetInt32());
        Assert.Equal(2, (await officer.Get<JsonElement>("/api/admin/violations?q=0500000001")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await officer.Get<JsonElement>("/api/admin/violations?q=0500000005")).GetProperty("total").GetInt32());
        Assert.Equal("image/png", (await officer.Get<JsonElement>($"/api/admin/violations/{id}/attachment")).GetProperty("contentType").GetString());

        // a paid fine is not cancelled; an unpaid one is, with a reason the owner sees
        Assert.Contains("لا يمكن إلغاؤها", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/violations/{id}/cancel", new { reason = "خطأ في الإصدار" })));
        var second = await officer.Post<JsonElement>("/api/admin/violations", new { ownerId = mine[0].GetProperty("ownerId").GetString(), typeId = fineId, description = "تأخير ثانٍ في سداد قيمة الشعار" });
        var sid = second.GetProperty("id").GetString();
        Assert.Contains("سبب", await officer.Fails(() => officer.Post<JsonElement>($"/api/admin/violations/{sid}/cancel", new { reason = "" })));
        var cancelled = await officer.Post<JsonElement>($"/api/admin/violations/{sid}/cancel", new { reason = "صدرت بالخطأ" });
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(1, (await officer.Get<JsonElement>("/api/admin/violations?status=cancelled&q=0500000001")).GetProperty("total").GetInt32());
        Assert.Contains("غير متاحة", await owner.Fails(() => owner.Post<JsonElement>("/api/payments/card", new { purpose = "violation", @ref = sid, amount = 0, callback = "https://sammha.test/cb" })));
        Assert.Contains("لغيت", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/violations/{sid}/pay")));

        // a type in use is switched off, not deleted
        Assert.Contains("أوقفه", await admin.Fails(() => admin.Send<JsonElement>(HttpMethod.Delete, $"/api/admin/violation-types/{fineId}", null)));
        await admin.Put<JsonElement>($"/api/admin/violation-types/{fineId}", new { name = "تأخير سداد قيمة شعار", amount = 750, active = false });
        Assert.Contains("موقوف", await officer.Fails(() => officer.Post<JsonElement>("/api/admin/violations", new { owner = "0500000001", typeId = fineId, description = "نوع موقوف لا يُصدر منه" })));
        Assert.Equal(500, (await owner.Get<JsonElement>("/api/me/violations")).EnumerateArray().First(x => x.GetProperty("id").GetString() == id).GetProperty("amount").GetDecimal());
        var unused = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "نوع للحذف", amount = 10 });
        await admin.Send<JsonElement>(HttpMethod.Delete, $"/api/admin/violation-types/{unused.GetProperty("id").GetString()}", null);
    }

    [Fact]
    public async Task Violations_are_private_and_need_their_permissions()
    {
        var admin = await Admin();
        var type = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "استخدام شعار غير معتمد", amount = 1000 });
        var v = await admin.Post<JsonElement>("/api/admin/violations", new
        {
            owner = "0500000003", typeId = type.GetProperty("id").GetString(), description = "استخدام شعار لم تعتمده اللجنة في مهرجان",
            attachment = new { fileName = "a.png", dataBase64 = Png },
        });
        var id = v.GetProperty("id").GetString();
        // the admin holds owners.full: the phone is shown in full
        Assert.Equal("0500000003", v.GetProperty("ownerPhone").GetString());

        // the default committee role has neither violations.view nor violations.manage
        var committee = await app.CommitteeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Get<JsonElement>("/api/admin/violations")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Get<JsonElement>($"/api/admin/violations/{id}/attachment")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>("/api/admin/violations", new { owner = "0500000001", typeId = type.GetProperty("id").GetString(), description = "محاولة إصدار بلا صلاحية" })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>("/api/admin/violation-types", new { name = "نوع جديد", amount = 5 })));

        // view only: can list and download, cannot issue or cancel
        var viewer = await StaffAsync(admin, "0500000082", "violations.view");
        Assert.Equal(1, (await viewer.Get<JsonElement>("/api/admin/violations?q=0500000003")).GetProperty("total").GetInt32());
        await viewer.Get<JsonElement>($"/api/admin/violations/{id}/attachment");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => viewer.Post<JsonElement>($"/api/admin/violations/{id}/cancel", new { reason = "لا صلاحية" })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => viewer.Post<JsonElement>("/api/admin/violations/owner", new { q = "0500000001" })));

        // another owner sees nothing of it
        var other = await app.OwnerAsync();
        Assert.DoesNotContain((await other.Get<JsonElement>("/api/me/violations")).EnumerateArray(), x => x.GetProperty("id").GetString() == id);
        Assert.Equal(HttpStatusCode.NotFound, await StatusOf(() => other.Get<JsonElement>($"/api/me/violations/{id}/attachment")));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOf(() => other.Post<JsonElement>($"/api/me/violations/{id}/pay")));
        Assert.Contains("غير متاحة", await other.Fails(() => other.Post<JsonElement>("/api/payments/card", new { purpose = "violation", @ref = id, amount = 0, callback = "https://sammha.test/cb" })));

        // owners cannot reach the staff list
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => other.Get<JsonElement>("/api/admin/violations")));
    }

    [Fact]
    public async Task Festival_fines_are_paid_settled_by_a_name_or_end_in_a_ban()
    {
        var admin = await Admin();
        Assert.Contains("قبل", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/festivals", new { name = "مهرجان بتواريخ مقلوبة", startsOn = "2026-12-31", endsOn = "2026-12-01" })));
        var festival = await admin.Post<JsonElement>("/api/admin/festivals", new { name = "مهرجان الملك عبدالعزيز للإبل 2026", nameEn = "King Abdulaziz Camel Festival 2026", year = 2026, startsOn = "2026-12-01", endsOn = "2026-12-31" });
        Assert.Equal("2026-12-01", festival.GetProperty("startsOn").GetString());
        var fid = festival.GetProperty("id").GetString();
        var off = await admin.Post<JsonElement>("/api/admin/festivals", new { name = "مهرجان موقوف للتجربة", active = false });
        var noName = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "المشاركة بجمل بدون اسم مسجل", amount = 5000, allowsNameResolution = true });
        Assert.True(noName.GetProperty("allowsNameResolution").GetBoolean());
        var strict = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "مخالفة لا تقبل التسوية بالاسم", amount = 200 });
        Assert.False(strict.GetProperty("allowsNameResolution").GetBoolean());

        Task<JsonElement> Issue(JsonElement type, string? festivalId, string? chip = "9821000000099") => admin.Post<JsonElement>("/api/admin/violations", new
        {
            owner = "0500000004", typeId = type.GetProperty("id").GetString(), festivalId, camelChip = chip, description = "المشاركة في المهرجان بجمل ليس له اسم مسجل",
        });
        Assert.Contains("موقوف", await admin.Fails(() => Issue(noName, off.GetProperty("id").GetString())));
        Assert.Contains("الشريحة", await admin.Fails(() => Issue(noName, fid, "98-21")));
        Assert.Empty((await admin.Post<JsonElement>("/api/admin/violations/owner", new { q = "0500000004" })).GetProperty("chips").EnumerateArray());

        // 1) pay and keep taking part
        var v1 = await Issue(noName, fid);
        var id1 = v1.GetProperty("id").GetString();
        Assert.Equal("مهرجان الملك عبدالعزيز للإبل 2026", v1.GetProperty("festivalName").GetString());
        Assert.Equal("9821000000099", v1.GetProperty("camelChip").GetString());
        Assert.True(v1.GetProperty("allowsNameResolution").GetBoolean());
        // renaming the festival leaves issued violations as they were
        await admin.Put<JsonElement>($"/api/admin/festivals/{fid}", new { name = "مهرجان الملك عبدالعزيز للإبل 2026 — النسخة العاشرة", year = 2026, active = true });
        Assert.Equal("مهرجان الملك عبدالعزيز للإبل 2026", (await admin.Get<JsonElement>($"/api/admin/violations/{id1}")).GetProperty("festivalName").GetString());
        var owner = await app.BidderAsync(2); // 0500000004
        var paid = await owner.PayByCard("violation", id1, $"/api/me/violations/{id1}/pay");
        Assert.Equal("paid", paid.GetProperty("status").GetString());
        Assert.Equal("paid_continue", paid.GetProperty("resolution").GetString());
        Assert.Contains("لا يمكن إلغاؤها", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/violations/{id1}/cancel", new { reason = "إلغاء بعد السداد" })));
        Assert.Contains("غلقت", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/violations/{id1}/ban", new { reason = "منع بعد السداد" })));
        Assert.Contains("غلقت", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/violations/{id1}/resolve-name", new { note = "بعد السداد" })));

        // 2) settled by registering a name: closed with no payment
        var v2 = await Issue(noName, fid);
        var id2 = v2.GetProperty("id").GetString();
        var settled = await admin.Post<JsonElement>($"/api/admin/violations/{id2}/resolve-name", new { note = "سُجل الاسم «الوافي» للجمل" });
        Assert.Equal("resolved_name", settled.GetProperty("status").GetString());
        Assert.Equal("resolved_name", settled.GetProperty("resolution").GetString());
        var mine2 = (await owner.Get<JsonElement>("/api/me/violations")).EnumerateArray().First(x => x.GetProperty("id").GetString() == id2);
        Assert.Contains("الوافي", mine2.GetProperty("resolutionNote").GetString());
        Assert.False(mine2.TryGetProperty("resolvedByName", out _));
        Assert.Contains("غير متاحة", await owner.Fails(() => owner.Post<JsonElement>("/api/payments/card", new { purpose = "violation", @ref = id2, amount = 0, callback = "https://sammha.test/cb" })));
        Assert.Contains("بتسجيل اسم", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/violations/{id2}/pay")));
        Assert.Contains("غلقت", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/violations/{id2}/cancel", new { reason = "إلغاء بعد التسوية" })));

        // 3) a type that does not take a name settlement; no payment → barred from the festival
        var v3 = await Issue(strict, fid, null);
        var id3 = v3.GetProperty("id").GetString();
        Assert.False(v3.TryGetProperty("camelChip", out _));
        Assert.Contains("لا يقبل", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/violations/{id3}/resolve-name", new { note = "" })));
        Assert.Contains("سبب", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/violations/{id3}/ban", new { reason = "" })));
        var banned = await admin.Post<JsonElement>($"/api/admin/violations/{id3}/ban", new { reason = "لم تُسدَّد الغرامة ولم يُسجَّل اسم" });
        Assert.Equal("banned", banned.GetProperty("status").GetString());
        Assert.Contains("غير متاحة", await owner.Fails(() => owner.Post<JsonElement>("/api/payments/card", new { purpose = "violation", @ref = id3, amount = 0, callback = "https://sammha.test/cb" })));
        Assert.Contains("المنع", await owner.Fails(() => owner.Post<JsonElement>($"/api/me/violations/{id3}/pay")));
        Assert.Contains("المنع", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/violations/{id3}/cancel", new { reason = "إلغاء بعد المنع" })));
        Assert.Contains((await owner.Get<JsonElement>("/api/me/notifications")).EnumerateArray(), n => n.GetProperty("title").GetString()!.Contains(v3.GetProperty("number").GetString()!));

        // the staff list by festival and by outcome
        var byFestival = await admin.Get<JsonElement>($"/api/admin/violations?festivalId={fid}");
        Assert.Equal(3, byFestival.GetProperty("total").GetInt32());
        var counts = byFestival.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("paid").GetInt32());
        Assert.Equal(1, counts.GetProperty("resolvedName").GetInt32());
        Assert.Equal(1, counts.GetProperty("banned").GetInt32());
        Assert.Equal(id3, (await admin.Get<JsonElement>($"/api/admin/violations?festivalId={fid}&status=banned")).GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(id2, (await admin.Get<JsonElement>($"/api/admin/violations?festivalId={fid}&status=resolved_name")).GetProperty("items")[0].GetProperty("id").GetString());

        // a festival in use is switched off, not deleted; the committee cannot manage festivals
        Assert.Contains("أوقفه", await admin.Fails(() => admin.Send<JsonElement>(HttpMethod.Delete, $"/api/admin/festivals/{fid}", null)));
        await admin.Send<JsonElement>(HttpMethod.Delete, $"/api/admin/festivals/{off.GetProperty("id").GetString()}", null);
        var committee = await app.CommitteeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>("/api/admin/festivals", new { name = "مهرجان بلا صلاحية" })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusOf(() => committee.Post<JsonElement>($"/api/admin/violations/{id3}/ban", new { reason = "بلا صلاحية" })));
    }

    [Fact]
    public async Task Owner_settles_a_no_name_fine_by_linking_a_name_they_own_to_the_chip()
    {
        var admin = await Admin();
        var type = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "جمل بلا اسم — ربط", amount = 5000, allowsNameResolution = true });
        var strict = await admin.Post<JsonElement>("/api/admin/violation-types", new { name = "مخالفة بلا تسوية بالاسم", amount = 300 });
        Task<JsonElement> Issue(JsonElement t, string? chip) => admin.Post<JsonElement>("/api/admin/violations", new
        {
            owner = "0500000003", typeId = t.GetProperty("id").GetString(), camelChip = chip, description = "المشاركة في المهرجان بجمل ليس له اسم مسجل",
        });
        var owner = await app.BidderAsync(1); // owns قعدان الفاخر وشامخ، منقية الوسمية
        var names = (await owner.Get<JsonElement>("/api/me/names")).EnumerateArray().ToList();
        string Id(string n) => names.First(x => x.GetProperty("name").GetString() == n).GetProperty("id").GetString()!;

        var v = (await Issue(type, "9821000000777")).GetProperty("id").GetString();
        string Url(string? id) => $"/api/me/violations/{id}/link-name";
        Assert.Contains("المنقية", await owner.Fails(() => owner.Post<JsonElement>(Url(v), new { reservedId = Id("الوسمية") })));
        var done = await owner.Post<JsonElement>(Url(v), new { reservedId = Id("الفاخر") });
        Assert.Equal("resolved_name", done.GetProperty("status").GetString());
        Assert.Contains("9821000000777", done.GetProperty("resolutionNote").GetString());
        // the chip now shows on «إبلي» with the name, and nothing is charged
        var camel = (await owner.Get<JsonElement>("/api/me/camels")).GetProperty("camels").EnumerateArray()
            .Single(c => c.GetProperty("chipNumbers").EnumerateArray().Any(x => x.GetString() == "9821000000777"));
        Assert.Equal("الفاخر", camel.GetProperty("reservedName").GetString());
        Assert.Contains("غلقت", await owner.Fails(() => owner.Post<JsonElement>(Url(v), new { reservedId = Id("شامخ") })));

        // the same chip again with another name, a type without name settlement, a fine without a chip: refused
        var again = (await Issue(type, "9821000000777")).GetProperty("id").GetString();
        Assert.Contains("مربوطة باسم آخر", await owner.Fails(() => owner.Post<JsonElement>(Url(again), new { reservedId = Id("شامخ") })));
        var s = (await Issue(strict, "9821000000778")).GetProperty("id").GetString();
        Assert.Contains("لا يقبل", await owner.Fails(() => owner.Post<JsonElement>(Url(s), new { reservedId = Id("شامخ") })));
        var noChip = (await Issue(type, null)).GetProperty("id").GetString();
        Assert.Contains("شريحة", await owner.Fails(() => owner.Post<JsonElement>(Url(noChip), new { reservedId = Id("شامخ") })));
        // someone else's fine is not found
        var other = await app.BidderAsync(2);
        var e = await Assert.ThrowsAsync<ApiException>(() => other.Post<JsonElement>(Url(again), new { reservedId = Id("شامخ") }));
        Assert.Equal(HttpStatusCode.NotFound, e.Status);
    }
}
