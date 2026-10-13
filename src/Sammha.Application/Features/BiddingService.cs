using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features;

/// <summary>Bids, automatic bidding, the watchlist and resale participation guarantees.</summary>
public class BiddingService(IAppDbContext db, TimeProvider clock, Access access, Mapper map, AuctionEngine engine, Money money,
    IAuctionRealtime realtime, IPostCommit postCommit, IOptions<PlatformOptions> rules)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    private async Task<Auction> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Auctions.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound("المزاد غير موجود");

    /// <summary>the proposer of an investment name and the seller of a resale cannot bid; resale bidders pay the guarantee first</summary>
    private async Task GuardAsync(Auction a, Guid userId, CancellationToken ct)
    {
        if (a.IsInvestment && a.RequesterId == userId) throw new DomainException("لا يمكنك المزايدة على اسم قدّمته في «استثمار الاسم»");
        if (a.ListingId == null) return;
        if (a.RequesterId == userId) throw new DomainException("لا يمكنك المزايدة على اسم تبيعه");
        if (!await db.BidDeposits.AnyAsync(d => d.AuctionId == a.Id && d.UserId == userId && d.State == BidDepositStates.Held, ct))
            throw new DomainException($"ادفع ضمان دخول المزاد {a.BidDeposit ?? rules.Value.ResaleBidDeposit:0.##} ريال أولًا");
    }

    public async Task<AuctionViewDto> PlaceBidAsync(Guid auctionId, decimal amount, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"a:{auctionId}"], ct);
        var a = await LoadAsync(auctionId, ct);
        var phase = a.PhaseAt(Now);
        if (phase == "scheduled") throw new DomainException("المزاد لم يبدأ بعد");
        if (phase == "ended" || a.Settled) throw new DomainException("انتهى المزاد");
        var min = a.CurrentPrice + a.MinIncrement;
        if (amount < min) throw new DomainException($"أقل مزايدة {min:0.##} ريال");
        if (amount > 10_000_000) throw new DomainException("المبلغ كبير جدًا");
        if (a.LeaderId == u.Id) throw new DomainException("أنت صاحب أعلى مزايدة حاليًا");
        await GuardAsync(a, u.Id, ct);
        await engine.RecordBidAsync(a, u.Id, decimal.Floor(amount), false, ct);
        await engine.ResolveAutoAsync(a, ct);
        await db.SaveChangesAsync(ct);
        postCommit.Enqueue(() => realtime.ChangedAsync(a.Id));
        await uow.CommitAsync(ct);
        return await map.ViewAsync(a, ct);
    }

    public async Task<AuctionViewDto> SetAutoBidAsync(Guid auctionId, decimal max, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"a:{auctionId}"], ct);
        var a = await LoadAsync(auctionId, ct);
        if (a.Settled || a.PhaseAt(Now) == "ended") throw new DomainException("انتهى المزاد");
        await GuardAsync(a, u.Id, ct);
        var floor = a.CurrentPrice + a.MinIncrement;
        if (max < floor) throw new DomainException($"الحد الأقصى {floor:0.##} ريال على الأقل");
        if (max > 1_000_000) throw new DomainException("الحد الأقصى كبير جدًا");
        var existing = await db.AutoBids.FirstOrDefaultAsync(x => x.AuctionId == a.Id && x.UserId == u.Id, ct);
        if (existing != null) db.AutoBids.Remove(existing);
        db.AutoBids.Add(new AutoBid { AuctionId = a.Id, UserId = u.Id, Max = decimal.Floor(max), CreatedAt = Now });
        if (!await db.Watches.AnyAsync(w => w.AuctionId == a.Id && w.UserId == u.Id, ct)) db.Watches.Add(new Watch { AuctionId = a.Id, UserId = u.Id });
        await db.SaveChangesAsync(ct);
        await engine.ResolveAutoAsync(a, ct);
        await db.SaveChangesAsync(ct);
        postCommit.Enqueue(() => realtime.ChangedAsync(a.Id));
        await uow.CommitAsync(ct);
        return await map.ViewAsync(a, ct);
    }

    public async Task<AuctionViewDto> CancelAutoBidAsync(Guid auctionId, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var a = await LoadAsync(auctionId, ct);
        await db.AutoBids.Where(x => x.AuctionId == a.Id && x.UserId == u.Id).ExecuteDeleteAsync(ct);
        return await map.ViewAsync(a, ct);
    }

    public async Task<bool> ToggleWatchAsync(Guid auctionId, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (!await db.Auctions.AnyAsync(a => a.Id == auctionId, ct)) throw DomainException.NotFound("المزاد غير موجود");
        var w = await db.Watches.FirstOrDefaultAsync(x => x.AuctionId == auctionId && x.UserId == u.Id, ct);
        if (w != null) { db.Watches.Remove(w); await db.SaveChangesAsync(ct); return false; }
        db.Watches.Add(new Watch { AuctionId = auctionId, UserId = u.Id });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<AuctionDto>> WatchlistAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.Watches.Where(w => w.UserId == u.Id).Join(db.Auctions, w => w.AuctionId, a => a.Id, (w, a) => a)
            .AsNoTracking().OrderBy(a => a.EndsAt).ToListAsync(ct);
        return await map.AuctionsAsync(list, ct);
    }

    public async Task<List<MyBidDto>> MyBidsAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var bids = await db.Bids.AsNoTracking().Where(b => b.UserId == u.Id).ToListAsync(ct);
        var top = bids.GroupBy(b => b.AuctionId).Select(g => g.OrderByDescending(b => b.Amount).First()).ToList();
        var ids = top.Select(b => b.AuctionId).ToList();
        var auctions = await db.Auctions.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var dtos = (await map.AuctionsAsync(auctions, ct)).ToDictionary(a => a.Id);
        return top.Where(b => dtos.ContainsKey(b.AuctionId)).OrderByDescending(b => b.CreatedAt)
            .Select(b => new MyBidDto(b.Id, b.AuctionId, b.UserId, "", b.Amount, b.CreatedAt, b.Auto ? true : null, dtos[b.AuctionId])).ToList();
    }

    /// <summary>resale auctions: the participation guarantee, refunded unless the bidder wins</summary>
    public async Task<AuctionViewDto> PayBidDepositAsync(Guid auctionId, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"a:{auctionId}", $"u:{u.Id}"], ct);
        var a = await LoadAsync(auctionId, ct);
        if (a.ListingId == null) throw DomainException.NotFound("المزاد غير موجود");
        if (a.Settled || a.PhaseAt(Now) == "ended") throw new DomainException("انتهى المزاد");
        if (a.RequesterId == u.Id) throw new DomainException("لا يمكنك المزايدة على اسم تبيعه");
        var dep = await db.BidDeposits.FirstOrDefaultAsync(d => d.AuctionId == a.Id && d.UserId == u.Id, ct);
        if (dep is { State: BidDepositStates.Held }) return await map.ViewAsync(a, ct);
        var amount = a.BidDeposit ?? rules.Value.ResaleBidDeposit;
        await money.DebitAsync(u.Id, amount, TxKinds.BidDeposit, $"ضمان دخول مزاد «{a.Name}»", a.Id, ct);
        if (dep == null) db.BidDeposits.Add(new BidDeposit { AuctionId = a.Id, UserId = u.Id, Amount = amount, CreatedAt = Now });
        else { dep.State = BidDepositStates.Held; dep.Amount = amount; dep.CreatedAt = Now; }
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.ViewAsync(a, ct);
    }
}
