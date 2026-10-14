using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;
using Sammha.Infrastructure.Services;

namespace Sammha.Tests;

/// <summary>the HyperPay COPYandPAY client against a stand-in for HyperPay's API</summary>
public class HyperPayTests
{
    private sealed class Stub(Func<HttpRequestMessage, string> reply) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Req, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r, r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(r), Encoding.UTF8, "application/json") };
        }
    }

    private static (HyperPayGateway, Stub) Make(Func<HttpRequestMessage, string> reply, Action<HyperPayOptions>? setup = null)
    {
        var o = new PaymentOptions { Simulate = false, PublicApiUrl = "https://api.sammha.test/" };
        o.HyperPay.AccessToken = "TOKEN"; o.HyperPay.EntityId = "card-entity"; o.HyperPay.MadaEntityId = "mada-entity"; o.HyperPay.TestMode = "EXTERNAL";
        setup?.Invoke(o.HyperPay);
        var stub = new Stub(reply);
        return (new HyperPayGateway(new HttpClient(stub), Options.Create(o), NullLogger<HyperPayGateway>.Instance), stub);
    }

    [Fact]
    public async Task Prepares_a_checkout_for_the_chosen_channel()
    {
        var (g, stub) = Make(_ => """{"result":{"code":"000.200.100","description":"successfully created checkout"},"id":"CHK-1","integrity":"sha384-abc"}""");
        Assert.Equal([PayChannels.Mada, PayChannels.Card], g.Channels);
        var p = new Payment { Amount = 5000m, Method = PayChannels.Mada, Purpose = PaymentPurposes.Deposit };
        var (id, url) = await g.CreateCheckoutAsync(p, "x", "https://ibrasalato.github.io/sammaha/#/dashboard", CancellationToken.None);
        Assert.Equal("CHK-1", id);
        Assert.Equal($"https://api.sammha.test/api/payments/hyperpay/{p.Id}/form", url);
        Assert.Equal("sha384-abc", p.Integrity);
        var (req, body) = stub.Calls.Single();
        Assert.Equal("https://eu-test.oppwa.com/v1/checkouts", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Contains("entityId=mada-entity", body);
        Assert.Contains("amount=5000.00", body);
        Assert.Contains("currency=SAR", body);
        Assert.Contains("paymentType=DB", body);
        Assert.Contains("testMode=EXTERNAL", body);
        Assert.Equal("MADA", HyperPayGateway.BrandsFor(PayChannels.Mada));
        // a channel HyperPay has not given an entity id for is refused
        await Assert.ThrowsAsync<DomainException>(() => g.CreateCheckoutAsync(new Payment { Amount = 1, Method = PayChannels.ApplePay }, "x", "", CancellationToken.None));
    }

    [Theory]
    [InlineData("000.100.110", true, false)]
    [InlineData("000.000.000", true, false)]
    [InlineData("000.200.000", false, true)]
    [InlineData("800.100.151", false, false)]
    public async Task Reads_the_payment_result(string code, bool paid, bool pending)
    {
        var p = new Payment { Amount = 50m, Method = PayChannels.Card, ProviderId = "CHK-9" };
        var (g, stub) = Make(_ => $$"""{"result":{"code":"{{code}}","description":"d"},"amount":"50.00","currency":"SAR","merchantTransactionId":"{{p.Id:N}}"}""");
        var s = await g.StatusAsync(p, CancellationToken.None);
        Assert.Equal(paid, s.Paid);
        Assert.Equal(pending, s.Pending);
        Assert.Equal(50m, s.Amount);
        Assert.Equal("https://eu-test.oppwa.com/v1/checkouts/CHK-9/payment?entityId=card-entity", stub.Calls.Single().Req.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_result_for_another_payment_is_not_paid()
    {
        var p = new Payment { Amount = 50m, Method = PayChannels.Card, ProviderId = "CHK-9" };
        var (g, _) = Make(_ => """{"result":{"code":"000.000.000"},"amount":"50.00","currency":"SAR","merchantTransactionId":"someone-else"}""");
        Assert.False((await g.StatusAsync(p, CancellationToken.None)).Paid);
    }
}
