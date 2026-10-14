using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Engine;

/// <summary>Bidding (with anti-sniping and proxy bids) and settlement of ended auctions. Callers hold the auction lock.</summary>
public class AuctionEngine(IAppDbContext db, TimeProvider clock, Notifier notifier, Money money, IOptions<PlatformOptions> rules)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private PlatformOptions R => rules.Value;

    /// <summary>One bid: price, leader, extension in the last minutes, outbid notice.</summary>
    public async Task RecordBidAsync(Auction a, Guid userId, decimal amount, bool auto, CancellationToken ct)
    {
        if (a.LeaderId is { } prev && prev != userId)
            await notifier.NotifyAsync(prev, NotificationKinds.Outbid, $"تجاوز مزايد آخر مزايدتك على «{a.Name}»", $"أعلى مزايدة الآن {amount:0.##} ريال. زايد قبل انتهاء الوقت.", $"/auctions/{a.Id}", ct);
        var t = Now;
        db.Bids.Add(new Bid { AuctionId = a.Id, UserId = userId, Amount = amount, CreatedAt = t, Auto = auto });
        a.CurrentPrice = amount;
        a.BidCount += 1;
        a.LeaderId = userId;
        var window = TimeSpan.FromMinutes(R.ExtendMinutes);
        if (a.EndsAt - t < window) { a.EndsAt = t + window; a.Extensions += 1; }
    }

    /// <summary>Proxy bidding: each auto-bidder is raised by the minimum step up to their ceiling; the earlier ceiling wins ties.</summary>
    public async Task ResolveAutoAsync(Auction a, CancellationToken ct)
    {
        var list = await db.AutoBids.Where(x => x.AuctionId == a.Id).ToListAsync(ct);
        for (var guard = 0; guard < 20; guard++)
        {
            if (a.PausedAt != null || a.PhaseAt(Now) != "live") return;
            var inc = a.MinIncrement;
            var challenger = list.Where(x => x.UserId != a.LeaderId && x.Max >= a.CurrentPrice + inc)
                .OrderByDescending(x => x.Max).ThenBy(x => x.CreatedAt).FirstOrDefault();
            if (challenger == null) return;
            var leaderAuto = a.LeaderId is { } lid ? list.FirstOrDefault(x => x.UserId == lid) : null;
            var leaderMax = Math.Max(leaderAuto?.Max ?? 0, a.CurrentPrice);
            if (challenger.Max > leaderMax)
            {
                if (leaderAuto != null && leaderAuto.Max > a.CurrentPrice) await RecordBidAsync(a, a.LeaderId!.Value, leaderAuto.Max, true, ct);
                await RecordBidAsync(a, challenger.UserId, Math.Min(challenger.Max, leaderMax + inc), true, ct);
            }
            else
            {
                var leader = a.LeaderId!.Value;
                await RecordBidAsync(a, challenger.UserId, challenger.Max, true, ct);
                await RecordBidAsync(a, leader, Math.Min(leaderMax, challenger.Max + inc), true, ct);
            }
        }
    }

    public async Task SettleAsync(Auction a, CancellationToken ct)
    {
        if (a.Settled || a.PausedAt != null || a.CancelledAt != null || a.PhaseAt(Now) != "ended") return;
        if (a.ListingId != null) { await SettleResaleAsync(a, ct); return; }
        var r = await db.Requests.Include(x => x.Candidates).FirstAsync(x => x.Id == a.RequestId, ct);
        if (r.Kind == RequestKinds.Investment) { await SettleInvestmentAsync(a, r, ct); return; }

        var noBids = a.BidCount == 0 || a.LeaderId == null;
        var winnerId = noBids ? a.RequesterId : a.LeaderId!.Value;
        var winner = await db.Users.FirstAsync(u => u.Id == winnerId, ct);
        var price = noBids ? a.OpeningPrice : a.CurrentPrice;
        // the guarantee is never deducted from the invoice: the winner pays the full price (wallet or card), and the guarantee returns to the wallet
        await money.IssueInvoiceAsync(winner.Id, a.Id, null, a.Name, a.Type, price, 0, ct);
        Register(a, winner, price, ReservedSources.Auction);
        Close(a, winner.Id, price, winnerId == r.UserId);
        await notifier.NotifyAsync(winnerId, NotificationKinds.Won, $"مبارك عليك الفوز بمزاد شعار «{a.Name}»", $"بسعر {price:0.##} ريال. فاتورتك الإلكترونية جاهزة، سدّدها لإصدار شهادة التسجيل.", "/dashboard/invoices", ct);
        await NotifyLosersAsync(a, winnerId, price, ct);
        // every auction of the request settled → the guarantee returns to the requester's wallet, whoever won
        var others = await db.Auctions.Where(x => x.RequestId == r.Id && x.Id != a.Id).Select(x => x.Settled).ToListAsync(ct);
        var pendingNames = r.Candidates.Any(c => c.Status == CandidateStatuses.Accepted && c.AuctionId == null && c.ExceptedAt == null);
        if (!pendingNames && others.All(s => s) && r.DepositState == DepositStates.Held)
            await money.RefundDepositAsync(r, winnerId == r.UserId ? "انتهى المزاد ورسا عليك الاسم" : "لم يُرسَ عليك أي اسم", ct);
    }

    /// <summary>«استثمار الاسم»: sold → invoice to the winner and 30% (before VAT) earmarked for the proposer; no bids → the name goes to the proposer free.</summary>
    private async Task SettleInvestmentAsync(Auction a, LogoRequest r, CancellationToken ct)
    {
        var owner = await db.Users.FirstAsync(u => u.Id == r.UserId, ct);
        if (a.BidCount == 0 || a.LeaderId == null)
        {
            Register(a, owner, 0, ReservedSources.Investment);
            Close(a, owner.Id, 0, true);
            await notifier.NotifyAsync(owner.Id, NotificationKinds.Won, $"انتهى مزاد «{a.Name}» دون مزايدات", $"رسا الاسم عليك بلا مقابل، ولا تُستحق نسبة الـ{Money.Pct(R.InvestOwnerShare)}% لعدم وجود مزايدة.", "/dashboard/names", ct);
            return;
        }
        var winner = await db.Users.FirstAsync(u => u.Id == a.LeaderId, ct);
        var price = a.CurrentPrice;
        var inv = await money.IssueInvoiceAsync(winner.Id, a.Id, null, a.Name, a.Type, price, 0, ct);
        inv.OwnerShare = Money.Round2((price - inv.Vat) * R.InvestOwnerShare);
        inv.OwnerShareTo = owner.Id;
        Register(a, winner, price, ReservedSources.Investment);
        Close(a, winner.Id, price, false);
        await notifier.NotifyAsync(winner.Id, NotificationKinds.Won, $"مبارك عليك الفوز بمزاد شعار «{a.Name}»", $"بسعر {price:0.##} ريال. فاتورتك الإلكترونية جاهزة، سدّدها لإصدار شهادة التسجيل.", "/dashboard/invoices", ct);
        await notifier.NotifyAsync(owner.Id, NotificationKinds.Won, $"بيع اسمك «{a.Name}» في المزاد", $"بسعر {price:0.##} ريال. نصيبك {inv.OwnerShare:0.##} ريال ({Money.Pct(R.InvestOwnerShare)}%) يُضاف إلى محفظتك فور سداد الفائز.", "/dashboard/invest", ct);
        await NotifyLosersAsync(a, winner.Id, price, ct);
    }

    /// <summary>«بيع اسم» resale: the winner's participation guarantee is applied, the others refunded; the seller is paid on the invoice.</summary>
    private async Task SettleResaleAsync(Auction a, CancellationToken ct)
    {
        var l = await db.Listings.FirstAsync(x => x.Id == a.ListingId, ct);
        var deposits = await db.BidDeposits.Where(d => d.AuctionId == a.Id && d.State == BidDepositStates.Held).ToListAsync(ct);
        if (a.BidCount == 0 || a.LeaderId == null)
        {
            Close(a, l.SellerId, 0, true);
            l.Status = ListingStatuses.Unsold;
            await RefundBidDepositsAsync(a, deposits, null, ct);
            await notifier.NotifyAsync(l.SellerId, NotificationKinds.Lost, $"انتهى مزاد «{a.Name}» دون مزايدات", "بقي الاسم باسمك. تقدر تعرضه من جديد بسعر آخر.", "/dashboard/sell", ct);
            return;
        }
        var winner = await db.Users.FirstAsync(u => u.Id == a.LeaderId, ct);
        var price = a.CurrentPrice;
        var dep = deposits.FirstOrDefault(d => d.UserId == winner.Id);
        if (dep != null) dep.State = BidDepositStates.Applied;
        await RefundBidDepositsAsync(a, deposits, winner.Id, ct);
        Close(a, winner.Id, price, false);
        var inv = await money.IssueInvoiceAsync(winner.Id, a.Id, l.Id, a.Name, a.Type, price, Math.Min(dep?.Amount ?? 0, price), ct);
        inv.OwnerShare = Money.Round2(price * (1 - l.CommissionRate));
        inv.OwnerShareTo = l.SellerId;
        l.Payout = inv.OwnerShare;
        l.Status = ListingStatuses.Sold;
        l.SoldPrice = price;
        l.SoldAt = Now;
        await TransferAsync(l, winner, price, ct);
        await notifier.NotifyAsync(winner.Id, NotificationKinds.Won, $"مبارك عليك الفوز بمزاد شعار «{a.Name}»", $"بسعر {price:0.##} ريال، وخُصم منه ضمان الدخول. سدّد الفاتورة لإصدار شهادة الاسم.", "/dashboard/invoices", ct);
        await notifier.NotifyAsync(l.SellerId, NotificationKinds.Won, $"بيع اسمك «{a.Name}» في المزاد", $"بسعر {price:0.##} ريال. يصلك {inv.OwnerShare:0.##} ريال بعد عمولة سمّها ({Money.Pct(l.CommissionRate)}%) فور سداد المشتري.", "/dashboard/sell", ct);
        await NotifyLosersAsync(a, winner.Id, price, ct);
    }

    private async Task RefundBidDepositsAsync(Auction a, List<BidDeposit> deposits, Guid? except, CancellationToken ct)
    {
        foreach (var d in deposits.Where(d => d.UserId != except && d.State == BidDepositStates.Held))
        {
            d.State = BidDepositStates.Refunded;
            money.Add(d.UserId, d.Amount, TxKinds.Refund, $"استرداد ضمان دخول مزاد «{a.Name}»", a.Id);
            await notifier.NotifyAsync(d.UserId, NotificationKinds.Refund, "أُعيد ضمان دخول المزاد إلى محفظتك", $"{d.Amount:0.##} ريال من مزاد «{a.Name}».", "/dashboard/wallet", ct);
        }
    }

    public async Task TransferAsync(NameListing l, User buyer, decimal price, CancellationToken ct)
    {
        var r = await db.ReservedNames.FirstAsync(x => x.Id == l.ReservedId, ct);
        r.OwnerId = buyer.Id;
        r.OwnerName = buyer.FullName;
        r.Price = price;
        r.AcquiredAt = Now;
        r.Source = ReservedSources.Resale;
    }

    private void Register(Auction a, User owner, decimal price, string source)
    {
        var id = Guid.NewGuid();
        db.ReservedNames.Add(new ReservedName
        {
            Id = id, Name = a.Name, Normalized = NameMatcher.Normalize(a.Name), Type = a.Type, OwnerId = owner.Id, OwnerName = owner.FullName,
            Price = price, AcquiredAt = a.EndsAt, Source = source, AuctionId = a.Id, CertCode = ReservedName.CertFor(id),
        });
    }

    private static void Close(Auction a, Guid winnerId, decimal price, bool byRequester)
    {
        a.Settled = true;
        a.WinnerId = winnerId;
        a.FinalPrice = price;
        a.WonByRequester = byRequester;
    }

    /// <summary>
    /// The administration cancels an auction: closed with no sale, automatic bids removed, bidders told.
    /// A request's name goes back to the committee's scheduling list (its guarantee stays held); a resale listing is withdrawn,
    /// the name stays with its owner and every participation guarantee is refunded.
    /// </summary>
    public async Task CancelAsync(Auction a, string reason, CancellationToken ct)
    {
        a.CancelledAt = Now;
        a.CancelReason = reason;
        a.PausedAt = null;
        a.Settled = true;
        db.AutoBids.RemoveRange(await db.AutoBids.Where(x => x.AuctionId == a.Id).ToListAsync(ct));
        var bidders = await db.Bids.Where(b => b.AuctionId == a.Id).Select(b => b.UserId).Distinct().ToListAsync(ct);
        Guid? owner = null;
        if (a.ListingId != null)
        {
            var l = await db.Listings.FirstAsync(x => x.Id == a.ListingId, ct);
            l.Status = ListingStatuses.Cancelled;
            owner = l.SellerId;
            var deposits = await db.BidDeposits.Where(d => d.AuctionId == a.Id && d.State == BidDepositStates.Held).ToListAsync(ct);
            await RefundBidDepositsAsync(a, deposits, null, ct);
            await notifier.NotifyAsync(l.SellerId, NotificationKinds.AuctionUpdate, $"أُلغي مزاد «{a.Name}»", $"بقي الاسم باسمك ويمكنك عرضه من جديد. السبب: {reason}", "/dashboard/sell", ct);
        }
        else if (a.CandidateId != null)
        {
            var c = await db.Candidates.FirstOrDefaultAsync(x => x.Id == a.CandidateId, ct);
            if (c != null && c.AuctionId == a.Id) c.AuctionId = null; // back to «فتح المزادات»
            owner = a.RequesterId;
            await notifier.NotifyAsync(a.RequesterId, NotificationKinds.AuctionUpdate, $"أُلغي مزاد «{a.Name}»",
                $"يعود الاسم إلى قائمة الجدولة وتحدد اللجنة موعدًا جديدًا لمزاده. السبب: {reason}", "/dashboard/requests", ct);
        }
        foreach (var u in bidders.Where(u => u != owner))
            await notifier.NotifyAsync(u, NotificationKinds.AuctionUpdate, $"أُلغي مزاد «{a.Name}»", $"لم تُحتسب المزايدات على هذا المزاد. السبب: {reason}", $"/auctions/{a.Id}", ct);
    }

    private async Task NotifyLosersAsync(Auction a, Guid winnerId, decimal price, CancellationToken ct)
    {
        var losers = await db.Bids.Where(b => b.AuctionId == a.Id && b.UserId != winnerId).Select(b => b.UserId).Distinct().ToListAsync(ct);
        foreach (var u in losers)
            await notifier.NotifyAsync(u, NotificationKinds.Lost, $"انتهى مزاد «{a.Name}»", $"رسا الاسم بسعر {price:0.##} ريال.", $"/auctions/{a.Id}", ct);
    }
}
