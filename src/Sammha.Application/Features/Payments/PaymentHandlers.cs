using System.Text.Json;
using MediatR;
using Sammha.Application.Features.Bidding;
using Sammha.Application.Features.Listings;
using Sammha.Application.Features.Requests;
using Sammha.Application.Features.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Payments;

/// <summary>
/// Card payments (mada, Visa, Mastercard, Apple Pay) through Moyasar. A paid card payment tops the wallet up,
/// then completes what it was for (guarantee, invoice, participation guarantee or purchase) from the wallet.
/// With Payments:Simulate the payment succeeds at once (development).
/// </summary>
public class PaymentHandlers(IAppDbContext db, TimeProvider clock, Access access, Money money, IPaymentGateway gateway, IOptions<PaymentOptions> opt,
    ISender sender, ILogger<PaymentHandlers> log)
    : IRequestHandler<StartCardPaymentCommand, CardCheckout>, IRequestHandler<TopUpCommand, WalletDto>, IRequestHandler<MoyasarWebhookCommand, Unit>,
      IRequestHandler<CompleteHyperPayCommand, HyperPayResult>, IRequestHandler<GetHyperPayFormQuery, HyperPayFormDto?>
{
    public Task<CardCheckout> Handle(StartCardPaymentCommand c, CancellationToken ct) => StartAsync(c.Purpose, c.Ref, c.Amount, c.CallbackUrl, ct, c.Method);
    public Task<WalletDto> Handle(TopUpCommand c, CancellationToken ct) => TopUpAsync(c.Amount, c.CallbackUrl, ct);
    public async Task<Unit> Handle(MoyasarWebhookCommand c, CancellationToken ct) { await HandleWebhookAsync(c.Body, ct); return Unit.Value; }

    private DateTimeOffset Now => clock.GetUtcNow();

    private async Task<CardCheckout> StartAsync(string purpose, Guid? reference, decimal amount, string callbackUrl, CancellationToken ct, string? method = null)
    {
        var u = await access.MeAsync(ct);
        if (!PaymentPurposes.All.Contains(purpose)) throw new DomainException("عملية دفع غير معروفة");
        // the channel: one the gateway has (mada, card, applepay); the simulator takes any
        method = string.IsNullOrWhiteSpace(method) ? null : method.Trim().ToLowerInvariant();
        if (method != null && !PayChannels.All.Contains(method)) throw new DomainException("وسيلة الدفع غير معروفة");
        if (!opt.Value.Simulate)
        {
            var channels = gateway.Channels;
            if (channels.Count == 0) throw new DomainException("الدفع الإلكتروني غير مفعّل حاليًا، تواصل مع الدعم");
            method ??= channels.Contains(PayChannels.Mada) ? PayChannels.Mada : channels[0];
            if (!channels.Contains(method)) throw new DomainException("وسيلة الدفع هذه غير مفعّلة حاليًا");
        }
        // never trust the browser's amount: read it from the database
        amount = purpose switch
        {
            PaymentPurposes.Deposit => await db.Requests.Where(r => r.Id == reference && r.UserId == u.Id && r.Status == RequestStatuses.PendingPayment).Select(r => (decimal?)r.DepositAmount).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("الطلب غير متاح للسداد"),
            PaymentPurposes.Invoice => await db.Invoices.Where(i => i.Id == reference && i.UserId == u.Id && i.Status == InvoiceStatuses.Unpaid).Select(i => (decimal?)i.AmountDue).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("الفاتورة غير متاحة للسداد"),
            PaymentPurposes.BidDeposit => await db.Auctions.Where(a => a.Id == reference && (a.ListingId != null || a.BidDeposit > 0) && !a.Settled && a.RequesterId != u.Id).Select(a => a.BidDeposit).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("المزاد غير متاح"),
            PaymentPurposes.Listing => await db.Listings.Where(l => l.Id == reference && l.Mode == ListingModes.Instant && l.Status == ListingStatuses.Live && l.SellerId != u.Id).Select(l => (decimal?)l.Price).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("العرض غير متاح"),
            PaymentPurposes.Transfer => await db.Transfers.Where(t => t.Id == reference && t.FromId == u.Id && t.Status == TransferStatuses.PendingPayment).Select(t => (decimal?)t.Fee).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("طلب النقل غير متاح للسداد"),
            _ => amount >= 10 && amount <= 100_000 ? decimal.Round(amount, 2) : throw new DomainException("أقل مبلغ للشحن 10 ريال"),
        };
        var p = new Payment { UserId = u.Id, Purpose = purpose, Ref = reference, Amount = amount, CreatedAt = Now, Method = method ?? PayChannels.Card,
            CallbackUrl = callbackUrl is { Length: > 0 and <= 1000 } ? callbackUrl : null };
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

    private async Task<WalletDto> TopUpAsync(decimal amount, string callbackUrl, CancellationToken ct)
    {
        var r = await StartAsync(PaymentPurposes.TopUp, null, amount, callbackUrl, ct);
        if (r.Url != null) throw new DomainException("أكمل الدفع في صفحة البطاقة");
        return await sender.Send(new GetWalletQuery(), ct);
    }

    /// <summary>Moyasar webhook (event payment_paid): credits the wallet once, then completes the purpose.</summary>
    private async Task HandleWebhookAsync(JsonElement body, CancellationToken ct)
    {
        var secret = opt.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret) || !body.TryGetProperty("secret_token", out var tok) || tok.GetString() != secret) throw DomainException.Forbidden("forbidden");
        if (!body.TryGetProperty("type", out var type) || type.GetString() != "payment_paid") return;
        var data = body.GetProperty("data");
        if (!data.TryGetProperty("metadata", out var meta) || !meta.TryGetProperty("payment_id", out var pidEl) || !Guid.TryParse(pidEl.GetString(), out var pid)) return;
        var paidHalalas = data.TryGetProperty("amount", out var am) ? am.GetInt64() : 0;

        await CreditAsync(pid, paidHalalas / 100m, "دفع بالبطاقة عبر ميسّر", null, ct);
    }

    /// <summary>marks the payment paid once (providers retry), credits the wallet, then completes what it was for</summary>
    private async Task<bool> CreditAsync(Guid pid, decimal paid, string note, string? code, CancellationToken ct)
    {
        Payment p;
        await using (var uow = await db.BeginAsync([$"pay:{pid}"], ct))
        {
            p = await db.Payments.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("payment");
            if (p.Status == "paid") return true;
            if (Math.Round(paid, 2) != Math.Round(p.Amount, 2)) { log.LogWarning("Payment {Id}: amount mismatch", pid); return false; }
            p.Status = "paid";
            p.PaidAt = Now;
            p.ResultCode = code;
            money.Add(p.UserId, p.Amount, TxKinds.TopUp, note, p.Id);
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
        }
        if (p.Purpose == PaymentPurposes.TopUp || p.Ref is not { } reference) return true;
        var user = await db.Users.FirstAsync(x => x.Id == p.UserId, ct);
        access.ActAs(user);
        try
        {
            switch (p.Purpose)
            {
                case PaymentPurposes.Deposit: await sender.Send(new PayDepositCommand(reference), ct); break;
                case PaymentPurposes.Invoice: await sender.Send(new PayInvoiceCommand(reference), ct); break;
                case PaymentPurposes.BidDeposit: await sender.Send(new PayBidDepositCommand(reference), ct); break;
                case PaymentPurposes.Listing: await sender.Send(new BuyListingCommand(reference), ct); break;
                case PaymentPurposes.Transfer: await sender.Send(new Features.Transfers.PayTransferFeeCommand(reference), ct); break;
            }
        }
        catch (DomainException e)
        {
            // the money stays in the wallet; the owner can retry from there
            log.LogWarning("Payment {Id} credited but {Purpose} not completed: {Reason}", pid, p.Purpose, e.Message);
        }
        return true;
    }

    public async Task<HyperPayResult> Handle(CompleteHyperPayCommand c, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.PaymentId, ct);
        if (p == null) return new(false, false, null, "عملية الدفع غير موجودة");
        if (p.Status == "paid") return new(true, false, p.CallbackUrl, null);
        if (!string.IsNullOrEmpty(c.CheckoutId) && c.CheckoutId != p.ProviderId) return new(false, false, p.CallbackUrl, "عملية الدفع غير مطابقة");
        var s = await gateway.StatusAsync(p, ct);
        if (s.Paid)
        {
            var ok = await CreditAsync(p.Id, s.Amount, "دفع إلكتروني عبر هايبر باي", s.Code, ct);
            return new(ok, false, p.CallbackUrl, ok ? null : "المبلغ المدفوع لا يطابق المطلوب، تواصل مع الدعم");
        }
        await using (var uow = await db.BeginAsync([$"pay:{p.Id}"], ct))
        {
            var t = await db.Payments.FirstAsync(x => x.Id == p.Id, ct);
            if (t.Status != "paid") { t.Status = s.Pending ? "pending" : "failed"; t.ResultCode = s.Code; }
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
        }
        log.LogInformation("HyperPay payment {Id}: {Code} {Message}", p.Id, s.Code, s.Message);
        return new(false, s.Pending, p.CallbackUrl, s.Pending ? "الدفع قيد المعالجة لدى البنك، سيصلك إشعار عند اكتماله" : "لم تكتمل عملية الدفع، حاول مرة أخرى");
    }

    public async Task<HyperPayFormDto?> Handle(GetHyperPayFormQuery q, CancellationToken ct) =>
        await db.Payments.AsNoTracking().Where(p => p.Id == q.PaymentId && p.ProviderId != null && (p.Status == "initiated" || p.Status == "failed") && p.CreatedAt > Now.AddMinutes(-30))
            .Select(p => new HyperPayFormDto(p.ProviderId!, p.Integrity, p.Method, p.Amount, p.Purpose, p.CallbackUrl)).FirstOrDefaultAsync(ct);

    private static string Describe(string purpose) => purpose switch
    {
        PaymentPurposes.Deposit => "ضمان مزاد — منصة سمّها",
        PaymentPurposes.Invoice => "سداد فاتورة — منصة سمّها",
        PaymentPurposes.BidDeposit => "ضمان دخول مزاد — منصة سمّها",
        PaymentPurposes.Listing => "شراء اسم — منصة سمّها",
        PaymentPurposes.Transfer => "رسوم نقل ملكية اسم — منصة سمّها",
        _ => "شحن محفظة — منصة سمّها",
    };
}
