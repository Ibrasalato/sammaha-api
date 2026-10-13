using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features;

public record CardCheckout(string? Url, bool Simulated);

/// <summary>
/// Card payments (mada, Visa, Mastercard, Apple Pay) through Moyasar. A paid card payment tops the wallet up,
/// then completes what it was for (guarantee, invoice, participation guarantee or purchase) from the wallet.
/// With Payments:Simulate the payment succeeds at once (development).
/// </summary>
public class PaymentService(IAppDbContext db, TimeProvider clock, Access access, Money money, IPaymentGateway gateway, IOptions<PaymentOptions> opt,
    RequestService requests, WalletService wallet, BiddingService bidding, ListingService listings, ILogger<PaymentService> log)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<CardCheckout> StartAsync(string purpose, Guid? reference, decimal amount, string callbackUrl, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (!PaymentPurposes.All.Contains(purpose)) throw new DomainException("عملية دفع غير معروفة");
        // never trust the browser's amount: read it from the database
        amount = purpose switch
        {
            PaymentPurposes.Deposit => await db.Requests.Where(r => r.Id == reference && r.UserId == u.Id && r.Status == RequestStatuses.PendingPayment).Select(r => (decimal?)r.DepositAmount).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("الطلب غير متاح للسداد"),
            PaymentPurposes.Invoice => await db.Invoices.Where(i => i.Id == reference && i.UserId == u.Id && i.Status == InvoiceStatuses.Unpaid).Select(i => (decimal?)i.AmountDue).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("الفاتورة غير متاحة للسداد"),
            PaymentPurposes.BidDeposit => await db.Auctions.Where(a => a.Id == reference && a.ListingId != null && !a.Settled && a.RequesterId != u.Id).Select(a => a.BidDeposit).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("المزاد غير متاح"),
            PaymentPurposes.Listing => await db.Listings.Where(l => l.Id == reference && l.Mode == ListingModes.Instant && l.Status == ListingStatuses.Live && l.SellerId != u.Id).Select(l => (decimal?)l.Price).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("العرض غير متاح"),
            _ => amount >= 10 && amount <= 100_000 ? decimal.Round(amount, 2) : throw new DomainException("أقل مبلغ للشحن 10 ريال"),
        };
        var p = new Payment { UserId = u.Id, Purpose = purpose, Ref = reference, Amount = amount, CreatedAt = Now };
        db.Payments.Add(p);
        if (opt.Value.Simulate)
        {
            // development: behave as if the card was charged; the caller then pays from the wallet
            p.Status = "paid";
            p.PaidAt = Now;
            money.Add(u.Id, amount, TxKinds.TopUp, "دفع بالبطاقة (محاكاة)", p.Id);
            await db.SaveChangesAsync(ct);
            return new CardCheckout(null, true);
        }
        await db.SaveChangesAsync(ct);
        var (providerId, url) = await gateway.CreateCheckoutAsync(p, Describe(purpose), callbackUrl, ct);
        p.ProviderId = providerId;
        await db.SaveChangesAsync(ct);
        return new CardCheckout(url, false);
    }

    public async Task<WalletDto> TopUpAsync(decimal amount, string callbackUrl, CancellationToken ct)
    {
        var r = await StartAsync(PaymentPurposes.TopUp, null, amount, callbackUrl, ct);
        if (r.Url != null) throw new DomainException("أكمل الدفع في صفحة البطاقة");
        return await wallet.WalletAsync(ct);
    }

    /// <summary>Moyasar webhook (event payment_paid): credits the wallet once, then completes the purpose.</summary>
    public async Task HandleWebhookAsync(JsonElement body, CancellationToken ct)
    {
        var secret = opt.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret) || !body.TryGetProperty("secret_token", out var tok) || tok.GetString() != secret) throw DomainException.Forbidden("forbidden");
        if (!body.TryGetProperty("type", out var type) || type.GetString() != "payment_paid") return;
        var data = body.GetProperty("data");
        if (!data.TryGetProperty("metadata", out var meta) || !meta.TryGetProperty("payment_id", out var pidEl) || !Guid.TryParse(pidEl.GetString(), out var pid)) return;
        var paidHalalas = data.TryGetProperty("amount", out var am) ? am.GetInt64() : 0;

        Payment p;
        await using (var uow = await db.BeginAsync([$"pay:{pid}"], ct))
        {
            p = await db.Payments.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("payment");
            if (p.Status == "paid") return; // already handled (Moyasar retries)
            if (paidHalalas != (long)Math.Round(p.Amount * 100)) { log.LogWarning("Payment {Id}: amount mismatch", pid); return; }
            p.Status = "paid";
            p.PaidAt = Now;
            money.Add(p.UserId, p.Amount, TxKinds.TopUp, "دفع بالبطاقة عبر ميسّر", p.Id);
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
        }
        if (p.Purpose == PaymentPurposes.TopUp || p.Ref is not { } reference) return;
        var user = await db.Users.FirstAsync(x => x.Id == p.UserId, ct);
        access.ActAs(user);
        try
        {
            switch (p.Purpose)
            {
                case PaymentPurposes.Deposit: await requests.PayDepositAsync(reference, ct); break;
                case PaymentPurposes.Invoice: await wallet.PayInvoiceAsync(reference, ct); break;
                case PaymentPurposes.BidDeposit: await bidding.PayBidDepositAsync(reference, ct); break;
                case PaymentPurposes.Listing: await listings.BuyAsync(reference, ct); break;
            }
        }
        catch (DomainException e)
        {
            // the money stays in the wallet; the owner can retry from there
            log.LogWarning("Payment {Id} credited but {Purpose} not completed: {Reason}", pid, p.Purpose, e.Message);
        }
    }

    private static string Describe(string purpose) => purpose switch
    {
        PaymentPurposes.Deposit => "ضمان مزاد — منصة سمّها",
        PaymentPurposes.Invoice => "سداد فاتورة — منصة سمّها",
        PaymentPurposes.BidDeposit => "ضمان دخول مزاد — منصة سمّها",
        PaymentPurposes.Listing => "شراء اسم — منصة سمّها",
        _ => "شحن محفظة — منصة سمّها",
    };
}
