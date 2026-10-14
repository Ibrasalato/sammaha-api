using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;

namespace Sammha.Application.Features.Camels;

/// <summary>one camel registered to the owner on the camel registry platform</summary>
public record OwnedCamelDto(Guid Id, string? ExternalId, string? Name, List<string> ChipNumbers, string? Sex, string? Color, string? Type, string? Age, string? Status,
    Guid? ReservedId, string? ReservedName, DateTimeOffset? LinkedAt, DateTimeOffset? SyncedAt);

/// <summary>«إبلي»: the owner's camels, when they were last brought over, and whether the registry is linked yet</summary>
public record MyCamelsDto(List<OwnedCamelDto> Camels, DateTimeOffset? LastSyncedAt, bool Connected);

/// <summary>the signed-in owner's camels (a delegate may view them)</summary>
public record GetMyCamelsQuery : IQuery<MyCamelsDto>;

public class CamelHandlers(IAppDbContext db, Access access, IOptions<CamelRegistryOptions> registry) : IRequestHandler<GetMyCamelsQuery, MyCamelsDto>
{
    public async Task<MyCamelsDto> Handle(GetMyCamelsQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var camels = await db.OwnedCamels.AsNoTracking().Where(c => c.UserId == u.Id).OrderBy(c => c.Name).ThenBy(c => c.CreatedAt).ToListAsync(ct);
        var linked = camels.Where(c => c.ReservedId != null).Select(c => c.ReservedId!.Value).Distinct().ToList();
        // only the owner's own names are shown as linked
        var names = linked.Count == 0 ? new Dictionary<Guid, string>()
            : await db.ReservedNames.AsNoTracking().Where(n => linked.Contains(n.Id) && n.OwnerId == u.Id).ToDictionaryAsync(n => n.Id, n => n.Name, ct);
        var list = camels.Select(c => new OwnedCamelDto(c.Id, c.ExternalId, c.Name,
            c.ChipNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4).ToList(),
            c.Sex, c.Color, c.Type, c.Age, c.Status, c.ReservedId, c.ReservedId is { } r ? names.GetValueOrDefault(r) : null, c.LinkedAt, c.SyncedAt)).ToList();
        return new MyCamelsDto(list, camels.Max(c => c.SyncedAt), registry.Value.Enabled);
    }
}
