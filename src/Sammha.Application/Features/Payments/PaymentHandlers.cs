using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Sammha.Application.Features.Bidding;
using Sammha.Application.Features.Listings;
using Sammha.Application.Features.Requests;
using Sammha.Application.Features.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// <summary>the sweeper's retry of paid card payments whose purpose did not complete (not a Command: kept out of the activity log)</summary>
public record RetryPaidPaymentsRequest : IRequest<int>;

/// <summary>where a payment page may send the owner back to: the platform's own sites only (Cors:Origins)</summary>
public static class CallbackUrls
{
    public static readonly string[] DefaultOrigins = ["https://ibrasalato.github.io", "http://localhost:5173", "http://localhost:4173"];
    public static string[] Origins(IConfiguration cfg) => cfg.GetSection("Cors:Origins").Get<string[]>() ?? DefaultOrigins;
    public static bool Allowed(string? url, IEnumerable<string> origins) =>
        !string.IsNullOrWhiteSpace(url) && url.Length <= 1000 && Uri.TryCreate(url, UriKind.Absolute, out var u)
        && origins.Any(o => Uri.TryCreate(o, UriKind.Absolute, out var ou) && ou.Scheme == u.Scheme && ou.Authority == u.Authority);
}

public class PaymentHandlers(IAppDbContext db, TimeProvider clock, Access access, Money money, IPaymentGateway gateway, IOptions<PaymentOptions> opt,
    ISender sender, ILogger<PaymentHandlers> log, ICurrentUser current, IConfiguration cfg)
    : IRequestHandler<StartCardPaymentCommand, CardCheckout>, IRequestHandler<TopUpCommand, WalletDto>, IRequestHandler<MoyasarWebhookCommand, Unit>,
      IRequestHandler<CompleteHyperPayCommand, HyperPayResult>, IRequestHandler<GetHyperPayFormQuery, HyperPayFormDto?>, IRequestHandler<RetryPaidPaymentsRequest, int>
{
    public Task<CardCheckout> Handle(StartCardPaymentCommand c, CancellationToken ct) => StartAsync(c.Purpose, c.Ref, c.Amount, c.CallbackUrl, ct, c.Method);
    public Task<WalletDto> Handle(TopUpCommand c, CancellationToken ct) => TopUpAsync(c.Amount, c.CallbackUrl, ct);
    public async Task<Unit> Handle(MoyasarWebhookCommand c, CancellationToken ct) { await HandleWebhookAsync(c.Body, ct); return Unit.Value; }

    private DateTimeOffset Now => clock.GetUtcNow();

    private async Task<CardCheckout> StartAsync(string purpose, Guid? reference, decimal amount, string callbackUrl, CancellationToken ct, string? method = null)
    {
        var u = await access.MeAsync(ct);
        if (!PaymentPurposes.All.Contains(purpose)) throw new DomainException("عملية دفع غير معروفة");
        await RequireDelegateScopeAsync(purpose, ct);
        // the payment page sends the owner back here: only to the platform's own sites (the simulator never redirects)
        var callbackOk = CallbackUrls.Allowed(callbackUrl, CallbackUrls.Origins(cfg));
        if (!opt.Value.Simulate && !callbackOk) throw new DomainException("عنوان الرجوع بعد الدفع غير مسموح");
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
            CallbackUrl = callbackOk ? callbackUrl : null };
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

    /// <summary>
    /// A delegate's card payment completes a follow-up command (from the webhook, outside the delegated session), so the scope that command needs
    /// is checked here. Ownership transfer fees are the owner's alone.
    /// </summary>
    private async Task RequireDelegateScopeAsync(string purpose, CancellationToken ct)
    {
        if (current.DelegationId == null) return;
        if (purpose == PaymentPurposes.Transfer) throw DomainException.Forbidden("نقل الملكية لصاحب الحساب فقط");
        var command = purpose switch
        {
            PaymentPurposes.Deposit => nameof(PayDepositCommand),
            PaymentPurposes.Invoice => nameof(PayInvoiceCommand),
            PaymentPurposes.BidDeposit => nameof(PayBidDepositCommand),
            PaymentPurposes.Listing => nameof(BuyListingCommand),
            _ => nameof(TopUpCommand),
        };
        var d = await access.DelegationAsync(ct) ?? throw DomainException.Unauthorized("انتهى التفويض، سجّل الدخول من جديد");
        if (!DelegationScopes.ByCommand.TryGetValue(command, out var scope)) throw DomainException.Forbidden("هذا الإجراء لصاحب الحساب فقط");
        if (!d.ScopeList().Contains(scope)) throw DomainException.Forbidden($"التفويض لا يشمل «{DelegationScopes.Label(scope)}»");
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
        if (string.IsNullOrEmpty(secret) || body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("secret_token", out var tok) || tok.ValueKind != JsonValueKind.String
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tok.GetString() ?? ""), Encoding.UTF8.GetBytes(secret)))
            throw DomainException.Forbidden("forbidden");
        if (!body.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "payment_paid") return;
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return;
        if (Str(data, "status") != "paid" || !string.Equals(Str(data, "currency"), "SAR", StringComparison.OrdinalIgnoreCase)) return;
        if (!data.TryGetProperty("metadata", out var meta) || meta.ValueKind != JsonValueKind.Object || !Guid.TryParse(Str(meta, "payment_id"), out var pid)) return;
        // the checkout is a Moyasar invoice: the paid payment must belong to the invoice made for this payment
        var invoiceId = Str(data, "invoice_id");
        if (string.IsNullOrEmpty(invoiceId)) return;
        var paidHalalas = data.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number && am.TryGetInt64(out var h) ? h : 0;

        await CreditAsync(pid, paidHalalas / 100m, "دفع بالبطاقة عبر ميسّر", null, invoiceId, ct);
    }

    private static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>the locks completing a payment's purpose takes, all taken up front (sorted) so the nested commands never take them in another order</summary>
    private static List<string> LocksFor(Guid pid, Guid userId, string purpose, Guid? reference)
    {
        var keys = new List<string> { $"pay:{pid}", $"u:{userId}" };
        var prefix = purpose switch
        {
            PaymentPurposes.Deposit => "r", PaymentPurposes.Invoice => "i", PaymentPurposes.BidDeposit => "a",
            PaymentPurposes.Listing => "l", PaymentPurposes.Transfer => "t", _ => null,
        };
        if (prefix != null && reference is { } r) keys.Add($"{prefix}:{r}");
        return keys;
    }

    /// <summary>
    /// Marks the payment paid once (providers retry), credits the wallet, then completes what it was for, all in one transaction
    /// under the owner's wallet lock: the credited money cannot be spent elsewhere before the purpose takes it.
    /// </summary>
    private async Task<bool> CreditAsync(Guid pid, decimal paid, string note, string? code, string? providerId, CancellationToken ct)
    {
        var head = await db.Payments.AsNoTracking().Where(x => x.Id == pid).Select(x => new { x.UserId, x.Purpose, x.Ref }).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("payment");
        await using var uow = await db.BeginAsync(LocksFor(pid, head.UserId, head.Purpose, head.Ref), ct);
        var p = await db.Payments.FirstAsync(x => x.Id == pid, ct);
        if (p.Status == "paid") return true;
        if (providerId != null && p.ProviderId != providerId) { log.LogWarning("Payment {Id}: provider reference mismatch", pid); return false; }
        if (Math.Round(paid, 2) != Math.Round(p.Amount, 2)) { log.LogWarning("Payment {Id}: amount mismatch", pid); return false; }
        p.Status = "paid";
        p.PaidAt = Now;
        p.ResultCode = code;
        money.Add(p.UserId, p.Amount, TxKinds.TopUp, note, p.Id);
        await db.SaveChangesAsync(ct);
        await CompleteAsync(p, ct);
        await uow.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Runs the payment's purpose as its owner inside the caller's unit of work (the nested commands join it).
    /// A refusal leaves the money in the wallet: the command's unsaved changes are dropped and the credit is kept.
    /// </summary>
    private async Task<bool> CompleteAsync(Payment p, CancellationToken ct)
    {
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
            // the entry guarantee is paid from the wallet, not by spending the card payment: mark it used so it is not retried
            if (p.Purpose == PaymentPurposes.BidDeposit && p.UsedAt == null)
            {
                p.UsedAt = Now;
                await db.SaveChangesAsync(ct);
            }
            return true;
        }
        catch (DomainException e)
        {
            // the money stays in the wallet; the owner can retry from there (and the sweeper retries for a while)
            log.LogWarning("Payment {Id} credited but {Purpose} not completed: {Reason}", p.Id, p.Purpose, e.Message);
            if (db is DbContext ctx) ctx.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>paid card payments (2 minutes to 2 days old) whose purpose is still open: complete them now; a refusal is logged and left in the wallet</summary>
    public async Task<int> Handle(RetryPaidPaymentsRequest q, CancellationToken ct)
    {
        var now = Now;
        var settled = now.AddMinutes(-2);
        var since = now.AddDays(-2);
        var due = await db.Payments.AsNoTracking()
            .Where(p => p.Status == "paid" && p.UsedAt == null && p.Purpose != PaymentPurposes.TopUp && p.Ref != null && p.PaidAt != null && p.PaidAt <= settled && p.PaidAt > since)
            .Where(p => (p.Purpose == PaymentPurposes.Invoice && db.Invoices.Any(i => i.Id == p.Ref && i.UserId == p.UserId && i.Status == InvoiceStatuses.Unpaid))
                || (p.Purpose == PaymentPurposes.Deposit && db.Requests.Any(r => r.Id == p.Ref && r.UserId == p.UserId && r.Status == RequestStatuses.PendingPayment))
                || (p.Purpose == PaymentPurposes.BidDeposit && db.Auctions.Any(a => a.Id == p.Ref && !a.Settled && a.CancelledAt == null)
                    && !db.BidDeposits.Any(d => d.AuctionId == p.Ref && d.UserId == p.UserId))
                || (p.Purpose == PaymentPurposes.Listing && db.Listings.Any(l => l.Id == p.Ref && l.Status == ListingStatuses.Live))
                || (p.Purpose == PaymentPurposes.Transfer && db.Transfers.Any(t => t.Id == p.Ref && t.FromId == p.UserId && t.Status == TransferStatuses.PendingPayment)))
            .OrderBy(p => p.PaidAt).Select(p => new { p.Id, p.UserId, p.Purpose, p.Ref }).Take(20).ToListAsync(ct);
        var done = 0;
        foreach (var x in due)
        {
            try
            {
                await using var uow = await db.BeginAsync(LocksFor(x.Id, x.UserId, x.Purpose, x.Ref), ct);
                var p = await db.Payments.FirstAsync(y => y.Id == x.Id, ct);
                if (p.Status != "paid" || p.UsedAt != null) continue;
                if (await CompleteAsync(p, ct)) done++;
                await uow.CommitAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Completing paid payment {Id} failed", x.Id); }
        }
        return done;
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
            var ok = await CreditAsync(p.Id, s.Amount, "دفع إلكتروني عبر هايبر باي", s.Code, null, ct);
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
