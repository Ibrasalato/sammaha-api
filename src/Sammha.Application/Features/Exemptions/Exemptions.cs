using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Application.Features.Admin;
using Sammha.Application.Features.Camels;
using Sammha.Domain;

namespace Sammha.Application.Features.Exemptions;

/// <summary>
/// One exemption request and, once paid, its document. OwnerPhone and NationalId are masked unless the staff member has «owners.full»;
/// the owner's own view leaves out the staff names. Signature is only given out once the document is issued.
/// </summary>
public record ExemptionDto(Guid Id, string Number, Guid OwnerId, string OwnerName, string? OwnerPhone, string? NationalId,
    string CamelChip, string CamelType, Guid? FestivalId, string FestivalName, string? Race, string? OwnerNote, string Status, DateTimeOffset CreatedAt,
    decimal? Amount, string? StaffNote, DateTimeOffset? PricedAt, string? PricedByName,
    DateTimeOffset? RejectedAt, string? RejectReason, string? RejectedByName, DateTimeOffset? CancelledAt,
    DateTimeOffset? PaidAt, Guid? PaymentId, string? PaymentMethod,
    DateTimeOffset? IssuedAt, string? Signature, string? OwnerNameEn, DateTimeOffset? RevokedAt, string? RevokeReason, string? RevokedByName);

public record ExemptionCountsDto(int All, int Pending, int Priced, int Paid, int Rejected, int Cancelled);
public record ExemptionPageDto(List<ExemptionDto> Items, int Total, int Page, int Size, ExemptionCountsDto Counts);

/// <summary>an active festival the owner can ask an exemption for</summary>
public record FestivalOptionDto(Guid Id, string Name, string? NameEn, int? Year, DateOnly? StartsOn, DateOnly? EndsOn);

/// <summary>
/// Public verification. With the number alone (they are sequential) only whether it exists and its status;
/// the owner, chip and festival need the code printed on the document (or carried by its QR).
/// </summary>
public record ExemptionVerifyDto(string Number, string Status, bool? SignatureMatches, DateTimeOffset? IssuedAt,
    string? OwnerNameAr, string? OwnerNameEn, string? CamelChip, string? CamelType, string? FestivalName, string? Race, DateTimeOffset? RevokedAt, string? RevokeReason);

// ───── owner ─────
public record ListExemptionFestivalsQuery : IQuery<List<FestivalOptionDto>>;
public record RequestExemptionCommand(string CamelChip, string CamelType, Guid FestivalId, string? Race, string? Note) : ICommand<ExemptionDto>;
public record GetMyExemptionsQuery : IQuery<List<ExemptionDto>>;
public record GetMyExemptionQuery(string Number) : IQuery<ExemptionDto>;
/// <summary>withdraw a request that is not paid yet</summary>
public record CancelMyExemptionCommand(Guid Id) : ICommand<ExemptionDto>;
/// <summary>completes the request after the card payment (purpose "exemption"); the wallet cannot pay fines</summary>
public record PayExemptionCommand(Guid Id) : ICommand<ExemptionDto>;

// ───── staff ─────
public record ListExemptionsQuery(string? Q, string? Status, int Page = 1, int Size = 50, Guid? FestivalId = null) : IQuery<ExemptionPageDto>;
public record GetExemptionQuery(Guid Id) : IQuery<ExemptionDto>;
/// <summary>set (or change, before payment) the fine for not naming the camel</summary>
public record PriceExemptionCommand(Guid Id, decimal Amount, string? Note) : ICommand<ExemptionDto>;
public record RejectExemptionCommand(Guid Id, string Reason) : ICommand<ExemptionDto>;
/// <summary>cancel an issued document (issued in error, for example); the payment is not refunded by this</summary>
public record RevokeExemptionCommand(Guid Id, string Reason) : ICommand<ExemptionDto>;

// ───── anyone ─────
public record VerifyExemptionQuery(string Number, string? Signature) : IQuery<ExemptionVerifyDto?>;

