using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Application.Features.Admin;
using Sammha.Application.Features.Finance;
using Sammha.Application.Features.Logos;
using Sammha.Domain;

namespace Sammha.Application.Features.Violations;

public record ViolationTypeDto(Guid Id, string Name, string? NameEn, decimal Amount, bool Active, bool AllowsNameResolution, DateTimeOffset CreatedAt, int Used);

/// <summary>a festival a violation can be issued at; Used = violations issued at it (a used festival is switched off, not deleted)</summary>
public record FestivalDto(Guid Id, string Name, string? NameEn, int? Year, DateOnly? StartsOn, DateOnly? EndsOn, bool Active, DateTimeOffset CreatedAt, int Used);

/// <summary>an optional evidence file: PDF, PNG, JPEG or WebP up to 5 MB (base64, a data: URL prefix is accepted)</summary>
public record ViolationAttachmentInput(string? FileName, string? ContentType, string? DataBase64);

/// <summary>
/// OwnerPhone is masked unless the staff member has «owners.full»; the owner's own view leaves out the staff member's name and the owner's phone.
/// Paid violations carry the receipt: the card payment, its channel and the paid date (no tax invoice: fines are not subject to VAT).
/// </summary>
public record ViolationDto(Guid Id, string Number, Guid OwnerId, string OwnerName, string? OwnerPhone, Guid TypeId, string TypeName, string? TypeNameEn, decimal Amount,
    string Description, string? AttachmentName, string? AttachmentType, string Status, DateTimeOffset IssuedAt, string? IssuedByName,
    DateTimeOffset? PaidAt, Guid? PaymentId, string? PaymentMethod, DateTimeOffset? CancelledAt, string? CancelReason, string? CancelledByName,
    Guid? FestivalId, string? FestivalName, string? CamelChip, bool AllowsNameResolution, string? Resolution, DateTimeOffset? ResolvedAt, string? ResolvedByName, string? ResolutionNote);

public record ViolationCountsDto(int All, int Unpaid, int Paid, int Recorded, int Cancelled, int ResolvedName, int Banned);
public record ViolationPageDto(List<ViolationDto> Items, int Total, int Page, int Size, ViolationCountsDto Counts);
/// <summary>the owner a violation is issued to, found by mobile or identity number (masked unless «owners.full»)</summary>
/// <summary>Chips: the first chip number of each of the owner's camels (KACF), to pick the camel the violation is about</summary>
public record ViolationOwnerDto(Guid Id, string FullName, string Phone, string? NationalId, List<string> Chips);

// ───── settings: violation types (settings.manage) ─────
/// <summary>staff who issue, view or configure violations; ActiveOnly for the issue form</summary>
public record ListViolationTypesQuery(bool ActiveOnly = false) : IQuery<List<ViolationTypeDto>>;
public record SaveViolationTypeCommand(Guid? Id, string Name, string? NameEn, decimal Amount, bool Active, bool AllowsNameResolution = false) : ICommand<ViolationTypeDto>;
// ───── settings: festivals (settings.manage) ─────
public record ListFestivalsQuery(bool ActiveOnly = false) : IQuery<List<FestivalDto>>;
public record SaveFestivalCommand(Guid? Id, string Name, string? NameEn, int? Year, DateOnly? StartsOn, DateOnly? EndsOn, bool Active) : ICommand<FestivalDto>;
/// <summary>a festival violations were issued at cannot be deleted (switch it off instead)</summary>
public record DeleteFestivalCommand(Guid Id) : ICommand<MediatR.Unit>;
/// <summary>a type already used by violations cannot be deleted (switch it off instead)</summary>
public record DeleteViolationTypeCommand(Guid Id) : ICommand<MediatR.Unit>;

// ───── staff ─────
public record ListViolationsQuery(string? Q, string? Status, int Page = 1, int Size = 50, Guid? FestivalId = null) : IQuery<ViolationPageDto>;
public record GetViolationQuery(Guid Id) : IQuery<ViolationDto>;
public record GetViolationAttachmentQuery(Guid Id) : IQuery<DocumentDto>;
public record FindViolationOwnerQuery(string Q) : IQuery<ViolationOwnerDto>;
/// <summary>OwnerId, or the owner's mobile or identity number (named so the activity log leaves it out)</summary>
public record IssueViolationCommand(Guid? OwnerId, string? OwnerPhoneOrNationalId, Guid TypeId, string Description, ViolationAttachmentInput? Attachment,
    Guid? FestivalId = null, string? CamelChip = null) : ICommand<ViolationDto>;
