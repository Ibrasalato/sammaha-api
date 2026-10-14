using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Listings;

/// <summary>
/// «بيع اسم»: an owner sells a name they own.
/// Resale auction: announced 72 h ahead, opens at the owner's minimum, 3 days, bidders pay a guarantee, 10% commission.
/// Instant sale: on the market 72 h at the owner's price, first buyer who pays takes it, 15% commission.
/// </summary>
public class ListingHandlers(IAppDbContext db, TimeProvider clock, Access access, Mapper map, Money money, AuctionEngine engine, Notifier notifier, SettingsStore settings, IOptions<PlatformOptions> rules)
    : IRequestHandler<CreateListingCommand, ListingDto>, IRequestHandler<CancelListingCommand, ListingDto>, IRequestHandler<GetMyListingsQuery, List<ListingDto>>, IRequestHandler<GetMarketQuery, List<ListingDto>>, IRequestHandler<BuyListingCommand, ListingDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private PlatformOptions R => rules.Value;
    private static readonly string[] Active = [ListingStatuses.Scheduled, ListingStatuses.Live];

    private async Task<ListingDto> CreateAsync(Guid reservedId, string mode, decimal price, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (mode is not (ListingModes.Auction or ListingModes.Instant)) throw new DomainException("اختر نوع الطرح");
        await settings.EnsureOpenAsync(mode == ListingModes.Auction ? Intakes.ResaleAuction : Intakes.InstantSale, ct);
        await using var uow = await db.BeginAsync([$"n:{reservedId}"], ct);
        var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == reservedId && x.OwnerId == u.Id, ct) ?? throw new DomainException("الاسم غير مسجل باسمك");
        if (await db.Listings.AnyAsync(l => l.ReservedId == r.Id && Active.Contains(l.Status), ct)) throw new DomainException("الاسم معروض للبيع حاليًا");
        if (await db.Invoices.AnyAsync(i => i.UserId == u.Id && i.Name == r.Name && i.Status == InvoiceStatuses.Unpaid, ct)) throw new DomainException("سدّد فاتورة الاسم أولًا قبل عرضه للبيع");
        if (await db.Auctions.AnyAsync(a => a.Name == r.Name && !a.Settled, ct)) throw new DomainException("للاسم مزاد قائم حاليًا");
        var p = decimal.Floor(price);
        if (p < R.OpeningPrice) throw new DomainException($"أقل قيمة {R.OpeningPrice:0.##} ريال");
        if (p > 10_000_000) throw new DomainException("القيمة كبيرة جدًا");
        var auction = mode == ListingModes.Auction;
        var starts = auction ? Now.AddHours(R.ResaleNoticeHours) : Now;
        var ends = auction ? starts.AddDays(R.ResaleAuctionDays) : Now.AddHours(R.InstantSaleHours);
        var no = await db.NextNumberAsync("ListingNo", ct);
        var l = new NameListing
        {
            Number = $"SL-{no}", ReservedId = r.Id, Name = r.Name, Type = r.Type, SellerId = u.Id, Mode = mode, Price = p,
            CommissionRate = auction ? R.ResaleCommission : R.InstantCommission, Status = auction ? ListingStatuses.Scheduled : ListingStatuses.Live,
            CreatedAt = Now, StartsAt = starts, EndsAt = ends,
        };
        db.Listings.Add(l);
        if (auction)
        {
            var a = new Auction
            {
                Number = $"AU-{await db.NextNumberAsync("AuctionNo", ct)}",
                ListingId = l.Id, Name = l.Name, Type = l.Type, RequesterId = u.Id, OpeningPrice = p, MinIncrement = R.MinIncrement,
                StartsAt = starts, EndsAt = ends, CurrentPrice = p, BidDeposit = R.ResaleBidDeposit,
            };
            db.Auctions.Add(a);
            l.AuctionId = a.Id;
        }
        // announced to every registered owner (forwarded by SMS through the notification webhook)
        var owners = await db.Users.Where(x => x.Role == Roles.Owner && x.Active && x.Id != u.Id).Select(x => x.Id).ToListAsync(ct);
        foreach (var o in owners)
            await notifier.NotifyAsync(o, NotificationKinds.AuctionOpened,
                auction ? $"مزاد إعادة بيع: «{l.Name}»" : $"اسم معروض للبيع الفوري: «{l.Name}»",
                auction ? $"يبدأ بعد {R.ResaleNoticeHours} ساعة بسعر افتتاحي {p:0.##} ريال، ويستمر {R.ResaleAuctionDays} أيام." : $"بسعر {p:0.##} ريال، متاح للشراء {R.InstantSaleHours} ساعة.",
                auction ? $"/auctions/{l.AuctionId}" : "/market", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.ListingAsync(l, ct);
    }

    private async Task<ListingDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"l:{id}"], ct);
        var l = await db.Listings.FirstOrDefaultAsync(x => x.Id == id && x.SellerId == u.Id, ct) ?? throw DomainException.NotFound("العرض غير موجود");
        if (l.Mode == ListingModes.Auction)
        {
            var a = l.AuctionId is { } aid ? await db.Auctions.FirstOrDefaultAsync(x => x.Id == aid, ct) : null;
            if (l.Status != ListingStatuses.Scheduled || a == null || Now >= a.StartsAt) throw new DomainException("بدأ المزاد، فلا يمكن إلغاء العرض");
            await db.Watches.Where(w => w.AuctionId == a.Id).ExecuteDeleteAsync(ct);
            await db.AutoBids.Where(w => w.AuctionId == a.Id).ExecuteDeleteAsync(ct);
            db.Auctions.Remove(a);
            l.AuctionId = null;
        }
        else if (l.Status != ListingStatuses.Live) throw new DomainException("العرض منتهٍ");
        l.Status = ListingStatuses.Cancelled;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.ListingAsync(l, ct);
    }

    private async Task<List<ListingDto>> MineAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.Listings.AsNoTracking().Where(l => l.SellerId == u.Id).OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
        return await map.ListingsAsync(list, ct);
    }

    /// <summary>fixed-price names on sale now, plus those closed in the last 3 days (shown locked)</summary>
    private async Task<List<ListingDto>> MarketAsync(CancellationToken ct)
    {
        var now = Now;
        var since = now.AddDays(-3);
        var list = await db.Listings.AsNoTracking()
            .Where(l => l.Mode == ListingModes.Instant && ((l.Status == ListingStatuses.Live && l.EndsAt > now)
                || ((l.Status == ListingStatuses.Sold || l.Status == ListingStatuses.Expired || l.Status == ListingStatuses.Live) && (l.SoldAt ?? l.EndsAt) > since)))
            .ToListAsync(ct);
        foreach (var l in list.Where(l => l.Status == ListingStatuses.Live && l.EndsAt <= now)) l.Status = ListingStatuses.Expired; // the sweep catches up
        list = list.OrderBy(l => l.Status == ListingStatuses.Live ? 0 : 1).ThenBy(l => l.EndsAt).ToList();
        return await map.ListingsAsync(list, ct);
    }

    private async Task<ListingDto> BuyAsync(Guid id, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"l:{id}", $"u:{u.Id}"], ct);
        var l = await db.Listings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l == null || l.Mode != ListingModes.Instant) throw DomainException.NotFound("العرض غير موجود");
        if (l.Status != ListingStatuses.Live || Now >= l.EndsAt) throw new DomainException("انتهى عرض هذا الاسم");
        if (l.SellerId == u.Id) throw new DomainException("لا يمكنك شراء اسم تبيعه");
        await money.DebitByCardAsync(u.Id, l.Price, PaymentPurposes.Listing, l.Id, TxKinds.Purchase, $"شراء فوري للاسم «{l.Name}» ({l.Number})", ct);
        var inv = await money.IssueInvoiceAsync(u.Id, null, l.Id, l.Name, l.Type, l.Price, 0, ct);
        inv.OwnerShare = Money.Round2(l.Price * (1 - l.CommissionRate));
        inv.OwnerShareTo = l.SellerId;
        l.Payout = inv.OwnerShare;
        l.Status = ListingStatuses.Sold;
        l.SoldPrice = l.Price;
        l.SoldAt = Now;
        await engine.TransferAsync(l, u, l.Price, ct);
        await money.MarkInvoicePaidAsync(inv, ct); // credits the seller
        await notifier.NotifyAsync(u.Id, NotificationKinds.Won, $"مبارك، أصبح «{l.Name}» باسمك", $"اشتريته بـ {l.Price:0.##} ريال. فاتورتك وشهادة التسجيل في «أسمائي».", "/dashboard/names", ct);
        await notifier.NotifyAsync(l.SellerId, NotificationKinds.Won, $"بيع اسمك «{l.Name}»", $"بـ {l.Price:0.##} ريال، وأُضيف {inv.OwnerShare:0.##} ريال إلى محفظتك بعد عمولة {Money.Pct(l.CommissionRate)}%.", "/dashboard/sell", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.ListingAsync(l, ct);
    }

    // ───── MediatR entry points ─────
    public Task<ListingDto> Handle(CreateListingCommand q, CancellationToken ct) => CreateAsync(q.ReservedId, q.Mode, q.Price, ct);
    public Task<ListingDto> Handle(CancelListingCommand q, CancellationToken ct) => CancelAsync(q.ListingId, ct);
    public Task<List<ListingDto>> Handle(GetMyListingsQuery q, CancellationToken ct) => MineAsync(ct);
    public Task<List<ListingDto>> Handle(GetMarketQuery q, CancellationToken ct) => MarketAsync(ct);
    public Task<ListingDto> Handle(BuyListingCommand q, CancellationToken ct) => BuyAsync(q.ListingId, ct);
}
