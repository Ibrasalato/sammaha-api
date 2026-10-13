using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Infrastructure.Services;

/// <summary>Moyasar hosted invoices (https://docs.moyasar.com). The webhook confirms the payment.</summary>
public class MoyasarGateway(HttpClient http, IOptions<PaymentOptions> opt) : IPaymentGateway
{
    private record Invoice([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("url")] string Url);

    public async Task<(string ProviderId, string Url)> CreateCheckoutAsync(Payment payment, string description, string callbackUrl, CancellationToken ct)
    {
        var key = opt.Value.MoyasarSecretKey;
        if (string.IsNullOrEmpty(key)) throw new DomainException("الدفع بالبطاقة غير مفعّل حاليًا، ادفع من المحفظة");
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.moyasar.com/v1/invoices")
        {
            Content = JsonContent.Create(new
            {
                amount = (long)Math.Round(payment.Amount * 100), // halalas
                currency = "SAR",
                description,
                callback_url = callbackUrl,
                metadata = new { payment_id = payment.Id.ToString() },
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(key + ":")));
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new DomainException("تعذّر إنشاء عملية الدفع، حاول مرة أخرى");
        var inv = await res.Content.ReadFromJsonAsync<Invoice>(ct) ?? throw new DomainException("تعذّر إنشاء عملية الدفع");
        return (inv.Id, inv.Url);
    }
}

public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> _h = new();
    public string Hash(string password) => _h.HashPassword(new object(), password);
    public bool Verify(string hash, string password) =>
        !string.IsNullOrEmpty(hash) && _h.VerifyHashedPassword(new object(), hash, password) != Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed;
}