/// <summary>only an unpaid or recorded violation (paid, settled and banned are final)</summary>
public record CancelViolationCommand(Guid Id, string Reason) : ICommand<ViolationDto>;
/// <summary>«تسوية بتسجيل اسم»: the owner registered a name for the camel — an unpaid violation of a type that allows it closes with no payment</summary>
public record ResolveViolationByNameCommand(Guid Id, string? Note) : ICommand<ViolationDto>;
/// <summary>«منع من المشاركة»: the camel is barred from the festival (unpaid or recorded violations)</summary>
public record BanViolationCommand(Guid Id, string Reason) : ICommand<ViolationDto>;

// ───── owner ─────
public record GetMyViolationsQuery : IQuery<List<ViolationDto>>;
public record GetMyViolationAttachmentQuery(Guid Id) : IQuery<DocumentDto>;
/// <summary>completes the fine after the card payment (purpose "violation"); the wallet cannot pay fines</summary>
public record PayViolationCommand(Guid Id) : ICommand<ViolationDto>;

public class ViolationHandlers(IAppDbContext db, Access access, Notifier notifier, Money money, TimeProvider clock)
    : IRequestHandler<ListViolationTypesQuery, List<ViolationTypeDto>>, IRequestHandler<SaveViolationTypeCommand, ViolationTypeDto>, IRequestHandler<DeleteViolationTypeCommand, MediatR.Unit>,
      IRequestHandler<ListViolationsQuery, ViolationPageDto>, IRequestHandler<GetViolationQuery, ViolationDto>, IRequestHandler<GetViolationAttachmentQuery, DocumentDto>,
      IRequestHandler<FindViolationOwnerQuery, ViolationOwnerDto>, IRequestHandler<IssueViolationCommand, ViolationDto>, IRequestHandler<CancelViolationCommand, ViolationDto>,
      IRequestHandler<ResolveViolationByNameCommand, ViolationDto>, IRequestHandler<BanViolationCommand, ViolationDto>,
      IRequestHandler<GetMyViolationsQuery, List<ViolationDto>>, IRequestHandler<GetMyViolationAttachmentQuery, DocumentDto>, IRequestHandler<PayViolationCommand, ViolationDto>
{
    public const int MaxAttachmentBytes = 5 * 1024 * 1024;
    private const string OwnerLink = "/dashboard/violations";
    private DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>PDF, or PNG / JPEG / WebP, by the file's first bytes</summary>
    public static string? Sniff(byte[] b) =>
        b.Length > 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 ? "application/pdf" : LogoHandlers.Sniff(b);

    private static string Clip(string? s, int max) { s = (s ?? "").Trim(); return s.Length > max ? s[..max] : s; }

    /// <summary>a violation row without its file bytes</summary>
    private sealed record Row(Guid Id, string Number, Guid UserId, Guid TypeId, string TypeName, decimal Amount, string Description, string? AttachmentName, string? AttachmentType,
        string Status, DateTimeOffset IssuedAt, string IssuedByName, DateTimeOffset? PaidAt, Guid? PaymentId, DateTimeOffset? CancelledAt, string? CancelReason, string? CancelledByName,
        Guid? FestivalId, string? FestivalName, string? CamelChip, string? Resolution, DateTimeOffset? ResolvedAt, string? ResolvedByName, string? ResolutionNote);

    /// <summary>the final projection: filter on db.Violations first, then project (EF cannot filter on a constructor projection)</summary>
    private static IQueryable<Row> Project(IQueryable<Violation> x) => x.Select(v => new Row(v.Id, v.Number, v.UserId, v.TypeId, v.TypeName, v.Amount, v.Description, v.AttachmentName, v.AttachmentType,
        v.Status, v.IssuedAt, v.IssuedByName, v.PaidAt, v.PaymentId, v.CancelledAt, v.CancelReason, v.CancelledByName,
        v.FestivalId, v.FestivalName, v.CamelChip, v.Resolution, v.ResolvedAt, v.ResolvedByName, v.ResolutionNote));

    private static Row RowOf(Violation v) => new(v.Id, v.Number, v.UserId, v.TypeId, v.TypeName, v.Amount, v.Description, v.AttachmentName, v.AttachmentType,
        v.Status, v.IssuedAt, v.IssuedByName, v.PaidAt, v.PaymentId, v.CancelledAt, v.CancelReason, v.CancelledByName,
        v.FestivalId, v.FestivalName, v.CamelChip, v.Resolution, v.ResolvedAt, v.ResolvedByName, v.ResolutionNote);

    /// <summary>builds the DTOs with owner names, English type names and payment channels</summary>
    private async Task<List<ViolationDto>> DtosAsync(List<Row> rows, bool staff, bool fullPhone, CancellationToken ct)
    {
        var users = rows.Select(r => r.UserId).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Phone }).ToDictionaryAsync(u => u.Id, ct);
        var typeIds = rows.Select(r => r.TypeId).Distinct().ToList();
        var types = await db.ViolationTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).Select(t => new { t.Id, t.NameEn, t.AllowsNameResolution }).ToDictionaryAsync(t => t.Id, ct);
        var pays = rows.Where(r => r.PaymentId != null).Select(r => r.PaymentId!.Value).ToList();
        var methods = pays.Count == 0 ? new Dictionary<Guid, string?>() : await db.Payments.AsNoTracking().Where(p => pays.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Method, ct);
        return rows.Select(r =>
        {
            var o = owners.GetValueOrDefault(r.UserId);
            var phone = !staff || o == null ? null : fullPhone ? o.Phone : OwnersHandlers.Mask(o.Phone);
            var t = types.GetValueOrDefault(r.TypeId);
            return new ViolationDto(r.Id, r.Number, r.UserId, o?.FullName ?? "", phone, r.TypeId, r.TypeName, t?.NameEn, r.Amount, r.Description,
                r.AttachmentName, r.AttachmentType, r.Status, r.IssuedAt, staff ? r.IssuedByName : null, r.PaidAt, r.PaymentId,
                r.PaymentId is { } pid ? methods.GetValueOrDefault(pid) : null, r.CancelledAt, r.CancelReason, staff ? r.CancelledByName : null,
                r.FestivalId, r.FestivalName, r.CamelChip, t?.AllowsNameResolution ?? false, r.Resolution, r.ResolvedAt, staff ? r.ResolvedByName : null, r.ResolutionNote);
        }).ToList();
    }

    private async Task<ViolationDto> StaffDtoAsync(Violation v, User me, CancellationToken ct) =>
        (await DtosAsync([RowOf(v)], true, await access.CanAsync(me, Permissions.OwnersFull, ct), ct))[0];

    private async Task<ViolationDto> OwnerDtoAsync(Violation v, CancellationToken ct) => (await DtosAsync([RowOf(v)], false, false, ct))[0];

    /// <summary>the signed-in staff member, if they hold one of these permissions</summary>
    private async Task<User> RequireAnyAsync(CancellationToken ct, params string[] perms)
    {
        var u = await access.StaffAsync(ct);
        foreach (var p in perms) if (await access.CanAsync(u, p, ct)) return u;
        throw DomainException.Forbidden($"ليست لديك صلاحية «{Permissions.Label(perms[0])}»، تواصل مع مدير النظام");
    }

    // ───── violation types ─────

    public async Task<List<ViolationTypeDto>> Handle(ListViolationTypesQuery q, CancellationToken ct)
    {
        await RequireAnyAsync(ct, Permissions.SettingsManage, Permissions.ViolationsManage, Permissions.ViolationsView);
        var x = db.ViolationTypes.AsNoTracking();
        if (q.ActiveOnly) x = x.Where(t => t.Active);
        var list = await x.OrderBy(t => t.Name).ToListAsync(ct);
        var used = await db.Violations.AsNoTracking().GroupBy(v => v.TypeId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        return list.Select(t => new ViolationTypeDto(t.Id, t.Name, t.NameEn, t.Amount, t.Active, t.AllowsNameResolution, t.CreatedAt, used.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<ViolationTypeDto> Handle(SaveViolationTypeCommand c, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.SettingsManage, ct);
        var name = Clip(c.Name, 200);
        if (name.Length < 3) throw new DomainException("اكتب اسم المخالفة");
        if (name.Length > 100) throw new DomainException("اسم المخالفة طويل (100 حرف كحد أقصى)");
        var en = Clip(c.NameEn, 200);
        if (en.Length > 100) throw new DomainException("الاسم الإنجليزي طويل (100 حرف كحد أقصى)");
        if (c.Amount < 0 || c.Amount > 1_000_000) throw new DomainException("قيمة الغرامة بين 0 و1,000,000 ريال");
        var amount = Money.Round2(c.Amount);
        await using var uow = await db.BeginAsync(["violation-types"], ct);
        if (await db.ViolationTypes.AnyAsync(t => t.Name == name && t.Id != c.Id, ct)) throw new DomainException("يوجد نوع مخالفة بهذا الاسم");
        ViolationType t;
        if (c.Id is { } id) t = await db.ViolationTypes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("نوع المخالفة غير موجود");
        else { t = new ViolationType { CreatedAt = Now }; db.ViolationTypes.Add(t); }
        // violations already issued keep the name and amount they were issued with
        t.Name = name; t.NameEn = en.Length == 0 ? null : en; t.Amount = amount; t.Active = c.Active; t.AllowsNameResolution = c.AllowsNameResolution;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        var used = await db.Violations.CountAsync(v => v.TypeId == t.Id, ct);
        return new ViolationTypeDto(t.Id, t.Name, t.NameEn, t.Amount, t.Active, t.AllowsNameResolution, t.CreatedAt, used);
    }

    public async Task<MediatR.Unit> Handle(DeleteViolationTypeCommand c, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.SettingsManage, ct);
        await using var uow = await db.BeginAsync(["violation-types"], ct);
        var t = await db.ViolationTypes.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("نوع المخالفة غير موجود");
        if (await db.Violations.AnyAsync(v => v.TypeId == t.Id, ct)) throw new DomainException("صدرت مخالفات من هذا النوع، أوقفه بدل حذفه");
        db.ViolationTypes.Remove(t);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return MediatR.Unit.Value;
    }

    // ───── staff: list, view, issue, cancel ─────

    public async Task<ViolationPageDto> Handle(ListViolationsQuery q, CancellationToken ct)
    {
        var me = await RequireAnyAsync(ct, Permissions.ViolationsView, Permissions.ViolationsManage);
        var full = await access.CanAsync(me, Permissions.OwnersFull, ct);
        var size = Math.Clamp(q.Size, 10, 200);
        var page = Math.Max(1, q.Page);
        var x = db.Violations.AsNoTracking();
        var term = q.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 100) term = term[..100];
            var phone = Phones.FromInput(term);
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var who = db.Users.AsNoTracking().Where(u => u.Role == Roles.Owner && (u.FullName.Contains(term) || (phone != null && u.Phone == phone)
                || (digits.Length >= 4 && (u.Phone.Contains(digits) || u.NationalId == digits)))).Select(u => u.Id);
            x = x.Where(v => v.Number.Contains(term) || v.TypeName.Contains(term) || who.Contains(v.UserId));
        }
        if (q.FestivalId is { } fid) x = x.Where(v => v.FestivalId == fid);
        var counts = await x.GroupBy(v => v.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var status = q.Status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            if (!ViolationStatuses.All.Contains(status)) throw new DomainException("حالة غير معروفة");
            x = x.Where(v => v.Status == status);
        }
        var total = await x.CountAsync(ct);
        var rows = await Project(x.OrderByDescending(v => v.IssuedAt).Skip((page - 1) * size).Take(size)).ToListAsync(ct);
        int N(string s) => counts.GetValueOrDefault(s);
        return new ViolationPageDto(await DtosAsync(rows, true, full, ct), total, page, size,
            new ViolationCountsDto(counts.Values.Sum(), N(ViolationStatuses.Unpaid), N(ViolationStatuses.Paid), N(ViolationStatuses.Recorded), N(ViolationStatuses.Cancelled),
                N(ViolationStatuses.ResolvedName), N(ViolationStatuses.Banned)));
    }

    public async Task<ViolationDto> Handle(GetViolationQuery q, CancellationToken ct)
    {
        var me = await RequireAnyAsync(ct, Permissions.ViolationsView, Permissions.ViolationsManage);
        var row = await Project(db.Violations.AsNoTracking().Where(v => v.Id == q.Id)).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("المخالفة غير موجودة");
        return (await DtosAsync([row], true, await access.CanAsync(me, Permissions.OwnersFull, ct), ct))[0];
    }

    public async Task<DocumentDto> Handle(GetViolationAttachmentQuery q, CancellationToken ct)
    {
        await RequireAnyAsync(ct, Permissions.ViolationsView, Permissions.ViolationsManage);
        return await AttachmentAsync(db.Violations.Where(v => v.Id == q.Id), ct);
    }

    private static async Task<DocumentDto> AttachmentAsync(IQueryable<Violation> x, CancellationToken ct)
    {
        var f = await x.AsNoTracking().Where(v => v.AttachmentBytes != null).Select(v => new { v.AttachmentName, v.AttachmentType, v.AttachmentBytes }).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("لا يوجد مرفق لهذه المخالفة");
        return new DocumentDto(f.AttachmentName ?? "attachment", f.AttachmentType ?? "application/octet-stream", Convert.ToBase64String(f.AttachmentBytes!));
    }

    private async Task<User?> FindOwnerAsync(string? q, CancellationToken ct)
    {
        var t = q?.Trim() ?? "";
        if (t.Length == 0) return null;
        var digits = new string(t.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var phone = Phones.FromInput(t);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == Roles.Owner && ((phone != null && u.Phone == phone) || (digits.Length >= 5 && u.NationalId == digits)), ct);
    }

    public async Task<ViolationOwnerDto> Handle(FindViolationOwnerQuery q, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ViolationsManage, ct);
        var u = await FindOwnerAsync(q.Q, ct) ?? throw DomainException.NotFound("لا يوجد مالك مسجل بهذا الجوال أو رقم الهوية");
        var full = await access.CanAsync(me, Permissions.OwnersFull, ct);
        var chips = (await db.OwnedCamels.AsNoTracking().Where(c => c.UserId == u.Id).OrderBy(c => c.CreatedAt).ToListAsync(ct))
            .Select(c => Camels.CamelLinks.Chips(c).FirstOrDefault()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        return new ViolationOwnerDto(u.Id, u.FullName, full ? u.Phone : OwnersHandlers.Mask(u.Phone)!, full ? u.NationalId : OwnersHandlers.Mask(u.NationalId), chips);
    }

    public async Task<ViolationDto> Handle(IssueViolationCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ViolationsManage, ct);
        var owner = c.OwnerId is { } oid
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == oid && u.Role == Roles.Owner, ct)
            : await FindOwnerAsync(c.OwnerPhoneOrNationalId, ct);
        if (owner == null) throw new DomainException("اختر المالك (بالجوال أو رقم الهوية)");
        var type = await db.ViolationTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == c.TypeId, ct) ?? throw new DomainException("اختر نوع المخالفة");
        if (!type.Active) throw new DomainException("نوع المخالفة هذا موقوف");
        var description = (c.Description ?? "").Trim();
        if (description.Length < 10) throw new DomainException("اكتب وصف المخالفة (10 أحرف على الأقل)");
        if (description.Length > 2000) throw new DomainException("الوصف طويل (2000 حرف كحد أقصى)");
        Festival? festival = null;
        if (c.FestivalId is { } fid)
        {
            festival = await db.Festivals.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fid, ct) ?? throw new DomainException("المهرجان غير موجود");
            if (!festival.Active) throw new DomainException("هذا المهرجان موقوف");
        }
        var chip = (c.CamelChip ?? "").Trim();
        if (chip.Length > 40 || chip.Any(ch => !char.IsLetterOrDigit(ch))) throw new DomainException("رقم الشريحة أرقام وحروف فقط (40 خانة كحد أقصى)");

        byte[]? bytes = null;
        string? fileType = null, fileName = null;
        if (c.Attachment is { DataBase64: { Length: > 0 } data })
        {
            try { bytes = Convert.FromBase64String(data.Contains(',') ? data[(data.IndexOf(',') + 1)..] : data); }
            catch (FormatException) { throw new DomainException("تعذّرت قراءة المرفق، أعد رفعه"); }
            if (bytes.Length == 0) bytes = null;
            else
            {
                if (bytes.Length > MaxAttachmentBytes) throw new DomainException("حجم المرفق أكبر من 5 ميجابايت");
                fileType = Sniff(bytes) ?? throw new DomainException("المرفق يكون PDF أو صورة PNG أو JPG أو WebP");
                var n = Path.GetFileName(c.Attachment.FileName ?? "").Trim();
                fileName = Clip(n.Length == 0 ? "attachment" : n, 120);
            }
        }

        var v = new Violation
        {
            Number = $"VL-{Now.Year}-{await db.NextNumberAsync("ViolationNo", ct):D5}", UserId = owner.Id, TypeId = type.Id, TypeName = type.Name, Amount = type.Amount,
            Description = description, FestivalId = festival?.Id, FestivalName = festival?.Name, CamelChip = chip.Length == 0 ? null : chip,
            AttachmentName = fileName, AttachmentType = fileType, AttachmentBytes = bytes,
            // a penalty without a fine has nothing to pay
            Status = type.Amount > 0 ? ViolationStatuses.Unpaid : ViolationStatuses.Recorded,
            IssuedById = me.Id, IssuedByName = me.FullName, IssuedAt = Now,
        };
        db.Violations.Add(v);
        await notifier.NotifyAsync(owner.Id, NotificationKinds.Violation, $"صدرت بحقك مخالفة «{type.Name}» ({v.Number})",
            v.Amount > 0 ? $"قيمة الغرامة {v.Amount:0.##} ريال. اطّلع على التفاصيل وسدّدها من صفحة المخالفات." : "مخالفة مسجلة دون غرامة مالية. اطّلع على التفاصيل من صفحة المخالفات.", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        return await StaffDtoAsync(v, me, ct);
    }

    public async Task<ViolationDto> Handle(CancelViolationCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ViolationsManage, ct);
        var reason = (c.Reason ?? "").Trim();
        if (reason.Length < 3) throw new DomainException("اكتب سبب الإلغاء، يصل للمالك");
        if (reason.Length > 500) throw new DomainException("سبب الإلغاء طويل (500 حرف كحد أقصى)");
        await using var uow = await db.BeginAsync([$"vl:{c.Id}"], ct);
        var v = await db.Violations.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("المخالفة غير موجودة");
        if (v.Status == ViolationStatuses.Cancelled) throw new DomainException("أُلغيت هذه المخالفة مسبقًا");
        if (v.Status == ViolationStatuses.Paid) throw new DomainException("سُدّدت هذه المخالفة ولا يمكن إلغاؤها");
        if (v.Status != ViolationStatuses.Unpaid && v.Status != ViolationStatuses.Recorded) throw new DomainException(Closed(v));
        v.Status = ViolationStatuses.Cancelled;
        v.CancelledAt = Now;
        v.CancelReason = reason;
        v.CancelledByName = me.FullName;
        await notifier.NotifyAsync(v.UserId, NotificationKinds.Violation, $"أُلغيت المخالفة {v.Number}", $"«{v.TypeName}» — السبب: {reason}", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await StaffDtoAsync(v, me, ct);
    }

    // ───── owner ─────

    public async Task<List<ViolationDto>> Handle(GetMyViolationsQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var rows = await Project(db.Violations.AsNoTracking().Where(v => v.UserId == u.Id).OrderByDescending(v => v.IssuedAt).Take(500)).ToListAsync(ct);
        return await DtosAsync(rows, false, false, ct);
    }

    public async Task<DocumentDto> Handle(GetMyViolationAttachmentQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (!await db.Violations.AnyAsync(v => v.Id == q.Id && v.UserId == u.Id, ct)) throw DomainException.NotFound("المخالفة غير موجودة");
        return await AttachmentAsync(db.Violations.Where(v => v.Id == q.Id && v.UserId == u.Id), ct);
    }

    public async Task<ViolationDto> Handle(PayViolationCommand c, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"vl:{c.Id}", $"u:{u.Id}"], ct);
        var v = await db.Violations.FirstOrDefaultAsync(x => x.Id == c.Id && x.UserId == u.Id, ct) ?? throw DomainException.NotFound("المخالفة غير موجودة");
        if (v.Status == ViolationStatuses.Paid) return await OwnerDtoAsync(v, ct);
        if (v.Status == ViolationStatuses.Cancelled) throw new DomainException("أُلغيت هذه المخالفة");
        if (v.Status is ViolationStatuses.Banned or ViolationStatuses.ResolvedName) throw new DomainException(Closed(v));
        if (v.Status != ViolationStatuses.Unpaid) throw new DomainException("لا توجد غرامة مستحقة على هذه المخالفة");
        // fines are not subject to VAT: no tax invoice, the card payment is the receipt
        var p = await money.DebitByCardAsync(u.Id, v.Amount, PaymentPurposes.Violation, v.Id, TxKinds.Fine, $"سداد المخالفة {v.Number} — {v.TypeName}", ct);
        v.Status = ViolationStatuses.Paid;
        v.PaidAt = Now;
        v.PaymentId = p.Id;
        v.Resolution = ViolationResolutions.PaidContinue;
        v.ResolvedAt = Now;
        await notifier.NotifyAsync(u.Id, NotificationKinds.Violation, $"تم سداد المخالفة {v.Number}", $"سُدّد {v.Amount:0.##} ريال، ويظهر الإيصال في صفحة المخالفات.", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await OwnerDtoAsync(v, ct);
    }

    /// <summary>why a closed violation cannot change any more</summary>
    private static string Closed(Violation v) => v.Status switch
    {
        ViolationStatuses.Paid => "سُدّدت هذه المخالفة وأُغلقت",
        ViolationStatuses.ResolvedName => "سُوّيت هذه المخالفة بتسجيل اسم وأُغلقت",
        ViolationStatuses.Banned => "صدر قرار المنع من المشاركة في هذه المخالفة وأُغلقت",
        ViolationStatuses.Cancelled => "أُلغيت هذه المخالفة",
        _ => "لا يمكن تغيير حالة هذه المخالفة",
    };

    private static string AtFestival(Violation v) => v.FestivalName is { } f ? $" في «{f}»" : "";

    public async Task<ViolationDto> Handle(ResolveViolationByNameCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ViolationsManage, ct);
        var note = (c.Note ?? "").Trim();
        if (note.Length > 500) throw new DomainException("الملاحظة طويلة (500 حرف كحد أقصى)");
        await using var uow = await db.BeginAsync([$"vl:{c.Id}"], ct);
        var v = await db.Violations.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("المخالفة غير موجودة");
        if (v.Status != ViolationStatuses.Unpaid) throw new DomainException(v.Status == ViolationStatuses.Recorded ? "لا توجد غرامة على هذه المخالفة لتسويتها" : Closed(v));
        if (!await db.ViolationTypes.AnyAsync(t => t.Id == v.TypeId && t.AllowsNameResolution, ct)) throw new DomainException("نوع هذه المخالفة لا يقبل التسوية بتسجيل اسم");
        v.Status = ViolationStatuses.ResolvedName;
        v.Resolution = ViolationResolutions.ResolvedName;
        v.ResolvedAt = Now;
        v.ResolvedByName = me.FullName;
        v.ResolutionNote = note.Length == 0 ? null : note;
        await notifier.NotifyAsync(v.UserId, NotificationKinds.Violation, $"سُوّيت المخالفة {v.Number} بتسجيل اسم",
            $"«{v.TypeName}»{AtFestival(v)}: أُغلقت المخالفة دون سداد بعد تسجيل اسم للجمل، ويمكنك مواصلة المشاركة." + (note.Length > 0 ? $" ملاحظة: {note}" : ""), OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await StaffDtoAsync(v, me, ct);
    }

    public async Task<ViolationDto> Handle(BanViolationCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ViolationsManage, ct);
        var reason = (c.Reason ?? "").Trim();
        if (reason.Length < 3) throw new DomainException("اكتب سبب المنع، يصل للمالك");
        if (reason.Length > 500) throw new DomainException("سبب المنع طويل (500 حرف كحد أقصى)");
        await using var uow = await db.BeginAsync([$"vl:{c.Id}"], ct);
        var v = await db.Violations.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("المخالفة غير موجودة");
        if (v.Status != ViolationStatuses.Unpaid && v.Status != ViolationStatuses.Recorded) throw new DomainException(Closed(v));
        v.Status = ViolationStatuses.Banned;
        v.Resolution = ViolationResolutions.Banned;
        v.ResolvedAt = Now;
        v.ResolvedByName = me.FullName;
        v.ResolutionNote = reason;
        await notifier.NotifyAsync(v.UserId, NotificationKinds.Violation, $"مُنع الجمل من المشاركة{AtFestival(v)} ({v.Number})",
            $"بسبب المخالفة «{v.TypeName}»{(v.CamelChip is { } chip ? $" للشريحة {chip}" : "")}. السبب: {reason}", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await StaffDtoAsync(v, me, ct);
    }
}

