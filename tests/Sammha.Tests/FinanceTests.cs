using System.Text;
using System.Text.Json;
using Sammha.Domain;

namespace Sammha.Tests;

/// <summary>Bank accounts with the IBAN certificate, refunds to the bank, wallet adjustments, guarantees and the marks report.</summary>
public class FinanceTests(TestApp app) : IClassFixture<TestApp>
{
    private Task<Api> Admin() => app.SignInAsync("ibrasalato@gmail.com", "Sammha@2026", "staff");
    private static readonly string Pdf = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n%%EOF"));

    [Theory]
    [InlineData("SA03 8000 0000 6080 1016 7519", true, "مصرف الراجحي")]
    [InlineData("SA0380000000608010167518", false, null)]
    [InlineData("SA038000000060801016751", false, null)]
    [InlineData("GB82WEST12345698765432", true, null)]
    public void Iban_checker(string iban, bool valid, string? bank)
    {
        var c = Iban.Check(iban);
        Assert.Equal(valid, c.Valid);
        if (bank != null) Assert.Equal(bank, c.BankName);
    }

    [Fact]
    public async Task Bank_account_verification_and_refunds_to_the_bank()
    {
        var owner = await app.SignInAsync("0500000004");
        var admin = await Admin();
        Assert.False((await owner.Get<JsonElement>("/api/banks/check-iban?iban=SA0380000000608010167518")).GetProperty("valid").GetBoolean());

        Assert.Contains("PDF", await owner.Fails(() => owner.Put<JsonElement>("/api/me/bank-account", new { iban = "SA0380000000608010167519", accountName = "فهد عبدالله الدوسري", docName = "x.exe", docBase64 = Convert.ToBase64String(new byte[] { 0x4D, 0x5A, 0, 0, 0 }) })));
        var b = await owner.Put<JsonElement>("/api/me/bank-account", new { iban = "SA03 8000 0000 6080 1016 7519", accountName = "فهد عبدالله الدوسري", docName = "iban.pdf", docBase64 = Pdf });
        Assert.Equal("pending", b.GetProperty("status").GetString());
        Assert.Equal("مصرف الراجحي", b.GetProperty("bankName").GetString());

        Assert.Contains("اعتماد", await owner.Fails(() => owner.Post<JsonElement>("/api/me/withdrawals", new { amount = 100 })));

        var me = await owner.Get<JsonElement>("/api/auth/me");
        var uid = me.GetProperty("id").GetString();
        var doc = await admin.Get<JsonElement>($"/api/admin/bank-accounts/{uid}/document");
        Assert.Equal("application/pdf", doc.GetProperty("contentType").GetString());
        await admin.Post<JsonElement>($"/api/admin/bank-accounts/{uid}/verify");

        var before = await owner.Balance();
        var w = await owner.Post<JsonElement>("/api/me/withdrawals", new { amount = 100 });
        Assert.StartsWith("WD-", w.GetProperty("number").GetString());
        Assert.Equal(before - 100, await owner.Balance());
        await admin.Post<JsonElement>($"/api/admin/withdrawals/{w.GetProperty("id").GetString()}/reject", new { reason = "الاسم لا يطابق" });
        Assert.Equal(before, await owner.Balance()); // the amount came back

        w = await owner.Post<JsonElement>("/api/me/withdrawals", new { amount = 150 });
        var done = await admin.Post<JsonElement>($"/api/admin/withdrawals/{w.GetProperty("id").GetString()}/complete", new { transferRef = "TRX-55821" });
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Contains("تمت معالجة", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/withdrawals/{w.GetProperty("id").GetString()}/complete", new { transferRef = "TRX-1" })));

        // direct payout: approving sends the money straight to the IBAN (simulated gateway in tests)
        Assert.True((await admin.Get<JsonElement>("/api/admin/payouts")).GetProperty("enabled").GetBoolean());
        w = await owner.Post<JsonElement>("/api/me/withdrawals", new { amount = 25 });
        var paid = await admin.Post<JsonElement>($"/api/admin/withdrawals/{w.GetProperty("id").GetString()}/payout");
        Assert.Equal("completed", paid.GetProperty("status").GetString());
        Assert.StartsWith("SIM-", paid.GetProperty("transferRef").GetString());
        Assert.Contains("تمت معالجة", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/withdrawals/{w.GetProperty("id").GetString()}/payout")));

        var wallets = (await admin.Get<JsonElement>("/api/admin/wallets")).EnumerateArray().ToList();
        Assert.Contains(wallets, x => x.GetProperty("userId").GetString() == uid && x.GetProperty("balance").GetDecimal() == before - 175);

        // adjustment with a reason; a debit cannot go below zero
        await admin.Post<JsonElement>($"/api/admin/wallets/{uid}/adjust", new { amount = 25, reason = "تعويض عن خطأ في الفاتورة" });
        Assert.Equal(before - 125, await owner.Balance());
        Assert.Contains("أكبر من رصيد", await admin.Fails(() => admin.Post<JsonElement>($"/api/admin/wallets/{uid}/adjust", new { amount = -1_000_000, reason = "خصم تجريبي كبير" })));

        // changing the IBAN sends it back for verification
        b = await owner.Put<JsonElement>("/api/me/bank-account", new { iban = "GB82WEST12345698765432", accountName = "فهد عبدالله الدوسري", bankName = "Natwest" });
        Assert.Equal("pending", b.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Guarantees_report_and_refund()
    {
        var owner = await app.SignInAsync("0500000003");
        var admin = await Admin();
        var r = await owner.Post<JsonElement>("/api/me/requests", new { type = "qaadan", names = new[] { "ريح الشمال", "سهم الجنوب", "برق الغرب" } });
        var id = r.GetProperty("id").GetString();
        await owner.Post<JsonElement>($"/api/me/requests/{id}/pay");

        var rows = (await admin.Get<JsonElement>("/api/admin/deposits?state=held")).EnumerateArray().ToList();
        var row = rows.Single(x => x.GetProperty("requestId").GetString() == id);
        Assert.Equal(50, row.GetProperty("amount").GetDecimal());

        var before = await owner.Balance();
        await admin.Post<JsonElement>("/api/admin/deposits/refund", new { kind = "request", requestId = id, reason = "استثناء بقرار الإدارة" });
        Assert.Equal(before + 50, await owner.Balance());
        Assert.Contains("ليس محجوزًا", await admin.Fails(() => admin.Post<JsonElement>("/api/admin/deposits/refund", new { kind = "request", requestId = id, reason = "مرة ثانية" })));

        var history = await admin.Get<JsonElement>("/api/admin/settings/history");
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("field").GetString() == "DepositRefund");
    }

    [Fact]
    public async Task Marks_report_numbers_every_auction()
    {
        var admin = await Admin();
        var rows = (await admin.Get<JsonElement>("/api/admin/reports/marks")).EnumerateArray().ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, x => Assert.StartsWith("AU-", x.GetProperty("number").GetString()));
        Assert.Contains(rows, x => x.GetProperty("requestNumber").GetString()?.StartsWith("SM-") == true);
        var committee = await app.CommitteeAsync();
        await Assert.ThrowsAsync<ApiException>(() => committee.Get<JsonElement>("/api/admin/reports/marks"));
    }
}
