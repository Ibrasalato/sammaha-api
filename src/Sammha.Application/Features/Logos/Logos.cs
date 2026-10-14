using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Logos;

public record LogoDto(Guid Id, Guid ReservedId, string Name, string Type, Guid OwnerId, string OwnerName, string FileName, string Status, string? RejectReason,
    DateTimeOffset UploadedAt, DateTimeOffset? ReviewedAt, string? ReviewedBy);
public record LogoFile(byte[] Bytes, string ContentType);

public record UploadLogoCommand(Guid ReservedId, string FileName, string Base64) : ICommand<ReservedNameDto>;
public record DeleteLogoCommand(Guid ReservedId) : ICommand<ReservedNameDto>;
/// <summary>the public image: approved logos of owned names only</summary>
public record GetLogoQuery(Guid ReservedId) : IQuery<LogoFile?>;
/// <summary>the owner sees their logo whatever its status</summary>
public record GetMyLogoQuery(Guid ReservedId) : IQuery<LogoFile?>;
public record ListLogosQuery(string? Status) : IQuery<List<LogoDto>>;
public record GetStaffLogoQuery(Guid Id) : IQuery<LogoFile?>;
public record ReviewLogoCommand(Guid Id, bool Approve, string? Reason) : ICommand<LogoDto>;

