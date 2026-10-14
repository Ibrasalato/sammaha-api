using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

/// <summary>Owner sign-up with identity and nationality, sign-in by ID, profile completion and email verification.</summary>
public class SignupTests(TestApp app) : IClassFixture<TestApp>
{
    private static object Owner(string id = "2123456789", string nationality = "EG", string residence = "SA", string phone = "512345670", string code = "966",
        string email = "owner.new@example.com", string birth = "1992-03-15") => new
    {
        nationalId = id, firstName = "أحمد", fatherName = "علي", grandfatherName = "حسن", familyName = "المصري", birthDate = birth, gender = "male",
        nationality, residenceCountry = residence, residenceCity = "الرياض", phoneCountry = code, phone, email, password = "Owner#2026",
    };

    [Fact]
    public async Task Sign_up_then_sign_in_with_the_identity_number()
    {
        var anon = app.Anonymous();
        var r = await anon.SignUp(Owner());
        var user = r.GetProperty("user");
        Assert.Equal("أحمد علي حسن المصري", user.GetProperty("fullName").GetString());
        Assert.Equal("0512345670", user.GetProperty("phone").GetString());
        var profile = user.GetProperty("profile");
        Assert.Equal("EG", profile.GetProperty("nationality").GetString());
        Assert.False(profile.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(33, profile.GetProperty("completion").GetInt32());
        Assert.Equal(new[] { "email", "address" }, profile.GetProperty("missing").EnumerateArray().Select(x => x.GetString()!).ToArray());

        var api = await app.SignInAsync("2123456789", "Owner#2026");
        Assert.NotNull(api.Token);
        var dup = await anon.Fails(() => anon.SignUp(Owner(phone: "512345671", email: "other@example.com")));
        Assert.Contains("رقم الهوية مسجل", dup);
    }

    [Theory]
    [InlineData("1123456789", "EG", "SA", "رقم الإقامة يبدأ بـ 2")]       // a non-Saudi resident uses an iqama
    [InlineData("2123456789", "SA", "SA", "رقم الهوية الوطنية يبدأ بـ 1")] // a citizen uses the national ID
    [InlineData("12345", "SA", "AE", "10 أرقام")]                          // Saudis abroad still use the national ID
    public async Task Identity_numbers_follow_nationality_and_residence(string id, string nationality, string residence, string expected)
    {
        var msg = await app.Anonymous().Fails(() => app.Anonymous().SignUp(Owner(id: id, nationality: nationality, residence: residence, phone: "512340001", email: $"id{id}@example.com")));
        Assert.Contains(expected, msg);
    }

    [Fact]
    public async Task International_numbers_minors_and_passports()
    {
        var anon = app.Anonymous();
        var r = await anon.SignUp(Owner(id: "A1234567", nationality: "AE", residence: "AE", phone: "0501234567", code: "971", email: "uae@example.com"));
        Assert.Equal("+971501234567", r.GetProperty("user").GetProperty("phone").GetString());
        var api = await app.SignInAsync("+971501234567", "Owner#2026");
        Assert.NotNull(api.Token);

        var minor = await anon.Fails(() => anon.SignUp(Owner(id: "2999999999", phone: "512340002", email: "young@example.com", birth: DateTime.UtcNow.AddYears(-16).ToString("yyyy-MM-dd"))));
        Assert.Contains("18 سنة", minor);
        var badPhone = await anon.Fails(() => anon.SignUp(Owner(id: "2999999998", phone: "41234", email: "p@example.com")));
        Assert.Contains("رقم الجوال", badPhone);
    }

    [Fact]
    public async Task Address_and_email_verification_complete_the_profile()
    {
        await app.Anonymous().SignUp(Owner(id: "2111111111", phone: "512340003", email: "complete@example.com"));
        var api = await app.SignInAsync("2111111111", "Owner#2026");

        var msg = await api.Fails(() => api.Put<JsonElement>("/api/me/address", new { addressCountry = "SA", addressCity = "الرياض", district = "الملقا", postalCode = "ab" }));
        Assert.Contains("الرمز البريدي", msg);
        var u = await api.Put<JsonElement>("/api/me/address", new { addressCountry = "SA", addressCity = "الرياض", district = "الملقا", postalCode = "13521", street = "طريق أنس بن مالك", altPhone = "0598765432", altPhoneCountry = "966" });
        Assert.Equal("13521", u.GetProperty("profile").GetProperty("postalCode").GetString());
        Assert.Equal(new[] { "email" }, u.GetProperty("profile").GetProperty("missing").EnumerateArray().Select(x => x.GetString()!).ToArray());

        var ch = await api.Post<JsonElement>("/api/me/email/verify");
        Assert.Contains("@example.com", ch.GetProperty("phoneMasked").GetString());
        var bad = await api.Fails(() => api.Post<JsonElement>("/api/me/email/verify/confirm", new { challenge = ch.GetProperty("challenge").GetString(), code = "0000" }));
        Assert.Contains("غير صحيح", bad);
        u = await api.Post<JsonElement>("/api/me/email/verify/confirm", new { challenge = ch.GetProperty("challenge").GetString(), code = "4321" });
        Assert.True(u.GetProperty("profile").GetProperty("emailVerified").GetBoolean());
        Assert.Equal(100, u.GetProperty("profile").GetProperty("completion").GetInt32());
        Assert.Contains("موثّق مسبقًا", await api.Fails(() => api.Post<JsonElement>("/api/me/email/verify")));
    }

    [Fact]
    public async Task The_mobile_number_is_verified_before_the_account_exists()
    {
        var anon = app.Anonymous();
        var body = Owner(id: "2111111133", phone: "512340005", email: "verify.phone@example.com");
        Assert.Contains("رمز التحقق", await anon.Fails(() => anon.Post<JsonElement>("/api/auth/signup", body)));
        var ch = await anon.Post<JsonElement>("/api/auth/signup/start", body);
        Assert.Equal("05•••••005", ch.GetProperty("phoneMasked").GetString());
        // no account yet: signing in fails
        Assert.Contains("غير صحيحة", await anon.Fails(() => anon.Post<JsonElement>("/api/auth/signin", new { login = "2111111133", password = "Owner#2026", portal = "owner" })));
        var challenge = ch.GetProperty("challenge").GetString();
        Assert.Contains("غير صحيح", await anon.Fails(() => anon.Post<JsonElement>("/api/auth/signup", Merge(body, challenge!, "0000"))));
        // the code belongs to the number it was sent to
        Assert.Contains("تغيّر رقم الجوال", await anon.Fails(() => anon.Post<JsonElement>("/api/auth/signup", Merge(Owner(id: "2111111133", phone: "512340006", email: "verify.phone@example.com"), challenge!, "4321"))));
        var ch2 = await anon.Post<JsonElement>("/api/auth/signup/start", body);
        var r = await anon.Post<JsonElement>("/api/auth/signup", Merge(body, ch2.GetProperty("challenge").GetString()!, "4321"));
        Assert.Equal("owner", r.GetProperty("user").GetProperty("role").GetString());
    }

    private static System.Text.Json.Nodes.JsonObject Merge(object body, string challenge, string code)
    {
        var o = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        o["challenge"] = challenge; o["code"] = code;
        return o;
    }

    [Fact]
    public async Task Identity_and_name_are_fixed_after_sign_up()
    {
        await app.Anonymous().SignUp(Owner(id: "2111111122", phone: "512340004", email: "fixed@example.com"));
        var api = await app.SignInAsync("2111111122", "Owner#2026");
        var me = await api.Get<JsonElement>("/api/auth/me");
        var name = me.GetProperty("fullName").GetString();
        Assert.Contains("الدعم", await api.Fails(() => api.Put<JsonElement>("/api/me/account", new { fullName = name, nationalId = "" })));
        Assert.Contains("الدعم", await api.Fails(() => api.Put<JsonElement>("/api/me/account", new { fullName = "اسم آخر مختلف تمامًا", nationalId = "2111111122" })));
        await api.Put<JsonElement>("/api/me/account", new { fullName = name, nationalId = "2111111122" }); // unchanged is fine
    }

    [Fact]
    public async Task Older_accounts_fill_in_their_personal_details()
    {
        var owner = await app.SignInAsync("0500000005"); // seeded before the new fields
        var me = await owner.Get<JsonElement>("/api/auth/me");
        Assert.Contains("personal", me.GetProperty("profile").GetProperty("missing").EnumerateArray().Select(x => x.GetString()));
        var u = await owner.Put<JsonElement>("/api/me/personal", new
        {
            firstName = "فهد", fatherName = "سعد", grandfatherName = "ناصر", familyName = "العتيبي", birthDate = "1980-07-07", gender = "male",
            nationality = "SA", residenceCountry = "SA", residenceCity = "القصيم", nationalId = "1055555555",
        });
        Assert.Equal("فهد سعد ناصر العتيبي", u.GetProperty("fullName").GetString());
        Assert.DoesNotContain("personal", u.GetProperty("profile").GetProperty("missing").EnumerateArray().Select(x => x.GetString()));
    }
}
