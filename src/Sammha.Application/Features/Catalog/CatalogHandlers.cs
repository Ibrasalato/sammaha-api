using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Catalog;

/// <summary>Public pages: auctions, the name registry, availability checks, events and certificate verification.</summary>
public class CatalogHandlers(IAppDbContext db, TimeProvider clock, Mapper map, Matching matching, Sweeper sweeper)
    : IRequestHandler<GetStatsQuery, PublicStatsDto>, IRequestHandler<ListAuctionsQuery, List<AuctionDto>>, IRequestHandler<GetAuctionQuery, AuctionViewDto>, IRequestHandler<ListReservedNamesQuery, List<ReservedNameDto>>, IRequestHandler<CheckNamesQuery, List<NameCheckDto>>, IRequestHandler<ListEventsQuery, List<CamelEventDto>>, IRequestHandler<VerifyCertificateQuery, ReservedNameDto?>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    private async Task<PublicStatsDto> StatsAsync(CancellationToken ct)
    {
        var now = Now;
        return new PublicStatsDto(
            await db.Auctions.CountAsync(a => a.StartsAt <= now && a.EndsAt > now, ct),
            await db.Auctions.CountAsync(a => a.StartsAt > now, ct),
            await db.ReservedNames.CountAsync(ct),
            await db.ReservedNames.Select(r => r.OwnerName).Distinct().CountAsync(ct));
    }

    private async Task<List<AuctionDto>> ListAuctionsAsync(AuctionFilter f, CancellationToken ct)
    {
        var now = Now;
        var q = db.Auctions.AsNoTracking().Where(a => a.CancelledAt == null);
        q = f.Phase switch
        {
            "live" => q.Where(a => a.StartsAt <= now && a.EndsAt > now),
            "scheduled" => q.Where(a => a.StartsAt > now),
            "ended" => q.Where(a => a.EndsAt <= now),
            _ => q,
        };
        if (NameTypes.IsValid(f.Type)) q = q.Where(a => a.Type == f.Type);
        var list = await q.OrderByDescending(a => a.EndsAt).Take(500).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var n = NameMatcher.Normalize(f.Q);
            list = list.Where(a => NameMatcher.Normalize(a.Name).Contains(n)).ToList();
        }
        int Order(Auction a) => a.PhaseAt(now) switch { "live" => 0, "scheduled" => 1, _ => 2 };
        list = list.OrderBy(Order)
            .ThenBy(a => a.PhaseAt(now) == "ended" ? -a.EndsAt.ToUnixTimeSeconds() : a.PhaseAt(now) == "scheduled" ? a.StartsAt.ToUnixTimeSeconds() : a.EndsAt.ToUnixTimeSeconds())
            .ToList();
        return await map.AuctionsAsync(list, ct);
    }

    private async Task<AuctionViewDto> GetAuctionAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Auctions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("المزاد غير موجود");
        if (!a.Settled && a.EndsAt <= Now)
        {
            // settle on demand so the page never waits for the next sweep
            await sweeper.SettleOneAsync(id, ct);
            a = await db.Auctions.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        }
        return await map.ViewAsync(a, ct);
    }

    private async Task<List<ReservedNameDto>> ListReservedAsync(string? q, string? type, CancellationToken ct)
    {
        var query = db.ReservedNames.AsNoTracking();
        if (NameTypes.IsValid(type)) query = query.Where(r => r.Type == type);
        if (!string.IsNullOrWhiteSpace(q) && Transliterator.IsLatin(q))
        {
            // Latin search: match the registry's Latin spellings (overrides or transliterations) in memory
            var key = Transliterator.Key(q.Trim());
            var all = await query.OrderByDescending(r => r.AcquiredAt).Take(5000).ToListAsync(ct);
            var hits = all.Where(r => Transliterator.Key(Mapper.LatinOf(r)).Contains(key)).Take(1000).ToList();
            var ids = hits.Where(r => r.OwnerId != null).Select(r => r.OwnerId!.Value).Distinct().ToList();
            var av = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.ShowAvatar && u.AvatarUrl != null).ToDictionaryAsync(u => u.Id, u => u.AvatarUrl, ct);
            return PublicRows(hits, av);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = NameMatcher.Normalize(q);
            var raw = q.Trim();
            query = query.Where(r => r.Normalized.Contains(n) || r.OwnerName.Contains(raw));
        }
        var list = await query.OrderByDescending(r => r.AcquiredAt).Take(1000).ToListAsync(ct);
        var ownerIds = list.Where(r => r.OwnerId != null).Select(r => r.OwnerId!.Value).Distinct().ToList();
        var avatars = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id) && u.ShowAvatar && u.AvatarUrl != null)
            .ToDictionaryAsync(u => u.Id, u => u.AvatarUrl, ct);
        return PublicRows(list, avatars);
    }

    /// <summary>
    /// registry rows for anyone: no owner id (it would link a person across auctions and requests), and each owner's
    /// opted-in photo only on their first row rather than repeated on every name they hold
    /// </summary>
    private static List<ReservedNameDto> PublicRows(List<ReservedName> rows, IReadOnlyDictionary<Guid, string?> avatars)
    {
        var shown = new HashSet<Guid>();
        return rows.Select(r =>
        {
            var avatar = r.OwnerId is { } o && shown.Add(o) ? avatars.GetValueOrDefault(o) : null;
            return Mapper.Reserved(r, avatar) with { OwnerId = null };
        }).ToList();
    }

    private async Task<List<NameCheckDto>> CheckNamesAsync(List<string> names, CancellationToken ct)
    {
        if (names.Count > 10) throw new DomainException("عدد الأسماء كبير");
        var checks = (await matching.CheckAsync(names, null, ct)).Select(Matching.Redact);
        return checks.Select(c => new NameCheckDto(c.Name, c.Normalized, c.Verdict, c.Message, Mapper.Hits(c.Hits))).ToList();
    }

    private async Task<List<CamelEventDto>> EventsAsync(CancellationToken ct) =>
        (await db.Events.AsNoTracking().OrderBy(e => e.StartsOn).ToListAsync(ct)).Select(Mapper.Event).ToList();

    private async Task<ReservedNameDto?> VerifyAsync(string code, CancellationToken ct)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        var r = await db.ReservedNames.AsNoTracking().FirstOrDefaultAsync(x => x.CertCode == c, ct);
        return r == null ? null : Mapper.Reserved(r) with { OwnerId = null };
    }

    // ───── MediatR entry points ─────
    public Task<PublicStatsDto> Handle(GetStatsQuery q, CancellationToken ct) => StatsAsync(ct);
    public Task<List<AuctionDto>> Handle(ListAuctionsQuery q, CancellationToken ct) => ListAuctionsAsync(q.Filter, ct);
    public Task<AuctionViewDto> Handle(GetAuctionQuery q, CancellationToken ct) => GetAuctionAsync(q.Id, ct);
    public Task<List<ReservedNameDto>> Handle(ListReservedNamesQuery q, CancellationToken ct) => ListReservedAsync(q.Q, q.Type, ct);
    public Task<List<NameCheckDto>> Handle(CheckNamesQuery q, CancellationToken ct) => CheckNamesAsync(q.Names, ct);
    public Task<List<CamelEventDto>> Handle(ListEventsQuery q, CancellationToken ct) => EventsAsync(ct);
    public Task<ReservedNameDto?> Handle(VerifyCertificateQuery q, CancellationToken ct) => VerifyAsync(q.Code, ct);
}
