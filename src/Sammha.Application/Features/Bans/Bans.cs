using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Application.Features.Admin;
using Sammha.Domain;

namespace Sammha.Application.Features.Bans;

/// <summary>Status is computed for today (Riyadh): active | scheduled | ended | lifted. NationalId and phone are masked unless «owners.full».</summary>
public record BanDto(Guid Id, Guid? OwnerId, string Name, string? NationalId, string? Phone, string Reason, DateOnly StartsOn, DateOnly? EndsOn,
    string Status, string Source, DateTimeOffset CreatedAt, string CreatedByName, DateTimeOffset? LiftedAt, string? LiftedByName, string? LiftReason);
public record BanCountsDto(int All, int Active, int Scheduled, int Ended, int Lifted);
public record BanPageDto(List<BanDto> Items, int Total, int Page, int Size, BanCountsDto Counts);
/// <summary>the ban in force on the signed-in owner, if any</summary>
public record MyBanDto(string Reason, DateOnly StartsOn, DateOnly? EndsOn);

public record ListBansQuery(string? Q, string? Status, int Page = 1, int Size = 50) : IQuery<BanPageDto>;
/// <summary>OwnerId, or an owner found by mobile or national ID; or a national ID alone (with a name) for someone not registered yet</summary>
public record CreateBanCommand(Guid? OwnerId, string? OwnerPhoneOrNationalId, string? NationalIdOnly, string? Name, string Reason, DateOnly StartsOn, DateOnly? EndsOn) : ICommand<BanDto>;
public record UpdateBanCommand(Guid Id, string Reason, DateOnly StartsOn, DateOnly? EndsOn) : ICommand<BanDto>;
public record LiftBanCommand(Guid Id, string Reason) : ICommand<BanDto>;
public record GetMyBanQuery : IQuery<MyBanDto?>;

/// <summary>finds the ban in force on an owner: by account, or by the national ID on the account</summary>
public class BanChecker(IAppDbContext db, TimeProvider clock)
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Riyadh).DateTime);

    public async Task<Ban?> ActiveForAsync(User u, CancellationToken ct)
    {
        var today = Today;
        var nid = u.NationalId;
        return await db.Bans.AsNoTracking()
            .Where(b => b.LiftedAt == null && b.StartsOn <= today && (b.EndsOn == null || b.EndsOn >= today) && (b.UserId == u.Id || (nid != null && b.NationalId == nid)))
            .OrderByDescending(b => b.EndsOn == null).ThenByDescending(b => b.EndsOn).FirstOrDefaultAsync(ct);
    }

    public static string Message(Ban b) => b.EndsOn is { } e
        ? $"حسابك محظور من المزايدة والشراء والبيع وتقديم الطلبات حتى {e:yyyy/MM/dd}. السبب: {b.Reason}"
        : $"حسابك محظور من المزايدة والشراء والبيع وتقديم الطلبات. السبب: {b.Reason}";
}

