using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Infrastructure.Services;

/// <summary>
/// HyperPay COPYandPAY (the OPPWA platform). 1) the server prepares a checkout for one channel (mada, Visa/Mastercard or Apple Pay —
/// each has its own entity id); 2) the owner pays on our small page that loads HyperPay's widget; 3) HyperPay sends them back and the
/// server asks for the result before crediting anything.
/// </summary>
public partial class HyperPayGateway(HttpClient http, IOptions<PaymentOptions> opt, ILogger<HyperPayGateway> log) : IPaymentGateway
{
    private HyperPayOptions H => opt.Value.HyperPay;
    public string Name => "hyperpay";

    public IReadOnlyList<string> Channels
    {
        get
        {
            if (string.IsNullOrWhiteSpace(H.AccessToken)) return [];
            var l = new List<string>();
            if (!string.IsNullOrWhiteSpace(H.MadaEntityId)) l.Add(PayChannels.Mada);
            if (!string.IsNullOrWhiteSpace(H.EntityId)) l.Add(PayChannels.Card);
            if (!string.IsNullOrWhiteSpace(H.ApplePayEntityId)) l.Add(PayChannels.ApplePay);
            return l;
        }
    }

    public string EntityFor(string? method) => method switch
    {
        PayChannels.Mada => H.MadaEntityId ?? "",
        PayChannels.ApplePay => H.ApplePayEntityId ?? "",
        _ => H.EntityId ?? "",
    } is { Length: > 0 } e ? e : throw new DomainException("وسيلة الدفع هذه غير مفعّلة حاليًا");

    /// <summary>the widget brands of a channel</summary>
    public static string BrandsFor(string? method) => method switch { PayChannels.Mada => "MADA", PayChannels.ApplePay => "APPLEPAY", _ => "VISA MASTER" };

    private string Base => H.BaseUrl.EndsWith('/') ? H.BaseUrl : H.BaseUrl + "/";
    public string WidgetScript(string checkoutId) => $"{Base}v1/paymentWidgets.js?checkoutId={Uri.EscapeDataString(checkoutId)}";

    private HttpRequestMessage Req(HttpMethod m, string path, HttpContent? body = null)
    {
        var r = new HttpRequestMessage(m, Base + path) { Content = body };
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", H.AccessToken);
        return r;
    }

    public async Task<(string ProviderId, string Url)> CreateCheckoutAsync(Payment payment, string description, string callbackUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(H.AccessToken)) throw new DomainException("الدفع الإلكتروني غير مفعّل حاليًا");
        var api = (opt.Value.PublicApiUrl ?? "").TrimEnd('/');
        if (api.Length == 0) throw new DomainException("الدفع الإلكتروني غير مهيأ (Payments:PublicApiUrl)");
        var form = new Dictionary<string, string>
        {
            ["entityId"] = EntityFor(payment.Method),
            ["amount"] = payment.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = "SAR",
            ["paymentType"] = "DB",
            ["merchantTransactionId"] = payment.Id.ToString("N"),
            ["integrity"] = "true",
        };
        if (!string.IsNullOrWhiteSpace(H.TestMode)) { form["testMode"] = H.TestMode!; form["customParameters[3DS2_enrolled]"] = "true"; }
        using var req = Req(HttpMethod.Post, "v1/checkouts", new FormUrlEncodedContent(form));
        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        var code = json.TryGetProperty("result", out var r) && r.TryGetProperty("code", out var c) ? c.GetString() : null;
        if (!res.IsSuccessStatusCode || !json.TryGetProperty("id", out var id) || code is null || !code.StartsWith("000.200"))
        {
            log.LogWarning("HyperPay checkout failed for {Payment}: {Code}", payment.Id, code);
            throw new DomainException("تعذّر بدء عملية الدفع، حاول مرة أخرى");
        }
        payment.Integrity = json.TryGetProperty("integrity", out var integ) ? integ.GetString() : null;
        // our own page shows HyperPay's form (the widget must live on a page we serve)
        return (id.GetString()!, $"{api}/api/payments/hyperpay/{payment.Id}/form");
    }

    // the OPPWA result-code groups
    [GeneratedRegex(@"^(000\.000\.|000\.100\.1|000\.[36]|000\.400\.[1][12]0)")] private static partial Regex Success();
    [GeneratedRegex(@"^(000\.200|800\.400\.5|100\.400\.500)")] private static partial Regex Pending();

    public async Task<GatewayStatus> StatusAsync(Payment payment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payment.ProviderId)) return new(false, false, 0, null, "no checkout");
        using var req = Req(HttpMethod.Get, $"v1/checkouts/{Uri.EscapeDataString(payment.ProviderId)}/payment?entityId={Uri.EscapeDataString(EntityFor(payment.Method))}");
        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        var code = json.TryGetProperty("result", out var r) && r.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var message = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("description", out var d) ? d.GetString() : null;
        var amount = json.TryGetProperty("amount", out var a) && decimal.TryParse(a.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var currencyOk = !json.TryGetProperty("currency", out var cur) || cur.GetString() == "SAR";
        var ours = !json.TryGetProperty("merchantTransactionId", out var mt) || mt.GetString() == payment.Id.ToString("N");
        var paid = Success().IsMatch(code) && currencyOk && ours;
        return new(paid, !paid && Pending().IsMatch(code), amount, code, message);
    }
}

/// <summary>no provider configured yet: card payments say so, wallet payments keep working</summary>
public class NoPaymentGateway : IPaymentGateway
{
    public string Name => "none";
    public IReadOnlyList<string> Channels => [];
    public Task<(string ProviderId, string Url)> CreateCheckoutAsync(Payment payment, string description, string callbackUrl, CancellationToken ct) =>
        throw new DomainException("الدفع الإلكتروني غير مفعّل حاليًا، تواصل مع الدعم");
    public Task<GatewayStatus> StatusAsync(Payment payment, CancellationToken ct) => Task.FromResult(new GatewayStatus(false, false, 0, null, null));
}