public class LogoHandlers(IAppDbContext db, Access access, ICurrentUser current, Notifier notifier, TimeProvider clock)
    : IRequestHandler<UploadLogoCommand, ReservedNameDto>, IRequestHandler<DeleteLogoCommand, ReservedNameDto>, IRequestHandler<GetLogoQuery, LogoFile?>,
      IRequestHandler<GetMyLogoQuery, LogoFile?>, IRequestHandler<ListLogosQuery, List<LogoDto>>, IRequestHandler<GetStaffLogoQuery, LogoFile?>,
      IRequestHandler<ReviewLogoCommand, LogoDto>
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>PNG, JPEG or WebP by their first bytes (no SVG: it can carry scripts)</summary>
    public static string? Sniff(byte[] b) =>
        b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P' ? "image/webp" : null;

    private static LogoDto Dto(NameLogo l, string owner, string type) => new(l.Id, l.ReservedId, l.Name, type, l.OwnerId, owner, l.FileName, l.Status, l.RejectReason, l.UploadedAt, l.ReviewedAt, l.ReviewedBy);

    private async Task<User> OwnerAsync(CancellationToken ct)
    {
        if (current.DelegationId != null) throw DomainException.Forbidden("لوجو الشعار لصاحب الحساب فقط");
        return await access.MeAsync(ct);
    }

    public async Task<ReservedNameDto> Handle(UploadLogoCommand c, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(c.Base64 ?? ""); } catch (FormatException) { throw new DomainException("ملف الصورة غير صالح"); }
        if (bytes.Length == 0) throw new DomainException("اختر صورة اللوجو");
        if (bytes.Length > MaxBytes) throw new DomainException("حجم الصورة أكبر من 2 ميجابايت");
        var type = Sniff(bytes) ?? throw new DomainException("اللوجو بصيغة PNG أو JPG أو WebP");
        await using var uow = await db.BeginAsync([$"n:{c.ReservedId}"], ct);
        var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == c.ReservedId && x.OwnerId == u.Id, ct) ?? throw new DomainException("الاسم غير مسجل باسمك");
        var l = await db.Logos.FirstOrDefaultAsync(x => x.ReservedId == r.Id, ct);
        if (l == null) { l = new NameLogo { ReservedId = r.Id }; db.Logos.Add(l); }
        var file = Path.GetFileName(c.FileName ?? "logo").Trim();
        l.OwnerId = u.Id; l.Name = r.Name; l.Bytes = bytes; l.ContentType = type; l.FileName = file.Length > 120 ? file[..120] : file;
        l.Status = LogoStatuses.Pending; l.RejectReason = null; l.ReviewedAt = null; l.ReviewedBy = null; l.UploadedAt = Now;
        // a new logo shows publicly only after the committee approves it
        r.LogoAt = null;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Mapper.Reserved(r, null, null, l.Status);
    }

    public async Task<ReservedNameDto> Handle(DeleteLogoCommand c, CancellationToken ct)
    {
        var u = await OwnerAsync(ct);
        await using var uow = await db.BeginAsync([$"n:{c.ReservedId}"], ct);
        var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == c.ReservedId && x.OwnerId == u.Id, ct) ?? throw new DomainException("الاسم غير مسجل باسمك");
        await db.Logos.Where(x => x.ReservedId == r.Id).ExecuteDeleteAsync(ct);
        r.LogoAt = null;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Mapper.Reserved(r);
    }

    public async Task<LogoFile?> Handle(GetLogoQuery q, CancellationToken ct) =>
        await db.Logos.AsNoTracking().Where(l => l.ReservedId == q.ReservedId && l.Status == LogoStatuses.Approved
                && db.ReservedNames.Any(r => r.Id == l.ReservedId && r.OwnerId == l.OwnerId))
            .Select(l => new LogoFile(l.Bytes, l.ContentType)).FirstOrDefaultAsync(ct);

    public async Task<LogoFile?> Handle(GetMyLogoQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        return await db.Logos.AsNoTracking().Where(l => l.ReservedId == q.ReservedId && l.OwnerId == u.Id).Select(l => new LogoFile(l.Bytes, l.ContentType)).FirstOrDefaultAsync(ct);
    }

    public async Task<List<LogoDto>> Handle(ListLogosQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.NamesLogos, ct);
        var x = db.Logos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Status)) x = x.Where(l => l.Status == q.Status);
        var rows = await x.OrderByDescending(l => l.UploadedAt).Take(500)
            .Select(l => new { l.Id, l.ReservedId, l.Name, l.OwnerId, l.FileName, l.Status, l.RejectReason, l.UploadedAt, l.ReviewedAt, l.ReviewedBy }).ToListAsync(ct);
        var owners = rows.Select(r => r.OwnerId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var rids = rows.Select(r => r.ReservedId).ToList();
        var types = await db.ReservedNames.AsNoTracking().Where(r => rids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Type, ct);
        return rows.Select(l => new LogoDto(l.Id, l.ReservedId, l.Name, types.GetValueOrDefault(l.ReservedId, NameTypes.Qaadan), l.OwnerId, names.GetValueOrDefault(l.OwnerId, ""),
            l.FileName, l.Status, l.RejectReason, l.UploadedAt, l.ReviewedAt, l.ReviewedBy)).ToList();
    }

    public async Task<LogoFile?> Handle(GetStaffLogoQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.NamesLogos, ct);
        return await db.Logos.AsNoTracking().Where(l => l.Id == q.Id).Select(l => new LogoFile(l.Bytes, l.ContentType)).FirstOrDefaultAsync(ct);
    }

    public async Task<LogoDto> Handle(ReviewLogoCommand c, CancellationToken ct)
    {
        var staff = await access.RequireAsync(Permissions.NamesLogos, ct);
        var reason = c.Reason?.Trim();
        if (!c.Approve && string.IsNullOrWhiteSpace(reason)) throw new DomainException("اكتب سبب الرفض، يصل للمالك");
        if (reason is { Length: > 300 }) reason = reason[..300];
        var rid = await db.Logos.AsNoTracking().Where(l => l.Id == c.Id).Select(l => (Guid?)l.ReservedId).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("اللوجو غير موجود");
        await using var uow = await db.BeginAsync([$"n:{rid}"], ct);
        var l = await db.Logos.FirstAsync(x => x.Id == c.Id, ct);
        var r = await db.ReservedNames.FirstAsync(x => x.Id == l.ReservedId, ct);
        if (r.OwnerId != l.OwnerId) throw new DomainException("لم يعد الاسم مملوكًا لصاحب اللوجو");
        l.Status = c.Approve ? LogoStatuses.Approved : LogoStatuses.Rejected;
        l.RejectReason = c.Approve ? null : reason;
        l.ReviewedAt = Now;
        l.ReviewedBy = staff.FullName;
        r.LogoAt = c.Approve ? Now : null;
        await notifier.NotifyAsync(l.OwnerId, NotificationKinds.Reviewed, c.Approve ? $"اعتُمد لوجو «{l.Name}»" : $"لم يُعتمد لوجو «{l.Name}»",
            c.Approve ? "يظهر اللوجو الآن مع الاسم في سجل الأسماء." : $"السبب: {reason}. يمكنك رفع لوجو آخر.", "/dashboard/names", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        var owner = await db.Users.Where(u => u.Id == l.OwnerId).Select(u => u.FullName).FirstAsync(ct);
        return Dto(l, owner, r.Type);
    }
}