public class BanHandlers(IAppDbContext db, Access access, Notifier notifier, BanChecker checker, TimeProvider clock)
    : IRequestHandler<ListBansQuery, BanPageDto>, IRequestHandler<CreateBanCommand, BanDto>, IRequestHandler<UpdateBanCommand, BanDto>,
      IRequestHandler<LiftBanCommand, BanDto>, IRequestHandler<GetMyBanQuery, MyBanDto?>
{
    private const string OwnerLink = "/dashboard";
    private DateTimeOffset Now => clock.GetUtcNow();

    private static string Clean(string? s) => (s ?? "").Trim();
    private static string? NationalIdOf(string? s) { var d = new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); return d.Length == 0 ? null : d; }

    private async Task<User> RequireAnyAsync(CancellationToken ct, params string[] perms)
    {
        var u = await access.StaffAsync(ct);
        foreach (var p in perms) if (await access.CanAsync(u, p, ct)) return u;
        throw DomainException.Forbidden($"ليست لديك صلاحية «{Permissions.Label(perms[0])}»، تواصل مع مدير النظام");
    }

    private async Task<List<BanDto>> DtosAsync(List<Ban> rows, bool full, CancellationToken ct)
    {
        var today = checker.Today;
        var ids = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Phone, u.NationalId }).ToDictionaryAsync(u => u.Id, ct);
        return rows.Select(b =>
        {
            var u = b.UserId is { } id ? users.GetValueOrDefault(id) : null;
            var nid = b.NationalId ?? u?.NationalId;
            return new BanDto(b.Id, b.UserId, b.Name, full ? nid : OwnersHandlers.Mask(nid), u == null ? null : full ? u.Phone : OwnersHandlers.Mask(u.Phone),
                b.Reason, b.StartsOn, b.EndsOn, b.StatusOn(today), b.Source, b.CreatedAt, b.CreatedByName, b.LiftedAt, b.LiftedByName, b.LiftReason);
        }).ToList();
    }

    private async Task<BanDto> DtoAsync(Ban b, User me, CancellationToken ct) => (await DtosAsync([b], await access.CanAsync(me, Permissions.OwnersFull, ct), ct))[0];

    private static void CheckDates(DateOnly start, DateOnly? end)
    {
        if (start.Year < 2000 || start.Year > 2100) throw new DomainException("تاريخ بداية الحظر غير صحيح");
        if (end is { } e && e < start) throw new DomainException("تاريخ نهاية الحظر قبل بدايته");
    }

    private static string Reason(string? r)
    {
        var s = Clean(r);
        if (s.Length < 3) throw new DomainException("اكتب سبب الحظر");
        if (s.Length > 500) throw new DomainException("سبب الحظر طويل (500 حرف كحد أقصى)");
        return s;
    }

    private string Period(Ban b) => b.EndsOn is { } e ? $"من {b.StartsOn:yyyy/MM/dd} حتى {e:yyyy/MM/dd}" : $"من {b.StartsOn:yyyy/MM/dd} حتى إشعار آخر";

    public async Task<BanPageDto> Handle(ListBansQuery q, CancellationToken ct)
    {
        var me = await RequireAnyAsync(ct, Permissions.BansView, Permissions.BansManage);
        var full = await access.CanAsync(me, Permissions.OwnersFull, ct);
        var today = checker.Today;
        var x = db.Bans.AsNoTracking();
        var term = q.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 100) term = term[..100];
            var phone = Phones.FromInput(term);
            var nid = NationalIdOf(term);
            var who = db.Users.AsNoTracking().Where(u => u.FullName.Contains(term) || (phone != null && u.Phone == phone) || (nid != null && nid.Length >= 5 && u.NationalId == nid)).Select(u => u.Id);
            x = x.Where(b => b.Name.Contains(term) || (nid != null && b.NationalId == nid) || (b.UserId != null && who.Contains(b.UserId.Value)));
        }
        var all = await x.ToListAsync(ct);
        var counts = new BanCountsDto(all.Count, all.Count(b => b.StatusOn(today) == BanStatuses.Active), all.Count(b => b.StatusOn(today) == BanStatuses.Scheduled),
            all.Count(b => b.StatusOn(today) == BanStatuses.Ended), all.Count(b => b.StatusOn(today) == BanStatuses.Lifted));
        var status = q.Status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            if (!BanStatuses.All.Contains(status)) throw new DomainException("حالة غير معروفة");
            all = all.Where(b => b.StatusOn(today) == status).ToList();
        }
        var size = Math.Clamp(q.Size, 10, 200);
        var page = Math.Max(1, q.Page);
        var rows = all.OrderByDescending(b => b.StatusOn(today) == BanStatuses.Active).ThenByDescending(b => b.CreatedAt).Skip((page - 1) * size).Take(size).ToList();
        return new BanPageDto(await DtosAsync(rows, full, ct), all.Count, page, size, counts);
    }

    public async Task<BanDto> Handle(CreateBanCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.BansManage, ct);
        var reason = Reason(c.Reason);
        CheckDates(c.StartsOn, c.EndsOn);
        User? owner = null;
        if (c.OwnerId is { } oid) owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == oid && u.Role == Roles.Owner, ct) ?? throw DomainException.NotFound("المالك غير موجود");
        else if (!string.IsNullOrWhiteSpace(c.OwnerPhoneOrNationalId))
        {
            var t = c.OwnerPhoneOrNationalId.Trim();
            var phone = Phones.FromInput(t);
            var nid = NationalIdOf(t);
            owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == Roles.Owner && ((phone != null && u.Phone == phone) || (nid != null && nid.Length >= 5 && u.NationalId == nid)), ct);
        }
        string? nationalId = owner?.NationalId;
        if (owner == null)
        {
            if (!string.IsNullOrWhiteSpace(c.OwnerPhoneOrNationalId)) throw DomainException.NotFound("لا يوجد مالك مسجل بهذا الجوال أو رقم الهوية. لحظر شخص غير مسجل اكتب رقم هويته واسمه");
            nationalId = NationalIdOf(c.NationalIdOnly) ?? throw new DomainException("اختر المالك، أو اكتب رقم هوية شخص غير مسجل");
            if (nationalId.Length < 5 || nationalId.Length > 20) throw new DomainException("رقم الهوية غير صحيح");
            var nid = nationalId;
            owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == Roles.Owner && u.NationalId == nid, ct);
        }
        var name = owner?.FullName ?? Clean(c.Name);
        if (name.Length < 3) throw new DomainException("اكتب اسم الشخص المحظور");
        if (name.Length > 150) name = name[..150];
        var b = new Ban
        {
            UserId = owner?.Id, NationalId = nationalId, Name = name, Reason = reason, StartsOn = c.StartsOn, EndsOn = c.EndsOn,
            Source = BanSources.Manual, CreatedAt = Now, CreatedByName = me.FullName,
        };
        db.Bans.Add(b);
        if (owner != null)
            await notifier.NotifyAsync(owner.Id, NotificationKinds.Ban, "حُظر حسابك من المشاركة في المنصة",
                $"لا يمكنك المزايدة أو الشراء أو البيع أو تقديم طلبات جديدة {Period(b)}. السبب: {reason}. يمكنك الدخول وسداد مستحقاتك.", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(b, me, ct);
    }

    public async Task<BanDto> Handle(UpdateBanCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.BansManage, ct);
        var reason = Reason(c.Reason);
        CheckDates(c.StartsOn, c.EndsOn);
        var b = await db.Bans.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("الحظر غير موجود");
        if (b.LiftedAt != null) throw new DomainException("رُفع هذا الحظر، أنشئ حظرًا جديدًا");
        b.Reason = reason; b.StartsOn = c.StartsOn; b.EndsOn = c.EndsOn;
        if (b.UserId is { } uid)
            await notifier.NotifyAsync(uid, NotificationKinds.Ban, "عُدّلت مدة حظر حسابك", $"الحظر {Period(b)}. السبب: {reason}", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(b, me, ct);
    }

    public async Task<BanDto> Handle(LiftBanCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.BansManage, ct);
        var reason = Clean(c.Reason);
        if (reason.Length < 3) throw new DomainException("اكتب سبب رفع الحظر");
        if (reason.Length > 500) throw new DomainException("السبب طويل (500 حرف كحد أقصى)");
        var b = await db.Bans.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("الحظر غير موجود");
        if (b.LiftedAt != null) throw new DomainException("رُفع هذا الحظر مسبقًا");
        var wasInForce = b.InForce(checker.Today);
        b.LiftedAt = Now; b.LiftedByName = me.FullName; b.LiftReason = reason;
        if (b.UserId is { } uid && wasInForce)
            await notifier.NotifyAsync(uid, NotificationKinds.Ban, "رُفع الحظر عن حسابك", "يمكنك المزايدة والشراء والبيع وتقديم الطلبات من جديد.", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(b, me, ct);
    }

    public async Task<MyBanDto?> Handle(GetMyBanQuery q, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        if (me.Role != Roles.Owner) return null;
        var b = await checker.ActiveForAsync(me, ct);
        return b == null ? null : new MyBanDto(b.Reason, b.StartsOn, b.EndsOn);
    }
}
