using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Features.Camels;

/// <summary>one camel registered to the owner on the camel registry platform (KACF)</summary>
public record OwnedCamelDto(Guid Id, string? ExternalId, string? Name, List<string> ChipNumbers, string? Sex, string? Color, string? Type, string? Age, string? Status,
    Guid? ReservedId, string? ReservedName, DateTimeOffset? LinkedAt, DateTimeOffset? SyncedAt, string? ReservedType = null);

/// <summary>«إبلي»: the owner's camels, when they were last brought over, and whether the registry is linked yet</summary>
public record MyCamelsDto(List<OwnedCamelDto> Camels, DateTimeOffset? LastSyncedAt, bool Connected);

/// <summary>the signed-in owner's camels (a delegate may view them)</summary>
public record GetMyCamelsQuery : IQuery<MyCamelsDto>;
/// <summary>«ربط بشريحة»: a won قعود or بكرة name is carried by one of the owner's camels (the account holder only)</summary>
public record LinkCamelCommand(Guid CamelId, Guid ReservedId) : ICommand<OwnedCamelDto>;
public record UnlinkCamelCommand(Guid CamelId) : ICommand<OwnedCamelDto>;

/// <summary>a name that changes hands (or passes to the organization) leaves the previous owner's camel</summary>
public static class CamelLinks
{
    public static async Task ClearAsync(IAppDbContext db, Guid reservedId, CancellationToken ct)
    {
        foreach (var c in await db.OwnedCamels.Where(x => x.ReservedId == reservedId).ToListAsync(ct))
        {
            c.ReservedId = null;
            c.LinkedAt = null;
        }
    }

    /// <summary>male | female | null (unknown); the registry may send Arabic or English</summary>
    public static string? SexOf(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "male" or "m" or "ذكر" => "male",
        "female" or "f" or "أنثى" or "انثى" => "female",
        _ => null,
    };

    /// <summary>قعدان are young males, بكار young females; منقية is a herd and has no chip</summary>
    public static string? SexFor(string nameType) => nameType switch { NameTypes.Qaadan => "male", NameTypes.Bakkar => "female", _ => null };

    public static List<string> Chips(OwnedCamel c) => c.ChipNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4).ToList();
}

public class CamelHandlers(IAppDbContext db, Access access, ICurrentUser current, IOptions<CamelRegistryOptions> registry, TimeProvider clock)
    : IRequestHandler<GetMyCamelsQuery, MyCamelsDto>, IRequestHandler<LinkCamelCommand, OwnedCamelDto>, IRequestHandler<UnlinkCamelCommand, OwnedCamelDto>
{
    private static OwnedCamelDto Dto(OwnedCamel c, ReservedName? r) => new(c.Id, c.ExternalId, c.Name, CamelLinks.Chips(c),
        c.Sex, c.Color, c.Type, c.Age, c.Status, c.ReservedId, r?.Name, c.LinkedAt, c.SyncedAt, r?.Type);

    public async Task<MyCamelsDto> Handle(GetMyCamelsQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var camels = await db.OwnedCamels.AsNoTracking().Where(c => c.UserId == u.Id).OrderBy(c => c.Name).ThenBy(c => c.CreatedAt).ToListAsync(ct);
        var linked = camels.Where(c => c.ReservedId != null).Select(c => c.ReservedId!.Value).Distinct().ToList();
        // only the owner's own names are shown as linked
        var names = linked.Count == 0 ? new Dictionary<Guid, ReservedName>()
            : await db.ReservedNames.AsNoTracking().Where(n => linked.Contains(n.Id) && n.OwnerId == u.Id).ToDictionaryAsync(n => n.Id, ct);
        var list = camels.Select(c => Dto(c, c.ReservedId is { } r ? names.GetValueOrDefault(r) : null)).ToList();
        return new MyCamelsDto(list, camels.Max(c => c.SyncedAt), registry.Value.Enabled);
    }

    /// <summary>linking a name to a chip is the account holder's decision (delegates are refused by the pipeline too)</summary>
    private async Task<User> OwnerAsync(CancellationToken ct)
    {
        if (current.DelegationId != null) throw DomainException.Forbidden("ربط الأسماء بالشرائح لصاحب الحساب فقط");
        var u = await access.MeAsync(ct);
        if (u.Role != Roles.Owner) throw DomainException.Forbidden("ربط الأسماء بالشرائح لحسابات ملاك الإبل");
        return u;
    }

    public async Task<OwnedCamelDto> Handle(LinkCamelCommand c, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        await using var uow = await db.BeginAsync([$"c:{c.CamelId}", $"n:{c.ReservedId}"], ct);
        var camel = await db.OwnedCamels.FirstOrDefaultAsync(x => x.Id == c.CamelId && x.UserId == u.Id, ct) ?? throw DomainException.NotFound("هذه الشريحة ليست ضمن إبلك");
        var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == c.ReservedId && x.OwnerId == u.Id, ct) ?? throw new DomainException("الاسم غير مسجل باسمك");
        var want = CamelLinks.SexFor(r.Type) ?? throw new DomainException("المنقية لا تُربط بشريحة؛ الربط لأسماء القعدان والبكار فقط");
        if (await db.Invoices.AnyAsync(i => i.UserId == u.Id && i.Name == r.Name && i.Status == InvoiceStatuses.Unpaid, ct)) throw new DomainException("سدّد فاتورة الاسم أولًا");
        if (camel.ReservedId != null) throw new DomainException(camel.ReservedId == r.Id ? "الاسم مربوط بهذه الشريحة مسبقًا" : "هذه الشريحة مربوطة باسم آخر، فك الربط أولًا");
        if (await db.OwnedCamels.AnyAsync(x => x.ReservedId == r.Id && x.Id != camel.Id, ct)) throw new DomainException("الاسم مربوط بشريحة أخرى، فك الربط أولًا");
        var sex = CamelLinks.SexOf(camel.Sex);
        if (sex != null && sex != want)
            throw new DomainException(want == "male" ? "أسماء القعدان للذكور: هذه الشريحة لأنثى" : "أسماء البكار للإناث: هذه الشريحة لذكر");
        camel.ReservedId = r.Id;
        camel.LinkedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(camel, r);
    }

    public async Task<OwnedCamelDto> Handle(UnlinkCamelCommand c, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        await using var uow = await db.BeginAsync([$"c:{c.CamelId}"], ct);
        var camel = await db.OwnedCamels.FirstOrDefaultAsync(x => x.Id == c.CamelId && x.UserId == u.Id, ct) ?? throw DomainException.NotFound("هذه الشريحة ليست ضمن إبلك");
        camel.ReservedId = null;
        camel.LinkedAt = null;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(camel, null);
    }
}
