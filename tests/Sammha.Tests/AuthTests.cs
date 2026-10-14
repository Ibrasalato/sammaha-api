using System.Net;
using System.Text.Json;

namespace Sammha.Tests;

public class AuthTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Sign_up_then_two_step_sign_in()
    {
        var api = app.Anonymous();
        var res = await api.Post<JsonElement>("/api/auth/signup", new
        {
            nationalId = "1012345678", firstName = "عبدالله", fatherName = "محمد", grandfatherName = "سالم", familyName = "القحطاني", birthDate = "1990-05-01",
            gender = "male", nationality = "SA", residenceCountry = "SA", residenceCity = "الرياض", phoneCountry = "966", phone = "551112233", email = "a.q@example.com", password = "secret12",
        });
        Assert.Equal("owner", res.GetProperty("user").GetProperty("role").GetString());

        var ch = await api.Post<JsonElement>("/api/auth/signin", new { login = "0551112233", password = "secret12", portal = "owner" });
        Assert.Equal("05•••••233", ch.GetProperty("phoneMasked").GetString());
        var challenge = ch.GetProperty("challenge").GetString();
        var wrong = await api.Fails(() => api.Post<JsonElement>("/api/auth/signin/complete", new { challenge, code = "0000" }));
        Assert.Contains("الرمز غير صحيح", wrong);
        var ok = await api.Post<JsonElement>("/api/auth/signin/complete", new { challenge, code = "4321" });
        api.Token = ok.GetProperty("token").GetString();
        var me = await api.Get<JsonElement>("/api/auth/me");
        Assert.Equal("a.q@example.com", me.GetProperty("email").GetString());
        // a used code cannot be replayed
        await api.Fails(() => api.Post<JsonElement>("/api/auth/signin/complete", new { challenge, code = "4321" }));
    }

    [Fact]
    public async Task Staff_portal_is_separate_and_owner_pages_need_a_token()
    {
        var api = app.Anonymous();
        var msg = await api.Fails(() => api.Post<JsonElement>("/api/auth/signin", new { login = "0500000002", password = "123456", portal = "owner" }));
        Assert.Equal("رقم الجوال أو كلمة المرور غير صحيحة", msg);
        var committee = await app.CommitteeAsync();
        Assert.Equal("committee", (await committee.Get<JsonElement>("/api/auth/me")).GetProperty("role").GetString());
        var e = await Assert.ThrowsAsync<ApiException>(() => api.Get<JsonElement>("/api/me/wallet"));
        Assert.Equal(HttpStatusCode.Unauthorized, e.Status);
        var owner = await app.OwnerAsync();
        var f = await Assert.ThrowsAsync<ApiException>(() => owner.Get<JsonElement>("/api/admin/overview"));
        Assert.Equal(HttpStatusCode.Forbidden, f.Status);
    }

    [Fact]
    public async Task Phone_change_needs_the_code_sent_to_the_new_number()
    {
        var owner = await app.SignInAsync("0500000005");
        var ch = await owner.Post<JsonElement>("/api/me/phone", new { phone = "0559998877" });
        var me = await owner.Post<JsonElement>("/api/me/phone/confirm", new { challenge = ch.GetProperty("challenge").GetString(), code = "4321" });
        Assert.Equal("0559998877", me.GetProperty("phone").GetString());
    }
}
