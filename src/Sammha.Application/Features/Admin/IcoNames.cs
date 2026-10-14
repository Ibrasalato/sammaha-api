using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Admin;

/// <summary>a name the International Camel Organization owns: who won it and did not pay, for how much, the forfeited guarantee, and any re-auction</summary>
public record IcoNameDto(Guid Id, string Name, string Latin, string Type, string? ForfeitedFromName, Guid? ForfeitedFromId, DateTimeOffset? ForfeitedAt,
    decimal? ForfeitedPrice, decimal? ForfeitedDeposit, string CertCode, AuctionDto? Auction);

public record ListIcoNamesQuery : IQuery<List<IcoNameDto>>;
/// <summary>put an organization name up for auction again (names.ico)</summary>
public record AuctionIcoNameCommand(Guid ReservedId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal? OpeningPrice) : ICommand<AuctionDto>;

public class IcoNameHandlers(IAppDbContext db, Access access, Notifier notifier, SettingsStore settings, TimeProvider clock, IOptions<PlatformOptions> rules)
    : IRequestHandler<ListIcoNamesQuery, List<IcoNameDto>>, IRequestHandler<AuctionIcoNameCommand, AuctionDto>
{
    private static readonly IReadOnlyDictionary<Guid, string> NoNames = new Dictionary<Guid, string>();

    public async Task<List<IcoNameDto>> Handle(ListIcoNamesQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.NamesIco, ct);
        var names = await db.ReservedNames.AsNoTracking().Where(r => r.OwnerId == null && r.Source == ReservedSources.Exception).OrderByDescending(r => r.ForfeitedAt).ToListAsync(ct);
        var ids = names.Select(n => n.Id).ToList();
        // the latest re-auction of each name that has not ended without a sale
        var auctions = await db.Auctions.AsNoTracking().Where(a => a.IcoReservedId != null && ids.Contains(a.IcoReservedId.Value) && a.CancelledAt == null)
            .OrderByDescending(a => a.StartsAt).ToListAsync(ct);
        return names.Select(n =>
        {
            var a = auctions.FirstOrDefault(x => x.IcoReservedId == n.Id && !x.Settled);
            return new IcoNameDto(n.Id, n.Name, Mapper.LatinOf(n), n.Type, n.ForfeitedFromName, n.ForfeitedFromId, n.ForfeitedAt, n.ForfeitedPrice, n.ForfeitedDeposit, n.CertCode,
                a == null ? null : Mapper.Auction(a, NoNames));
        }).ToList();
    }

    public async Task<AuctionDto> Handle(AuctionIcoNameCommand q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.NamesIco, ct);
        var now = clock.GetUtcNow();
        if (q.EndsAt <= q.StartsAt) throw new DomainException("موعد النهاية يجب أن يكون بعد البداية");
        if (q.EndsAt <= now) throw new DomainException("موعد النهاية في الماضي");
        if (q.OpeningPrice is < 0) throw new DomainException("السعر الافتتاحي غير صحيح");
        await using var uow = await db.BeginAsync([$"ico:{q.ReservedId}", "schedule"], ct);
        var rn = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == q.ReservedId, ct) ?? throw DomainException.NotFound("الاسم غير موجود");
        if (rn.OwnerId != null || rn.Source != ReservedSources.Exception) throw new DomainException("هذا الاسم ليس من أسماء المنظمة");
        if (await db.Auctions.AnyAsync(a => a.IcoReservedId == rn.Id && !a.Settled && a.CancelledAt == null, ct)) throw new DomainException("للاسم مزاد قائم أو مجدول");
        var R = rules.Value;
        var opening = Money.Round2(q.OpeningPrice ?? R.OpeningPrice);
        var entry = await settings.DepositAmountAsync(R.DepositAmount, ct);
        var a = new Auction
        {
            Number = $"AU-{await db.NextNumberAsync("AuctionNo", ct)}", IcoReservedId = rn.Id, Name = rn.Name, Type = rn.Type, RequesterId = Guid.Empty,
            OpeningPrice = opening, MinIncrement = R.MinIncrement, StartsAt = q.StartsAt, EndsAt = q.EndsAt, CurrentPrice = opening,
            BidDeposit = entry > 0 ? entry : null,
        };
        db.Auctions.Add(a);
        // whoever lost it last time is told it is back on offer
        if (rn.ForfeitedFromId is { } prev)
            await notifier.NotifyAsync(prev, NotificationKinds.AuctionOpened, $"يُطرح «{rn.Name}» في مزاد جديد", "طرحته المنظمة الدولية للإبل في مزاد عام، وتستطيع المشاركة بسداد ضمان الدخول.", $"/auctions/{a.Id}", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Mapper.Auction(a, NoNames);
    }
}