public class ExemptionHandlers(IAppDbContext db, Access access, Notifier notifier, Money money, ICertificateSigner signer, TimeProvider clock)
    : IRequestHandler<ListExemptionFestivalsQuery, List<FestivalOptionDto>>, IRequestHandler<RequestExemptionCommand, ExemptionDto>,
      IRequestHandler<GetMyExemptionsQuery, List<ExemptionDto>>, IRequestHandler<GetMyExemptionQuery, ExemptionDto>,
      IRequestHandler<CancelMyExemptionCommand, ExemptionDto>, IRequestHandler<PayExemptionCommand, ExemptionDto>,
      IRequestHandler<ListExemptionsQuery, ExemptionPageDto>, IRequestHandler<GetExemptionQuery, ExemptionDto>,
      IRequestHandler<PriceExemptionCommand, ExemptionDto>, IRequestHandler<RejectExemptionCommand, ExemptionDto>, IRequestHandler<RevokeExemptionCommand, ExemptionDto>,
      IRequestHandler<VerifyExemptionQuery, ExemptionVerifyDto?>
{
    private const string OwnerLink = "/dashboard/exemptions";
    public const decimal MaxAmount = 1_000_000;
    private DateTimeOffset Now => clock.GetUtcNow();

    private static string Clip(string? s, int max) { s = (s ?? "").Trim(); return s.Length > max ? s[..max] : s; }
    private static string TypeLabel(string t) => t == NameTypes.Bakkar ? "بكرة" : "قعود";

    private async Task<List<ExemptionDto>> DtosAsync(List<Exemption> rows, bool staff, bool full, CancellationToken ct)
    {
        var users = rows.Select(r => r.UserId).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Phone, u.NationalId }).ToDictionaryAsync(u => u.Id, ct);
        var pays = rows.Where(r => r.PaymentId != null).Select(r => r.PaymentId!.Value).ToList();
        var methods = pays.Count == 0 ? new Dictionary<Guid, string?>() : await db.Payments.AsNoTracking().Where(p => pays.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Method, ct);
        return rows.Select(e =>
        {
            var o = owners.GetValueOrDefault(e.UserId);
            string? phone = null, nid;
            if (staff) { phone = full ? o?.Phone : OwnersHandlers.Mask(o?.Phone); nid = full ? o?.NationalId : OwnersHandlers.Mask(o?.NationalId); }
            else nid = OwnersHandlers.Mask(e.NationalId ?? o?.NationalId);
            var issued = e.Status == ExemptionStatuses.Paid;
            return new ExemptionDto(e.Id, e.Number, e.UserId, o?.FullName ?? e.OwnerNameAr ?? "", phone, nid,
                e.CamelChip, e.CamelType, e.FestivalId, e.FestivalName, e.Race, e.OwnerNote, e.Status, e.CreatedAt,
                e.Amount, e.StaffNote, e.PricedAt, staff ? e.PricedByName : null,
                e.RejectedAt, e.RejectReason, staff ? e.RejectedByName : null, e.CancelledAt,
                e.PaidAt, e.PaymentId, e.PaymentId is { } pid ? methods.GetValueOrDefault(pid) : null,
                issued ? e.IssuedAt : null, issued ? e.Signature : null, issued ? e.OwnerNameEn : null, e.RevokedAt, e.RevokeReason, staff ? e.RevokedByName : null);
        }).ToList();
    }

    private async Task<ExemptionDto> OwnerDtoAsync(Exemption e, CancellationToken ct) => (await DtosAsync([e], false, false, ct))[0];
    private async Task<ExemptionDto> StaffDtoAsync(Exemption e, User me, CancellationToken ct) =>
        (await DtosAsync([e], true, await access.CanAsync(me, Permissions.OwnersFull, ct), ct))[0];

    private async Task<User> RequireAnyAsync(CancellationToken ct, params string[] perms)
    {
        var u = await access.StaffAsync(ct);
        foreach (var p in perms) if (await access.CanAsync(u, p, ct)) return u;
        throw DomainException.Forbidden($"ليست لديك صلاحية «{Permissions.Label(perms[0])}»، تواصل مع مدير النظام");
    }

    private static string Closed(Exemption e) => e.Status switch
    {
        ExemptionStatuses.Paid => "سُدّدت الغرامة وصدرت وثيقة الاستثناء",
        ExemptionStatuses.Rejected => "رُفض هذا الطلب",
        ExemptionStatuses.Cancelled => "سُحب هذا الطلب",
        _ => "لا يمكن تغيير حالة هذا الطلب",
    };

    // ───── owner ─────

    public async Task<List<FestivalOptionDto>> Handle(ListExemptionFestivalsQuery q, CancellationToken ct)
    {
        await access.MeAsync(ct);
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        return await db.Festivals.AsNoTracking().Where(f => f.Active && (f.EndsOn == null || f.EndsOn >= today))
            .OrderBy(f => f.StartsOn).ThenBy(f => f.Name)
            .Select(f => new FestivalOptionDto(f.Id, f.Name, f.NameEn, f.Year, f.StartsOn, f.EndsOn)).ToListAsync(ct);
    }

    public async Task<ExemptionDto> Handle(RequestExemptionCommand c, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (u.Role != Roles.Owner) throw DomainException.Forbidden("طلبات الاستثناء للملاك");
        var chip = (c.CamelChip ?? "").Trim().ToUpperInvariant();
        if (chip.Length < 5) throw new DomainException("اكتب رقم شريحة الجمل");
        if (chip.Length > 40 || chip.Any(ch => !char.IsAsciiLetterOrDigit(ch))) throw new DomainException("رقم الشريحة أرقام وحروف إنجليزية فقط (40 خانة كحد أقصى)");
        var type = (c.CamelType ?? "").Trim().ToLowerInvariant();
        if (type is not (NameTypes.Qaadan or NameTypes.Bakkar)) throw new DomainException("اختر نوع الجمل: قعود أو بكرة");
        var festival = await db.Festivals.AsNoTracking().FirstOrDefaultAsync(f => f.Id == c.FestivalId, ct) ?? throw new DomainException("اختر المهرجان");
        if (!festival.Active) throw new DomainException("هذا المهرجان غير متاح لطلبات الاستثناء");
        if (festival.EndsOn is { } end && end < DateOnly.FromDateTime(Now.UtcDateTime)) throw new DomainException("انتهى هذا المهرجان");
        var race = Clip(c.Race, 200);
        if (race.Length > 150) throw new DomainException("اسم الشوط طويل (150 حرفًا كحد أقصى)");
        var note = Clip(c.Note, 600);
        if (note.Length > 500) throw new DomainException("الملاحظة طويلة (500 حرف كحد أقصى)");

        await using var uow = await db.BeginAsync([$"exc:{chip}"], ct);
        // a chip that already carries one of the owner's names takes part with it: no exemption needed
        var camels = await db.OwnedCamels.AsNoTracking().Where(x => x.ReservedId != null && x.ChipNumbers.Contains(chip)).ToListAsync(ct);
        if (camels.Any(x => CamelLinks.Chips(x).Contains(chip, StringComparer.OrdinalIgnoreCase)))
            throw new DomainException("هذه الشريحة مربوطة باسم مسجل، فلا تحتاج استثناء");
        var dup = await db.Exemptions.AsNoTracking()
            .Where(x => x.CamelChip == chip && x.FestivalId == festival.Id && (ExemptionStatuses.Open.Contains(x.Status) || (x.Status == ExemptionStatuses.Paid && x.RevokedAt == null)))
            .Select(x => new { x.Number, x.Status }).FirstOrDefaultAsync(ct);
        if (dup != null)
            throw new DomainException(dup.Status == ExemptionStatuses.Paid ? $"صدرت لهذه الشريحة وثيقة استثناء في هذا المهرجان ({dup.Number})" : $"لهذه الشريحة طلب استثناء قائم في هذا المهرجان ({dup.Number})");

        var e = new Exemption
        {
            Number = $"EX-{Now.Year}-{await db.NextNumberAsync("ExemptionNo", ct):D5}", UserId = u.Id, CamelChip = chip, CamelType = type,
            FestivalId = festival.Id, FestivalName = festival.Name, Race = race.Length == 0 ? null : race, OwnerNote = note.Length == 0 ? null : note,
            Status = ExemptionStatuses.Pending, CreatedAt = Now,
        };
        db.Exemptions.Add(e);
        await notifier.NotifyAsync(u.Id, NotificationKinds.Exemption, $"استلمنا طلب الاستثناء {e.Number}",
            $"طلب مشاركة {TypeLabel(type)} بالشريحة {chip} في «{festival.Name}» بدون اسم. ستصلك قيمة غرامة عدم التسمية بعد دراسة الطلب.", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await OwnerDtoAsync(e, ct);
    }

    public async Task<List<ExemptionDto>> Handle(GetMyExemptionsQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var rows = await db.Exemptions.AsNoTracking().Where(x => x.UserId == u.Id).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
        return await DtosAsync(rows, false, false, ct);
    }

    public async Task<ExemptionDto> Handle(GetMyExemptionQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var n = (q.Number ?? "").Trim().ToUpperInvariant();
        var e = await db.Exemptions.AsNoTracking().FirstOrDefaultAsync(x => x.Number == n, ct) ?? throw DomainException.NotFound("طلب الاستثناء غير موجود");
        if (e.UserId == u.Id) return await OwnerDtoAsync(e, ct);
        var me = await RequireAnyAsync(ct, Permissions.ExemptionsView, Permissions.ExemptionsManage);
        return await StaffDtoAsync(e, me, ct);
    }

    public async Task<ExemptionDto> Handle(CancelMyExemptionCommand c, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"ex:{c.Id}"], ct);
        var e = await db.Exemptions.FirstOrDefaultAsync(x => x.Id == c.Id && x.UserId == u.Id, ct) ?? throw DomainException.NotFound("طلب الاستثناء غير موجود");
        if (!ExemptionStatuses.Open.Contains(e.Status)) throw new DomainException(Closed(e));
        e.Status = ExemptionStatuses.Cancelled;
        e.CancelledAt = Now;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await OwnerDtoAsync(e, ct);
    }

    public async Task<ExemptionDto> Handle(PayExemptionCommand c, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"ex:{c.Id}", $"u:{u.Id}"], ct);
        var e = await db.Exemptions.FirstOrDefaultAsync(x => x.Id == c.Id && x.UserId == u.Id, ct) ?? throw DomainException.NotFound("طلب الاستثناء غير موجود");
        if (e.Status == ExemptionStatuses.Paid) return await OwnerDtoAsync(e, ct);
        if (e.Status == ExemptionStatuses.Pending) throw new DomainException("الطلب قيد الدراسة، ستصلك قيمة الغرامة لسدادها");
        if (e.Status != ExemptionStatuses.Priced || e.Amount is not > 0) throw new DomainException(Closed(e));
        var amount = e.Amount.Value;
        // a fine is not subject to VAT: no tax invoice, the card payment is the receipt
        var p = await money.DebitByCardAsync(u.Id, amount, PaymentPurposes.Exemption, e.Id, TxKinds.Fine, $"غرامة عدم تسمية — استثناء {e.Number} للشريحة {e.CamelChip}", ct);
        var owner = await db.Users.AsNoTracking().FirstAsync(x => x.Id == e.UserId, ct);
        e.Status = ExemptionStatuses.Paid;
        e.PaidAt = Now;
        e.PaymentId = p.Id;
        e.OwnerNameAr = owner.FullName;
        e.OwnerNameEn = owner.FullNameEn ?? Transliterator.ToLatin(owner.FullName);
        e.NationalId = owner.NationalId;
        e.IssuedAt = DateTimeOffset.FromUnixTimeSeconds(Now.ToUnixTimeSeconds());
        e.Signature = signer.Sign(e.SignedContent());
        await notifier.NotifyAsync(u.Id, NotificationKinds.Exemption, $"صدرت وثيقة الاستثناء {e.Number}",
            $"سُدّدت الغرامة ({amount:0.##} ريال)، ويحق للشريحة {e.CamelChip} المشاركة في «{e.FestivalName}» بدون اسم. اطبع الوثيقة من صفحة الاستثناءات.", $"{OwnerLink}/{e.Number}", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await OwnerDtoAsync(e, ct);
    }

    // ───── staff ─────

    public async Task<ExemptionPageDto> Handle(ListExemptionsQuery q, CancellationToken ct)
    {
        var me = await RequireAnyAsync(ct, Permissions.ExemptionsView, Permissions.ExemptionsManage);
        var full = await access.CanAsync(me, Permissions.OwnersFull, ct);
        var size = Math.Clamp(q.Size, 10, 200);
        var page = Math.Max(1, q.Page);
        var x = db.Exemptions.AsNoTracking();
        var term = q.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 100) term = term[..100];
            var phone = Phones.FromInput(term);
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var up = term.ToUpperInvariant();
            var who = db.Users.AsNoTracking().Where(u => u.Role == Roles.Owner && (u.FullName.Contains(term) || (phone != null && u.Phone == phone)
                || (digits.Length >= 4 && (u.Phone.Contains(digits) || u.NationalId == digits)))).Select(u => u.Id);
            x = x.Where(e => e.Number.Contains(up) || e.CamelChip.Contains(up) || e.FestivalName.Contains(term) || who.Contains(e.UserId));
        }
        if (q.FestivalId is { } fid) x = x.Where(e => e.FestivalId == fid);
        var counts = await x.GroupBy(e => e.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var status = q.Status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            if (!ExemptionStatuses.All.Contains(status)) throw new DomainException("حالة غير معروفة");
            x = x.Where(e => e.Status == status);
        }
        var total = await x.CountAsync(ct);
        // the ones waiting for staff first, then the newest
        var rows = await x.OrderBy(e => e.Status == ExemptionStatuses.Pending ? 0 : 1).ThenByDescending(e => e.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        int N(string s) => counts.GetValueOrDefault(s);
        return new ExemptionPageDto(await DtosAsync(rows, true, full, ct), total, page, size,
            new ExemptionCountsDto(counts.Values.Sum(), N(ExemptionStatuses.Pending), N(ExemptionStatuses.Priced), N(ExemptionStatuses.Paid), N(ExemptionStatuses.Rejected), N(ExemptionStatuses.Cancelled)));
    }

    public async Task<ExemptionDto> Handle(GetExemptionQuery q, CancellationToken ct)
    {
        var me = await RequireAnyAsync(ct, Permissions.ExemptionsView, Permissions.ExemptionsManage);
        var e = await db.Exemptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.Id, ct) ?? throw DomainException.NotFound("طلب الاستثناء غير موجود");
        return await StaffDtoAsync(e, me, ct);
    }

    public async Task<ExemptionDto> Handle(PriceExemptionCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ExemptionsManage, ct);
        if (c.Amount <= 0 || c.Amount > MaxAmount) throw new DomainException("قيمة الغرامة أكبر من صفر وحتى 1,000,000 ريال");
        var note = Clip(c.Note, 600);
        if (note.Length > 500) throw new DomainException("الملاحظة طويلة (500 حرف كحد أقصى)");
        await using var uow = await db.BeginAsync([$"ex:{c.Id}"], ct);
        var e = await db.Exemptions.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("طلب الاستثناء غير موجود");
        if (!ExemptionStatuses.Open.Contains(e.Status)) throw new DomainException(Closed(e));
        var changed = e.Status == ExemptionStatuses.Priced;
        e.Amount = Money.Round2(c.Amount);
        e.StaffNote = note.Length == 0 ? null : note;
        e.Status = ExemptionStatuses.Priced;
        e.PricedAt = Now;
        e.PricedById = me.Id;
        e.PricedByName = me.FullName;
        await notifier.NotifyAsync(e.UserId, NotificationKinds.Exemption,
            changed ? $"عُدّلت غرامة طلب الاستثناء {e.Number}" : $"حُدّدت غرامة طلب الاستثناء {e.Number}",
            $"غرامة عدم تسمية الشريحة {e.CamelChip} في «{e.FestivalName}»: {e.Amount:0.##} ريال. سدّدها لتصدر لك وثيقة الاستثناء." + (note.Length > 0 ? $" ملاحظة: {note}" : ""), OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await StaffDtoAsync(e, me, ct);
    }

    public async Task<ExemptionDto> Handle(RejectExemptionCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ExemptionsManage, ct);
        var reason = (c.Reason ?? "").Trim();
        if (reason.Length < 3) throw new DomainException("اكتب سبب الرفض، يصل للمالك");
        if (reason.Length > 500) throw new DomainException("سبب الرفض طويل (500 حرف كحد أقصى)");
        await using var uow = await db.BeginAsync([$"ex:{c.Id}"], ct);
        var e = await db.Exemptions.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("طلب الاستثناء غير موجود");
        if (!ExemptionStatuses.Open.Contains(e.Status)) throw new DomainException(Closed(e));
        e.Status = ExemptionStatuses.Rejected;
        e.RejectedAt = Now;
        e.RejectReason = reason;
        e.RejectedByName = me.FullName;
        await notifier.NotifyAsync(e.UserId, NotificationKinds.Exemption, $"رُفض طلب الاستثناء {e.Number}", $"الشريحة {e.CamelChip} في «{e.FestivalName}» — السبب: {reason}", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await StaffDtoAsync(e, me, ct);
    }

    public async Task<ExemptionDto> Handle(RevokeExemptionCommand c, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.ExemptionsManage, ct);
        var reason = (c.Reason ?? "").Trim();
        if (reason.Length < 5) throw new DomainException("اكتب سبب إلغاء الوثيقة");
        if (reason.Length > 500) throw new DomainException("سبب الإلغاء طويل (500 حرف كحد أقصى)");
        await using var uow = await db.BeginAsync([$"ex:{c.Id}"], ct);
        var e = await db.Exemptions.FirstOrDefaultAsync(x => x.Id == c.Id, ct) ?? throw DomainException.NotFound("طلب الاستثناء غير موجود");
        if (e.Status != ExemptionStatuses.Paid) throw new DomainException("لم تصدر وثيقة لهذا الطلب بعد");
        if (e.RevokedAt != null) throw new DomainException("أُلغيت هذه الوثيقة مسبقًا");
        e.RevokedAt = Now;
        e.RevokeReason = reason;
        e.RevokedByName = me.FullName;
        await notifier.NotifyAsync(e.UserId, NotificationKinds.Exemption, $"أُلغيت وثيقة الاستثناء {e.Number}", $"السبب: {reason}", OwnerLink, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await StaffDtoAsync(e, me, ct);
    }

    // ───── public verification ─────

    public async Task<ExemptionVerifyDto?> Handle(VerifyExemptionQuery q, CancellationToken ct)
    {
        var n = (q.Number ?? "").Trim().ToUpperInvariant();
        var e = await db.Exemptions.AsNoTracking().FirstOrDefaultAsync(x => x.Number == n && x.Status == ExemptionStatuses.Paid, ct);
        if (e == null || e.Signature == null) return null;
        // the stored signature must still match the document's content, and the code the visitor brought must match it
        var intact = SameCode(signer.Sign(e.SignedContent()), e.Signature);
        var status = !intact || e.RevokedAt != null ? "revoked" : "valid";
        bool? matches = string.IsNullOrWhiteSpace(q.Signature) ? null : intact && SameCode(q.Signature.Trim().Replace(" ", ""), e.Signature);
        if (matches != true) return new(e.Number, status, matches, e.IssuedAt, null, null, null, null, null, null, null, null);
        return new(e.Number, status, true, e.IssuedAt, e.OwnerNameAr, e.OwnerNameEn, e.CamelChip, e.CamelType, e.FestivalName, e.Race,
            e.RevokedAt, intact ? e.RevokeReason : "تعذّر التحقق من سلامة بيانات الوثيقة");
    }

    private static bool SameCode(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a.ToUpperInvariant()), Encoding.UTF8.GetBytes(b.ToUpperInvariant()));
}
