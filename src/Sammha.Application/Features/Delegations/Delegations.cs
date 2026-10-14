using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Delegations;

public record DelegationInput(string FullName, string NationalId, string? PhoneCountry, string Phone, string Email, string Capacity, string? CapacityNote,
    List<string>? Scopes, bool Permanent, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);

public record DelegationDto(Guid Id, Guid OwnerId, string OwnerName, string FullName, string NationalId, string Phone, string Email, string Capacity, string CapacityLabel,
    string? CapacityNote, List<string> Scopes, bool Permanent, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt, bool Linked);

public record ListMyDelegationsQuery : IQuery<List<DelegationDto>>;
public record ListDelegatedToMeQuery : IQuery<List<DelegationDto>>;
public record SaveDelegationCommand(Guid? Id, DelegationInput Input) : ICommand<DelegationDto>;
public record RevokeDelegationCommand(Guid Id) : ICommand<DelegationDto>;
/// <summary>the delegate switches to the owner's account (a delegated session)</summary>
public record ActAsCommand(Guid DelegationId) : ICommand<AuthResult>;
/// <summary>back to the delegate's own account</summary>
public record StopActingCommand : ICommand<AuthResult>;

public partial class DelegationHandlers(IAppDbContext db, Access access, ICurrentUser current, Sessions sessions, Notifier notifier, TimeProvider clock)
    : IRequestHandler<ListMyDelegationsQuery, List<DelegationDto>>, IRequestHandler<ListDelegatedToMeQuery, List<DelegationDto>>,
      IRequestHandler<SaveDelegationCommand, DelegationDto>, IRequestHandler<RevokeDelegationCommand, DelegationDto>,
      IRequestHandler<ActAsCommand, AuthResult>, IRequestHandler<StopActingCommand, AuthResult>
{
    private const int MaxActive = 5;
    private DateTimeOffset Now => clock.GetUtcNow();
    // linear: the domain labels cannot contain the dot that separates them, so there is nothing to backtrack over
    [GeneratedRegex(@"^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$")] private static partial Regex EmailRx();

    private DelegationDto Dto(Delegation d, string ownerName, bool forDelegate = false) => new(d.Id, d.OwnerId, ownerName, d.FullName,
        forDelegate ? d.NationalId : d.NationalId, d.Phone, d.Email, d.Capacity, DelegationCapacities.Arabic.GetValueOrDefault(d.Capacity, d.Capacity),
        d.CapacityNote, d.ScopeList().ToList(), d.Permanent, d.StartsAt, d.EndsAt, d.StatusAt(Now), d.CreatedAt, d.LastUsedAt, d.DelegateUserId != null);

    /// <summary>the owner signed in to their own account (not a delegate on it)</summary>
    private async Task<User> OwnerAsync(CancellationToken ct)
    {
        if (current.DelegationId != null) throw DomainException.Forbidden("إدارة التفويض لصاحب الحساب فقط");
        var u = await access.MeAsync(ct);
        if (u.Role != Roles.Owner) throw DomainException.Forbidden("التفويض لحسابات ملاك الإبل");
        return u;
    }

    public async Task<List<DelegationDto>> Handle(ListMyDelegationsQuery q, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        return (await db.Delegations.AsNoTracking().Where(d => d.OwnerId == u.Id).OrderByDescending(d => d.CreatedAt).ToListAsync(ct)).Select(d => Dto(d, u.FullName)).ToList();
    }

    public async Task<List<DelegationDto>> Handle(ListDelegatedToMeQuery q, CancellationToken ct)
    {
        if (current.DelegationId != null) return [];
        var u = await access.MeAsync(ct);
        var list = await (from d in db.Delegations.AsNoTracking() join o in db.Users.AsNoTracking() on d.OwnerId equals o.Id
                          where d.DelegateUserId == u.Id && o.Active orderby d.CreatedAt descending select new { d, o.FullName }).ToListAsync(ct);
        return list.Select(x => Dto(x.d, x.FullName, true)).ToList();
    }

    public async Task<DelegationDto> Handle(SaveDelegationCommand c, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        var i = c.Input;
        var name = Regex.Replace((i.FullName ?? "").Trim(), @"\s+", " ");
        if (name.Length is < 6 or > 120) throw new DomainException("اكتب اسم المفوض الرباعي");
        var nid = (i.NationalId ?? "").Trim().ToUpperInvariant();
        if (!(nid.Length is >= 5 and <= 20 && nid.All(char.IsAsciiLetterOrDigit))) throw new DomainException("رقم هوية المفوض غير صحيح");
        var phone = Phones.Normalize(i.PhoneCountry, i.Phone) ?? throw new DomainException("رقم جوال المفوض غير صحيح");
        var email = (i.Email ?? "").Trim();
        if (email.Length > 160 || !EmailRx().IsMatch(email)) throw new DomainException("بريد المفوض غير صحيح");
        if (nid == u.NationalId || phone == u.Phone) throw new DomainException("لا يمكنك تفويض نفسك");
        if (!DelegationCapacities.Arabic.ContainsKey(i.Capacity ?? "")) throw new DomainException("اختر صفة المفوض");
        var note = string.IsNullOrWhiteSpace(i.CapacityNote) ? null : i.CapacityNote.Trim()[..Math.Min(i.CapacityNote.Trim().Length, 100)];
        if (i.Capacity == DelegationCapacities.Other && note == null) throw new DomainException("اكتب صفة المفوض");
        var scopes = (i.Scopes ?? []).Where(DelegationScopes.All.Contains).Distinct().ToList();
        var starts = i.StartsAt ?? Now;
        DateTimeOffset? ends = null;
        if (!i.Permanent)
        {
            ends = i.EndsAt ?? throw new DomainException("حدّد نهاية التفويض المؤقت");
            if (ends <= starts) throw new DomainException("نهاية التفويض بعد بدايته");
            if (ends <= Now) throw new DomainException("نهاية التفويض في الماضي");
            if (ends > starts.AddYears(3)) throw new DomainException("أقصى مدة للتفويض المؤقت 3 سنوات، أو اختر تفويضًا دائمًا");
        }

        Delegation? d;
        if (c.Id is { } id)
        {
            d = await db.Delegations.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == u.Id, ct) ?? throw DomainException.NotFound("التفويض غير موجود");
            if (d.RevokedAt != null) throw new DomainException("التفويض ملغى؛ أضف تفويضًا جديدًا");
        }
        else
        {
            var live = (await db.Delegations.AsNoTracking().Where(x => x.OwnerId == u.Id && x.RevokedAt == null).ToListAsync(ct)).Count(x => x.StatusAt(Now) != DelegationStatuses.Expired);
            if (live >= MaxActive) throw new DomainException($"{MaxActive} تفويضات قائمة على الأكثر، ألغِ أحدها أولًا");
            if (await db.Delegations.AnyAsync(x => x.OwnerId == u.Id && x.RevokedAt == null && x.NationalId == nid && (x.Permanent || x.EndsAt > Now), ct))
                throw new DomainException("يوجد تفويض قائم لهذا الشخص، عدّله بدل إضافة تفويض جديد");
            d = new Delegation { OwnerId = u.Id, CreatedAt = Now };
            db.Delegations.Add(d);
        }
        var relink = d.NationalId != nid || d.Phone != phone;
        d.FullName = name; d.NationalId = nid; d.Phone = phone; d.Email = email; d.Capacity = i.Capacity!; d.CapacityNote = note;
        d.Scopes = string.Join(',', scopes); d.Permanent = i.Permanent; d.StartsAt = starts; d.EndsAt = ends;
        if (relink)
        {
            // linked to the delegate's account only when both the ID and the mobile match
            var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.NationalId == nid && x.Phone == phone && x.Role == Roles.Owner && x.Active, ct);
            d.DelegateUserId = target?.Id;
            if (target != null)
                await notifier.NotifyAsync(target.Id, NotificationKinds.Delegation, $"فوّضك {u.FullName} لإدارة حسابه", "ادخل إلى حسابه من «التفويضات» في لوحة التحكم.", "/dashboard/delegations", ct);
        }
        await db.SaveChangesAsync(ct);
        return Dto(d, u.FullName);
    }

    public async Task<DelegationDto> Handle(RevokeDelegationCommand c, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        var d = await db.Delegations.FirstOrDefaultAsync(x => x.Id == c.Id && x.OwnerId == u.Id, ct) ?? throw DomainException.NotFound("التفويض غير موجود");
        if (d.RevokedAt == null)
        {
            d.RevokedAt = Now; d.RevokedBy = u.FullName;
            // end the delegate's open sessions on this account
            var now = Now;
            await db.RefreshTokens.Where(t => t.DelegationId == d.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "delegation_revoked"), ct);
            if (d.DelegateUserId is { } du) await notifier.NotifyAsync(du, NotificationKinds.Delegation, $"ألغى {u.FullName} تفويضك", "لم تعد تستطيع إدارة حسابه.", "/dashboard/delegations", ct);
            await db.SaveChangesAsync(ct);
        }
        return Dto(d, u.FullName);
    }

    public async Task<AuthResult> Handle(ActAsCommand c, CancellationToken ct)
    {
        if (current.DelegationId != null) throw new DomainException("ارجع إلى حسابك أولًا");
        var me = await access.MeAsync(ct);
        var d = await db.Delegations.FirstOrDefaultAsync(x => x.Id == c.DelegationId && x.DelegateUserId == me.Id, ct) ?? throw DomainException.NotFound("التفويض غير موجود");
        var status = d.StatusAt(Now);
        if (status != DelegationStatuses.Active)
            throw new DomainException(status switch { DelegationStatuses.Scheduled => "لم يبدأ التفويض بعد", DelegationStatuses.Expired => "انتهت مدة التفويض", _ => "التفويض ملغى" });
        var owner = await db.Users.FirstOrDefaultAsync(x => x.Id == d.OwnerId && x.Active, ct) ?? throw new DomainException("حساب صاحب التفويض موقوف");
        // delegation is for owners' accounts only (an owner later made staff cannot be entered through an old delegation)
        if (owner.Role != Roles.Owner) throw new DomainException("التفويض ملغى");
        d.LastUsedAt = Now;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(owner.Id, NotificationKinds.Delegation, $"دخل المفوض {me.FullName} إلى حسابك", "كل ما يجريه مسجّل باسمه في سجل النشاط.", "/dashboard/settings?tab=delegation", ct);
        return await sessions.IssueAsync(owner, null, ct, d.Id);
    }

    public async Task<AuthResult> Handle(StopActingCommand c, CancellationToken ct)
    {
        if (current.DelegationId is not { } did) throw new DomainException("أنت في حسابك");
        // the delegated session itself must still be valid (owner's stamp, delegation in force, delegate active),
        // and still signed in: a leftover access token of an ended session cannot be traded for a fresh one
        var owner = await access.MeAsync(ct);
        var d = await access.DelegationAsync(ct) ?? throw DomainException.Unauthorized();
        if (d.OwnerId != owner.Id) throw DomainException.Unauthorized();
        if (current.SessionId is not { } sid || !await db.RefreshTokens.AnyAsync(t => t.FamilyId == sid && t.DelegationId == did && t.RevokedAt == null, ct))
            throw DomainException.Unauthorized("انتهت الجلسة، سجّل الدخول من جديد");
        var me = await db.Users.FirstOrDefaultAsync(x => x.Id == d.DelegateUserId && x.Active, ct) ?? throw DomainException.Unauthorized("سجّل الدخول من جديد");
        var now = Now;
        await db.RefreshTokens.Where(t => t.FamilyId == sid && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "delegation_ended"), ct);
        return await sessions.IssueAsync(me, null, ct);
    }
}
