using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Engine;

/// <summary>
/// Periodic work (run every few seconds by a hosted service): settle ended auctions, run proxy bids,
/// watchlist reminders, close expired fixed-price listings, open scheduled resale listings.
/// </summary>
public class Sweeper(IAppDbContext db, TimeProvider clock, AuctionEngine engine, Notifier notifier, IAuctionRealtime realtime, IPostCommit postCommit, ILogger<Sweeper> log)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task RunAsync(CancellationToken ct)
    {
        await SettleDueAsync(ct);
        await AutoBidsAsync(ct);
        await RemindersAsync(ct);
        await ListingsAsync(ct);
    }

    public async Task SettleDueAsync(CancellationToken ct)
    {
        var now = Now;
        var due = await db.Auctions.Where(a => !a.Settled && a.PausedAt == null && a.EndsAt <= now).OrderBy(a => a.EndsAt).Select(a => a.Id).Take(100).ToListAsync(ct);
        foreach (var id in due) await SettleOneAsync(id, ct);
    }

    public async Task SettleOneAsync(Guid auctionId, CancellationToken ct)
    {
        try
        {
            await using var uow = await db.BeginAsync([$"a:{auctionId}"], ct);
            var a = await db.Auctions.FirstAsync(x => x.Id == auctionId, ct);
            if (a.Settled || a.PhaseAt(Now) != "ended") return;
            await engine.SettleAsync(a, ct);
            await db.SaveChangesAsync(ct);
            postCommit.Enqueue(() => realtime.ChangedAsync(auctionId));
            await uow.CommitAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Settling auction {Id} failed", auctionId); }
    }

    private async Task AutoBidsAsync(CancellationToken ct)
    {
        var now = Now;
        var ids = await db.AutoBids.Join(db.Auctions, x => x.AuctionId, a => a.Id, (x, a) => a)
            .Where(a => !a.Settled && a.PausedAt == null && a.StartsAt <= now && a.EndsAt > now).Select(a => a.Id).Distinct().ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                await using var uow = await db.BeginAsync([$"a:{id}"], ct);
                var a = await db.Auctions.FirstAsync(x => x.Id == id, ct);
                var before = a.BidCount;
                await engine.ResolveAutoAsync(a, ct);
                if (a.BidCount == before) continue;
                await db.SaveChangesAsync(ct);
                postCommit.Enqueue(() => realtime.ChangedAsync(id));
                await uow.CommitAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Auto-bids on {Id} failed", id); }
        }
    }

    /// <summary>an hour before the start, and an hour before the end</summary>
    private async Task RemindersAsync(CancellationToken ct)
    {
        var now = Now;
        var hour = now.AddHours(1);
        var rows = await db.Watches.Join(db.Auctions, w => w.AuctionId, a => a.Id, (w, a) => new { w, a })
            .Where(x => x.a.PausedAt == null && !x.a.Settled).Where(x => (!x.w.NotifiedStart && x.a.StartsAt > now && x.a.StartsAt <= hour) || (!x.w.NotifiedEnd && x.a.StartsAt <= now && x.a.EndsAt > now && x.a.EndsAt <= hour))
            .Take(500).ToListAsync(ct);
        if (rows.Count == 0) return;
        foreach (var x in rows)
        {
            if (!x.w.NotifiedStart && x.a.StartsAt > now)
            {
                await notifier.NotifyAsync(x.w.UserId, NotificationKinds.Reminder, $"مزاد «{x.a.Name}» يبدأ خلال ساعة", "جهّز مزايدتك، أو فعّل المزايدة التلقائية.", $"/auctions/{x.a.Id}", ct);
                x.w.NotifiedStart = true;
            }
            else if (!x.w.NotifiedEnd)
            {
                await notifier.NotifyAsync(x.w.UserId, NotificationKinds.Reminder, $"مزاد «{x.a.Name}» ينتهي خلال ساعة", $"أعلى مزايدة الآن {x.a.CurrentPrice:0.##} ريال.", $"/auctions/{x.a.Id}", ct);
                x.w.NotifiedEnd = true;
            }
        }
        await using var uow = await db.BeginAsync([], ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
    }

    private async Task ListingsAsync(CancellationToken ct)
    {
        var now = Now;
        var expired = await db.Listings.Where(l => l.Mode == ListingModes.Instant && l.Status == ListingStatuses.Live && l.EndsAt <= now).ToListAsync(ct);
        var opening = await db.Listings.Where(l => l.Mode == ListingModes.Auction && l.Status == ListingStatuses.Scheduled && l.StartsAt <= now).ToListAsync(ct);
        if (expired.Count == 0 && opening.Count == 0) return;
        foreach (var l in expired)
        {
            l.Status = ListingStatuses.Expired;
            await notifier.NotifyAsync(l.SellerId, NotificationKinds.Lost, $"انتهت مدة عرض «{l.Name}» للبيع", "لم يُشترَ الاسم خلال مدة العرض وبقي باسمك. تقدر تعرضه من جديد.", "/dashboard/sell", ct);
        }
        foreach (var l in opening) l.Status = ListingStatuses.Live;
        await using var uow = await db.BeginAsync([], ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
    }
}
