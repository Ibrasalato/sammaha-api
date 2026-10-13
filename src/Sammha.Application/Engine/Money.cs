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

    public async Task DebitAsync(Guid userId, decimal amount, string kind, string note, Guid? reference, CancellationToken ct, string? lowMessage = null)
    {
        if (await db.BalanceOfAsync(userId, ct) < amount) throw new DomainException(lowMessage ?? "رصيد المحفظة لا يكفي، اشحن المحفظة أو ادفع بالبطاقة");
        Add(userId, -amount, kind, note, reference);
    }

    public async Task<Invoice> IssueInvoiceAsync(Guid buyerId, Guid? auctionId, Guid? listingId, string name, string type, decimal price, decimal depositApplied, CancellationToken ct)
    {
        var vat = Round2(price * rules.Value.VatRate / (1 + rules.Value.VatRate));
        var no = await db.NextNumberAsync("InvoiceNo", ct);
        var inv = new Invoice
        {
            Number = $"INV-{Now.Year}-{no:D5}", UserId = buyerId, AuctionId = auctionId, ListingId = listingId, Name = name, Type = type,
            Total = price, Vat = vat, Subtotal = Round2(price - vat), DepositApplied = depositApplied, AmountDue = Round2(price - depositApplied),
            Status = price - depositApplied <= 0 ? InvoiceStatuses.Paid : InvoiceStatuses.Unpaid, IssuedAt = Now,
        };
        if (inv.Status == InvoiceStatuses.Paid) inv.PaidAt = Now;
        db.Invoices.Add(inv);
        var url = integrations.Value.InvoiceWebhookUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            var buyer = await db.Users.FindAsync([buyerId], ct);
            var payload = new { inv.Number, inv.Name, inv.Type, inv.Total, inv.Vat, inv.AmountDue, inv.IssuedAt, buyer = buyer?.FullName, email = buyer?.Email, phone = buyer?.Phone };
            postCommit.Enqueue(() => webhooks.PublishAsync(url, payload));
        }
        return inv;
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

    public async Task MarkInvoicePaidAsync(Invoice inv, CancellationToken ct)
    {
        inv.Status = InvoiceStatuses.Paid;
        inv.PaidAt = Now;
        await PayOwnerShareAsync(inv, ct);
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
