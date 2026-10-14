using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Features.Admin;

public record OwnedNameDto(Guid Id, string Name, string Type, decimal Price, DateTimeOffset AcquiredAt, string Source, string CertCode);

/// <summary>an owner as the customers page (CRM) shows them; NationalId and phones are partly hidden unless the viewer has «owners.full»</summary>
public record OwnerCrmDto(Guid Id, string? NationalId, string FullName, string? Nationality, string? ResidenceCountry, string? ResidenceCity,
    string Phone, string? AltPhone, string Email, bool EmailVerified, bool Active, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt,
    int Completion, List<OwnedNameDto> Names, decimal TotalValue, int Requests, int OpenRequests, bool Masked);

public record ListOwnersQuery : IQuery<List<OwnerCrmDto>>;
/// <summary>records that someone printed or exported the customers report</summary>
public record LogOwnersReportCommand(string Kind, int Count, string? Filter) : ICommand<MediatR.Unit>;

public class OwnersHandlers(IAppDbContext db, Access access, TimeProvider clock)
    : IRequestHandler<ListOwnersQuery, List<OwnerCrmDto>>, IRequestHandler<LogOwnersReportCommand, MediatR.Unit>
{
    /// <summary>keeps the first two and last three characters: 10•••••678</summary>
    public static string? Mask(string? v) => string.IsNullOrEmpty(v) ? v : v.Length <= 5 ? new string('•', v.Length) : v[..2] + new string('•', v.Length - 5) + v[^3..];

    public async Task<List<OwnerCrmDto>> Handle(ListOwnersQuery q, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.OwnersView, ct);
        var full = await access.CanAsync(me, Permissions.OwnersFull, ct);
        var owners = await db.Users.AsNoTracking().Where(u => u.Role == Roles.Owner).OrderByDescending(u => u.CreatedAt).Take(5000).ToListAsync(ct);
        var names = (await db.ReservedNames.AsNoTracking().Where(r => r.OwnerId != null).ToListAsync(ct)).ToLookup(r => r.OwnerId!.Value);
        var reqs = (await db.Requests.AsNoTracking().Where(r => r.Status != RequestStatuses.PendingPayment).Select(r => new { r.UserId, r.Status }).ToListAsync(ct)).ToLookup(r => r.UserId);
        return owners.Select(u =>
        {
            var own = names[u.Id].OrderBy(n => n.Type).ThenBy(n => n.Name)
                .Select(n => new OwnedNameDto(n.Id, n.Name, n.Type, n.Price, n.AcquiredAt, n.Source, n.CertCode)).ToList();
            var rs = reqs[u.Id].ToList();
            return new OwnerCrmDto(u.Id, full ? u.NationalId : Mask(u.NationalId), u.FullName, u.Nationality, u.ResidenceCountry, u.ResidenceCity,
                full ? u.Phone : Mask(u.Phone)!, full ? u.AltPhone : Mask(u.AltPhone), u.Email, u.EmailVerifiedAt != null, u.Active, u.CreatedAt, u.LastLoginAt,
                Mapper.Profile(u).Completion, own, own.Sum(n => n.Price), rs.Count,
                rs.Count(r => r.Status == RequestStatuses.UnderReview || r.Status == RequestStatuses.Accepted), !full);
        }).ToList();
    }

    public async Task<MediatR.Unit> Handle(LogOwnersReportCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.OwnersFull, ct);
        var kind = c.Kind == "csv" ? "تصدير CSV" : "طباعة";
        db.SettingsChanges.Add(new SettingsChange
        {
            At = clock.GetUtcNow(), UserId = me.Id, UserName = me.FullName, Field = "OwnersReport",
            OldValue = kind, NewValue = $"{c.Count.ToString(CultureInfo.InvariantCulture)} عميل{(string.IsNullOrWhiteSpace(c.Filter) ? "" : " · " + c.Filter.Trim()[..Math.Min(c.Filter.Trim().Length, 200)])}",
        });
        await db.SaveChangesAsync(ct);
        return MediatR.Unit.Value;
    }
}