/// <summary>the festivals violations are issued at (settings → «المخالفات»)</summary>
public class FestivalHandlers(IAppDbContext db, Access access, TimeProvider clock)
    : IRequestHandler<ListFestivalsQuery, List<FestivalDto>>, IRequestHandler<SaveFestivalCommand, FestivalDto>, IRequestHandler<DeleteFestivalCommand, MediatR.Unit>
{
    private static FestivalDto Dto(Festival f, int used) => new(f.Id, f.Name, f.NameEn, f.Year, f.StartsOn, f.EndsOn, f.Active, f.CreatedAt, used);

    public async Task<List<FestivalDto>> Handle(ListFestivalsQuery q, CancellationToken ct)
    {
        var u = await access.StaffAsync(ct);
        if (!await access.CanAsync(u, Permissions.SettingsManage, ct) && !await access.CanAsync(u, Permissions.ViolationsManage, ct) && !await access.CanAsync(u, Permissions.ViolationsView, ct))
            throw DomainException.Forbidden($"ليست لديك صلاحية «{Permissions.Label(Permissions.ViolationsView)}»، تواصل مع مدير النظام");
        var x = db.Festivals.AsNoTracking();
        if (q.ActiveOnly) x = x.Where(f => f.Active);
        var list = await x.OrderByDescending(f => f.Year).ThenBy(f => f.Name).ToListAsync(ct);
        var used = await db.Violations.AsNoTracking().Where(v => v.FestivalId != null).GroupBy(v => v.FestivalId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        return list.Select(f => Dto(f, used.GetValueOrDefault(f.Id))).ToList();
    }

    public async Task<FestivalDto> Handle(SaveFestivalCommand c, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.SettingsManage, ct);
        var name = (c.Name ?? "").Trim();
        if (name.Length < 3) throw new DomainException("اكتب اسم المهرجان");
        if (name.Length > 150) throw new DomainException("اسم المهرجان طويل (150 حرفًا كحد أقصى)");
        var en = (c.NameEn ?? "").Trim();
        if (en.Length > 150) throw new DomainException("الاسم الإنجليزي طويل (150 حرفًا كحد أقصى)");
        if (c.Year is { } y && (y < 2000 || y > 2100)) throw new DomainException("سنة المهرجان بين 2000 و2100");
        if (c.StartsOn is { } s && c.EndsOn is { } e && e < s) throw new DomainException("تاريخ نهاية المهرجان قبل بدايته");
        await using var uow = await db.BeginAsync(["festivals"], ct);
        if (await db.Festivals.AnyAsync(f => f.Name == name && f.Id != c.Id, ct)) throw new DomainException("يوجد مهرجان بهذا الاسم");
        Festival f;
        if (c.Id is { } id) f = await db.Festivals.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("المهرجان غير موجود");
        else { f = new Festival { CreatedAt = clock.GetUtcNow() }; db.Festivals.Add(f); }
        // violations already issued keep the festival name they were issued with
        f.Name = name; f.NameEn = en.Length == 0 ? null : en; f.Year = c.Year; f.StartsOn = c.StartsOn; f.EndsOn = c.EndsOn; f.Active = c.Active;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return Dto(f, await db.Violations.CountAsync(v => v.FestivalId == f.Id, ct));
    }

    public async Task<MediatR.Unit> Handle(DeleteFestivalCommand c, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.SettingsManage, ct);
        await using var uow = await db.BeginAsync(["festivals"], ct);
        var f = await db.Festivals.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("المهرجان غير موجود");
        if (await db.Violations.AnyAsync(v => v.FestivalId == f.Id, ct)) throw new DomainException("صدرت مخالفات في هذا المهرجان، أوقفه بدل حذفه");
        db.Festivals.Remove(f);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return MediatR.Unit.Value;
    }
}
