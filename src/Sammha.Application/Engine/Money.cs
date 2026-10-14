using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Engine;

/// <summary>Wallet movements, invoices and payouts. Callers hold the user's lock ("u:{id}").</summary>
public class Money(IAppDbContext db, TimeProvider clock, Notifier notifier, IOptions<PlatformOptions> rules, IOptions<IntegrationOptions> integrations,
    IPostCommit postCommit, IWebhookPublisher webhooks)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public WalletTx Add(Guid userId, decimal amount, string kind, string note, Guid? reference = null)
    {
        var t = new WalletTx { UserId = userId, Amount = amount, Kind = kind, Note = note, Ref = reference, CreatedAt = Now };
        db.WalletTxs.Add(t);
        return t;
    }

    public const string CardOnly = "تُسدَّد هذه العملية من قنوات الدفع (البطاقة) فقط، ورصيد المحفظة مخصص لسداد الضمانات";

    /// <summary>
    /// Invoices, purchases and fees are paid through the payment channels only: the wallet is for guarantees.
    /// A card payment passes through the wallet (credit then debit), so this spends one paid card payment made for exactly this purpose.
    /// </summary>
    /// <returns>the card payment that was spent (the receipt)</returns>
    public async Task<Payment> DebitByCardAsync(Guid userId, decimal amount, string purpose, Guid reference, string kind, string note, CancellationToken ct)
    {
        var p = await db.Payments.Where(x => x.UserId == userId && x.Purpose == purpose && x.Ref == reference && x.Status == "paid" && x.UsedAt == null && x.Amount >= amount)
            .OrderBy(x => x.PaidAt).FirstOrDefaultAsync(ct) ?? throw new DomainException(CardOnly);
        // the card money sits in the wallet between the credit and this debit: if it was spent meanwhile (e.g. on a guarantee), the purpose cannot complete
        if (await db.BalanceOfAsync(userId, ct) < amount)
            throw new DomainException("مبلغ الدفع بالبطاقة لم يعد متاحًا في محفظتك (استُخدم في عملية أخرى)، ادفع من جديد");
        p.UsedAt = Now;
        Add(userId, -amount, kind, note, reference);
        return p;
    }

    public async Task DebitAsync(Guid userId, decimal amount, string kind, string note, Guid? reference, CancellationToken ct, string? lowMessage = null)
    {
        if (await db.BalanceOfAsync(userId, ct) < amount) throw new DomainException(lowMessage ?? "رصيد المحفظة لا يكفي، اشحن المحفظة أو ادفع بالبطاقة");
        Add(userId, -amount, kind, note, reference);
    }

    public async Task<Invoice> IssueInvoiceAsync(Guid buyerId, Guid? auctionId, Guid? listingId, string name, string type, decimal price, decimal depositApplied, CancellationToken ct)
    {
        var vat = Round2(price * rules.Value.VatRate / (1 + rules.Value.VatRate));
        var paidNow = price - depositApplied <= 0;
        // ZATCA: a tax invoice is issued when it is paid; until then it is a payment request
        var pr = paidNow ? null : $"PR-{Now.Year}-{await db.NextNumberAsync("PayRequestNo", ct):D5}";
        var inv = new Invoice
        {
            Number = pr ?? await TaxNumberAsync(ct), RequestNumber = pr, UserId = buyerId, AuctionId = auctionId, ListingId = listingId, Name = name, Type = type,
            Total = price, Vat = vat, Subtotal = Round2(price - vat), DepositApplied = depositApplied, AmountDue = Round2(price - depositApplied),
            Status = price - depositApplied <= 0 ? InvoiceStatuses.Paid : InvoiceStatuses.Unpaid, IssuedAt = Now,
        };
        if (inv.Status == InvoiceStatuses.Paid) inv.PaidAt = Now;
        db.Invoices.Add(inv);
        if (paidNow) await PublishTaxInvoiceAsync(inv, ct);
        return inv;
    }

    private async Task<string> TaxNumberAsync(CancellationToken ct) => $"INV-{Now.Year}-{await db.NextNumberAsync("InvoiceNo", ct):D5}";

    /// <summary>the tax invoice exists from here: its number, its date, and the accounting webhook</summary>
    private async Task PublishTaxInvoiceAsync(Invoice inv, CancellationToken ct)
    {
        var url = integrations.Value.InvoiceWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        var buyer = await db.Users.FindAsync([inv.UserId], ct);
        var payload = new { inv.Number, inv.Name, inv.Type, inv.Total, inv.Vat, inv.AmountDue, inv.IssuedAt, buyer = buyer?.FullName, email = buyer?.Email, phone = buyer?.Phone };
        postCommit.Enqueue(() => webhooks.PublishAsync(url, payload));
    }

    /// <summary>The name owner's share (investment 30%, or a seller's price after commission) reaches their wallet once the invoice is paid.</summary>
    public async Task PayOwnerShareAsync(Invoice inv, CancellationToken ct)
    {
        if (inv.OwnerShare is not { } share || inv.OwnerShareTo is not { } to || inv.OwnerSharePaid) return;
        inv.OwnerSharePaid = true;
        var sale = inv.ListingId != null;
        Add(to, share, sale ? TxKinds.SalePayout : TxKinds.InvestmentPayout,
            sale ? $"ثمن بيع الاسم «{inv.Name}» بعد عمولة سمّها ({inv.Number})" : $"نصيبك {Pct(rules.Value.InvestOwnerShare)}% من بيع الاسم «{inv.Name}» ({inv.Number})", inv.Id);
        await notifier.NotifyAsync(to, NotificationKinds.Refund, $"أُضيف {share:0.##} ريال إلى محفظتك",
            sale ? $"ثمن بيع اسمك «{inv.Name}» بعد خصم عمولة سمّها." : $"نصيبك من بيع الاسم «{inv.Name}» في المزاد.", "/dashboard/wallet", ct);
        if (sale)
        {
            var l = await db.Listings.FindAsync([inv.ListingId!.Value], ct);
            if (l != null) l.PayoutPaid = true;
        }
    }

    /// <summary>
    /// Takes more locks inside the caller's transaction (sp_getapplock is re-entrant, and the outer unit of work commits).
    /// Without an open transaction it does nothing: a unit of work of its own would roll back when disposed here.
    /// </summary>
    public async Task<IUnitOfWork?> JoinLocksAsync(IEnumerable<string> keys, CancellationToken ct) =>
        db is DbContext ctx && ctx.Database.CurrentTransaction != null ? await db.BeginAsync(keys, ct) : null;

    /// <summary>
    /// Idempotent: an invoice already paid is left as it is. Callers may hold only the invoice's lock (staff marking it paid),
    /// so the buyer's wallet lock (and a resold name's) is taken here and the stored status is read again under them.
    /// </summary>
    public async Task MarkInvoicePaidAsync(Invoice inv, CancellationToken ct)
    {
        if (inv.Status == InvoiceStatuses.Paid) return;
        var reservedId = inv.ListingId is { } lid ? await db.Listings.AsNoTracking().Where(l => l.Id == lid).Select(l => (Guid?)l.ReservedId).FirstOrDefaultAsync(ct) : null;
        var keys = new List<string> { $"i:{inv.Id}", $"u:{inv.UserId}" };
        if (reservedId is { } nid) keys.Add($"n:{nid}");
        await using var locks = await JoinLocksAsync(keys, ct);
        var stored = await db.Invoices.AsNoTracking().Where(i => i.Id == inv.Id).Select(i => i.Status).FirstOrDefaultAsync(ct);
        // paid by someone else since the caller loaded it: fail, so the caller's own debit rolls back
        if (stored == InvoiceStatuses.Paid) throw new DomainException("سُدّدت هذه الفاتورة مسبقًا");
        if (inv.Status == InvoiceStatuses.Cancelled || stored == InvoiceStatuses.Cancelled) throw new DomainException("أُلغيت هذه الفاتورة لانتهاء مهلة السداد");
        inv.Status = InvoiceStatuses.Paid;
        inv.PaidAt = Now;
        if (!inv.Number.StartsWith("INV-"))
        {
            inv.RequestNumber ??= inv.Number;
            inv.Number = await TaxNumberAsync(ct);
            inv.IssuedAt = Now;
            await PublishTaxInvoiceAsync(inv, ct);
        }
        if (inv.ListingId != null) await HandOverResoldNameAsync(inv, ct);
        await PayOwnerShareAsync(inv, ct);
        await ReleaseWinnerGuaranteeAsync(inv, ct);
    }

    /// <summary>a resale auction's name stays with the seller until the winner pays; it moves to the buyer here (an instant sale moved it already)</summary>
    private async Task HandOverResoldNameAsync(Invoice inv, CancellationToken ct)
    {
        var l = await db.Listings.FirstOrDefaultAsync(x => x.Id == inv.ListingId, ct);
        if (l == null) return;
        var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == l.ReservedId, ct) ?? throw new DomainException("الاسم لم يعد في السجل، تواصل مع الدعم");
        if (r.OwnerId == inv.UserId) return;
        if (r.OwnerId != l.SellerId) throw new DomainException("لم يعد الاسم مسجلًا باسم البائع، تواصل مع الدعم");
        var buyer = await db.Users.FirstAsync(x => x.Id == inv.UserId, ct);
        r.OwnerId = buyer.Id;
        r.OwnerName = buyer.FullName;
        r.Price = inv.Total;
        r.AcquiredAt = Now;
        r.Source = ReservedSources.Resale;
        await Features.Camels.CamelLinks.ClearAsync(db, r.Id, ct);
        await notifier.NotifyAsync(buyer.Id, NotificationKinds.Won, $"أصبح «{inv.Name}» باسمك", "سُدّدت الفاتورة وانتقلت ملكية الاسم إليك، ويمكنك إصدار شهادة التسجيل الآن.", "/dashboard/names", ct);
    }

    /// <summary>a paid auction win returns the winner's guarantee: the request guarantee (the requester won) or the entry guarantee</summary>
    private async Task ReleaseWinnerGuaranteeAsync(Invoice inv, CancellationToken ct)
    {
        if (inv.AuctionId is not { } aid || inv.ListingId != null) return;
        var a = await db.Auctions.FindAsync([aid], ct);
        if (a == null) return;
        if (a.RequestId is { } rid && a.IcoReservedId == null)
        {
            var r = await db.Requests.FindAsync([rid], ct);
            if (r != null && r.UserId == inv.UserId && r.DepositState == DepositStates.Held) await RefundDepositAsync(r, "سُدّدت فاتورة الفوز", ct);
        }
        var dep = await db.BidDeposits.FirstOrDefaultAsync(d => d.AuctionId == aid && d.UserId == inv.UserId && d.State == BidDepositStates.Held, ct);
        if (dep != null)
        {
            dep.State = BidDepositStates.Refunded;
            Add(dep.UserId, dep.Amount, TxKinds.Refund, $"استرداد ضمان دخول مزاد «{a.Name}» بعد سداد الفاتورة {inv.Number}", a.Id);
            await notifier.NotifyAsync(dep.UserId, NotificationKinds.Refund, "أُعيد ضمان دخول المزاد إلى محفظتك", $"{dep.Amount:0.##} ريال بعد سداد فاتورة «{a.Name}».", "/dashboard/wallet", ct);
        }
    }

    /// <summary>Returns the guarantee of a request to the owner's wallet (only while it is held).</summary>
    public async Task RefundDepositAsync(LogoRequest r, string why, CancellationToken ct)
    {
        if (r.DepositState != DepositStates.Held) return;
        r.DepositState = DepositStates.Refunded;
        Add(r.UserId, r.DepositAmount, TxKinds.Refund, $"استرداد ضمان الطلب {r.Number} ({why})", r.Id);
        await notifier.NotifyAsync(r.UserId, NotificationKinds.Refund, "أُعيد ضمان المزاد إلى محفظتك", $"{r.DepositAmount:0.##} ريال من الطلب {r.Number} ({why}).", "/dashboard/wallet", ct);
    }

    public static int Pct(decimal rate) => (int)Math.Round(rate * 100);
}
