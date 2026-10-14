using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Certificates;

/// <summary>a mark on a certificate, with where it stands in today's registry: valid | transferred | removed</summary>
public record CertificateItemDto(Guid Id, string Name, string Latin, string Type, string TypeLabel, DateTimeOffset AcquiredAt, string RegistryCode, string Source, string? BaseName, string Current);

/// <summary>status: valid | partial (some marks changed hands since) | superseded (none still held) | revoked</summary>
public record CertificateDto(Guid Id, string Number, string Scope, Guid OwnerId, string OwnerNameAr, string OwnerNameEn, string? NationalIdMasked, string? Nationality,
    DateTimeOffset IssuedAt, string Signature, string Status, DateTimeOffset? RevokedAt, string? RevokeReason, List<CertificateItemDto> Items, int ItemCount);

/// <summary>the public verification view: no ID number, and only whether the printed signature code matches</summary>
public record CertificateVerifyDto(string Number, string Scope, string OwnerNameAr, string OwnerNameEn, string? Nationality, DateTimeOffset IssuedAt, string Status,
    bool? SignatureMatches, DateTimeOffset? RevokedAt, string? RevokeReason, List<CertificateItemDto> Items);

/// <summary>the owner asks for a certificate of one mark (ReservedNameId) or all their marks (scope "portfolio"); it is issued at once</summary>
public record IssueCertificateCommand(string Scope, Guid? ReservedNameId, string? OwnerNameEn) : ICommand<CertificateDto>;
public record ListMyCertificatesQuery : IQuery<List<CertificateDto>>;
/// <summary>the full certificate, for its owner or staff with certificates.manage</summary>
public record GetCertificateQuery(string Number) : IQuery<CertificateDto>;
public record VerifyIssuedCertificateQuery(string Number, string? Signature) : IQuery<CertificateVerifyDto?>;
public record ListCertificatesQuery(string? Q, string? Status, int Page = 1, int Size = 50) : IQuery<CertificatePage>;
public record CertificatePage(List<CertificateDto> Items, int Total);
public record RevokeCertificateCommand(Guid Id, string Reason) : ICommand<CertificateDto>;
/// <summary>staff (names.latin) set or clear the registry's Latin spelling of a name</summary>
public record SetLatinNameCommand(Guid ReservedNameId, string? Latin) : ICommand<ReservedNameDto>;

public partial class CertificateHandlers(IAppDbContext db, Access access, ICertificateSigner signer, Notifier notifier, TimeProvider clock)
    : IRequestHandler<IssueCertificateCommand, CertificateDto>, IRequestHandler<ListMyCertificatesQuery, List<CertificateDto>>,
      IRequestHandler<GetCertificateQuery, CertificateDto>, IRequestHandler<VerifyIssuedCertificateQuery, CertificateVerifyDto?>,
      IRequestHandler<ListCertificatesQuery, CertificatePage>, IRequestHandler<RevokeCertificateCommand, CertificateDto>,
      IRequestHandler<SetLatinNameCommand, ReservedNameDto>
{
    private const int DailyLimit = 30, MaxMarks = 500;
    // Arabic stays readable (and short) in the stored snapshot
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) };
    private static readonly Dictionary<string, string> TypeEn = new() { [NameTypes.Qaadan] = "Qa'dan (young males)", [NameTypes.Bakkar] = "Bakkar (young females)", [NameTypes.Manqiya] = "Manqiya (select herd)" };
    [GeneratedRegex(@"^[A-Za-z][A-Za-z .'\-]{3,118}[A-Za-z.]$")] private static partial Regex LatinNameRx();

    public static string TypeLabel(string type) => $"{NameTypes.Arabic.GetValueOrDefault(type, type)} · {TypeEn.GetValueOrDefault(type, type)}";

    /// <summary>what the signature covers: change any of it and the printed code no longer matches</summary>
    private static string Content(Certificate c) => string.Join('|', c.Number, c.OwnerId, c.OwnerNameAr, c.OwnerNameEn, c.NationalId, c.IssuedAt.ToUnixTimeSeconds(), c.ItemsJson);

    private static List<CertificateItem> ItemsOf(Certificate c) => JsonSerializer.Deserialize<List<CertificateItem>>(c.ItemsJson, Json) ?? [];

    private async Task<(List<CertificateItemDto> Items, string Status)> CurrentAsync(Certificate c, CancellationToken ct)
    {
        var items = ItemsOf(c);
        var ids = items.Select(i => i.Id).ToList();
        var now = await db.ReservedNames.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.OwnerId }).ToDictionaryAsync(r => r.Id, r => r.OwnerId, ct);
        var list = items.Select(i => new CertificateItemDto(i.Id, i.Name, i.Latin, i.Type, TypeLabel(i.Type), i.AcquiredAt, i.RegistryCode, i.Source, i.BaseName,
            !now.TryGetValue(i.Id, out var o) ? "removed" : o == c.OwnerId ? "valid" : "transferred")).ToList();
        var held = list.Count(i => i.Current == "valid");
        var status = c.RevokedAt != null ? "revoked" : held == list.Count ? "valid" : held == 0 ? "superseded" : "partial";
        return (list, status);
    }

    private async Task<CertificateDto> DtoAsync(Certificate c, CancellationToken ct)
    {
        var (items, status) = await CurrentAsync(c, ct);
        return new(c.Id, c.Number, c.Scope, c.OwnerId, c.OwnerNameAr, c.OwnerNameEn, Mask(c.NationalId), c.Nationality, c.IssuedAt, c.Signature, status, c.RevokedAt, c.RevokeReason, items, c.ItemCount);
    }

    /// <summary>the certificate shows the ID as ••••••6781</summary>
    public static string? Mask(string? id) => string.IsNullOrEmpty(id) ? null : id.Length <= 4 ? id : new string('•', id.Length - 4) + id[^4..];

    public async Task<CertificateDto> Handle(IssueCertificateCommand q, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        if (me.Role != Roles.Owner) throw DomainException.Forbidden("الشهادات لحسابات ملاك الإبل");
        if (string.IsNullOrWhiteSpace(me.FirstName) || string.IsNullOrWhiteSpace(me.FamilyName) || string.IsNullOrWhiteSpace(me.NationalId))
            throw new DomainException("أكمل بياناتك الشخصية (الاسم ورقم الهوية) في «حسابي» قبل إصدار الشهادة");
        var scope = q.Scope == CertificateScopes.Portfolio ? CertificateScopes.Portfolio : CertificateScopes.Mark;
        await using var tx = await db.BeginAsync([$"u:{me.Id}"], ct);
        var now = clock.GetUtcNow();
        if (await db.Certificates.CountAsync(c => c.OwnerId == me.Id && c.IssuedAt > now.AddDays(-1), ct) >= DailyLimit)
            throw new DomainException($"بلغت حد الإصدار اليومي ({DailyLimit} شهادة)، حاول غدًا");

        List<ReservedName> marks;
        if (scope == CertificateScopes.Mark)
        {
            if (q.ReservedNameId is not { } rid) throw new DomainException("اختر الشعار");
            var r = await db.ReservedNames.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rid, ct) ?? throw DomainException.NotFound("الشعار غير موجود");
            if (r.OwnerId != me.Id) throw DomainException.Forbidden("هذا الشعار ليس مسجلًا باسمك");
            if (await db.Invoices.AnyAsync(i => i.UserId == me.Id && i.Name == r.Name && i.Status == InvoiceStatuses.Unpaid, ct))
                throw new DomainException("سدّد فاتورة الشعار أولًا، ثم أصدر شهادته");
            marks = [r];
        }
        else
        {
            // a mark won at auction counts once its invoice is paid
            marks = await db.ReservedNames.AsNoTracking()
                .Where(x => x.OwnerId == me.Id && !db.Invoices.Any(i => i.UserId == me.Id && i.Name == x.Name && i.Status == InvoiceStatuses.Unpaid))
                .OrderBy(x => x.AcquiredAt).Take(MaxMarks + 1).ToListAsync(ct);
            if (marks.Count == 0) throw new DomainException("لا توجد شعارات مسجلة باسمك بعد (أو لم تُسدَّد فواتيرها)");
            if (marks.Count > MaxMarks) throw new DomainException($"الشهادة الشاملة تتسع لـ {MaxMarks} شعار، تواصل معنا لإصدار شهادة أكبر");
        }

        var en = (q.OwnerNameEn ?? "").Trim();
        en = Regex.Replace(en, @"\s+", " ");
        if (en.Length > 0)
        {
            if (!LatinNameRx().IsMatch(en)) throw new DomainException("اكتب اسمك بالإنجليزية بحروف لاتينية كما في جواز السفر");
            var u = await db.Users.FirstAsync(x => x.Id == me.Id, ct);
            u.FullNameEn = en;
        }
        else en = me.FullNameEn ?? Transliterator.ToLatin(me.FullName);

        var items = marks.Select(r => new CertificateItem(r.Id, r.Name, Mapper.LatinOf(r), r.Type, r.AcquiredAt, r.CertCode, r.Source, r.BaseName)).ToList();
        var c = new Certificate
        {
            Number = $"SC-{now.Year}-{await db.NextNumberAsync("CertificateNo", ct):D6}", OwnerId = me.Id, Scope = scope,
            OwnerNameAr = me.FullName, OwnerNameEn = en, NationalId = me.NationalId, Nationality = me.Nationality,
            ItemsJson = JsonSerializer.Serialize(items, Json), ItemCount = items.Count,
            // whole seconds, so the signed content reads back identically from the database
            IssuedAt = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()),
        };
        c.Signature = signer.Sign(Content(c));
        db.Certificates.Add(c);
        var what = scope == CertificateScopes.Mark ? $"شعار «{marks[0].Name}»" : $"جميع شعاراتك ({items.Count})";
        await notifier.NotifyAsync(me.Id, NotificationKinds.Certificate, $"صدرت شهادة {c.Number}", $"شهادة تسجيل {what}، موثقة من المنظمة الدولية للإبل.", $"/dashboard/certificates/{c.Number}", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await DtoAsync(c, ct);
    }

    public async Task<List<CertificateDto>> Handle(ListMyCertificatesQuery q, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        var list = await db.Certificates.AsNoTracking().Where(c => c.OwnerId == me.Id).OrderByDescending(c => c.IssuedAt).Take(200).ToListAsync(ct);
        var outp = new List<CertificateDto>();
        foreach (var c in list) outp.Add(await DtoAsync(c, ct));
        return outp;
    }

    public async Task<CertificateDto> Handle(GetCertificateQuery q, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        var n = (q.Number ?? "").Trim().ToUpperInvariant();
        var c = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(x => x.Number == n, ct) ?? throw DomainException.NotFound("الشهادة غير موجودة");
        if (c.OwnerId != me.Id) await access.RequireAsync(Permissions.CertificatesManage, ct);
        return await DtoAsync(c, ct);
    }

    public async Task<CertificateVerifyDto?> Handle(VerifyIssuedCertificateQuery q, CancellationToken ct)
    {
        var n = (q.Number ?? "").Trim().ToUpperInvariant();
        var c = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(x => x.Number == n, ct);
        if (c == null) return null;
        var (items, status) = await CurrentAsync(c, ct);
        // the stored signature must still match its content, and the code the visitor brought (from the QR or typed) must match it
        var intact = SameCode(signer.Sign(Content(c)), c.Signature);
        if (!intact) status = "revoked";
        bool? matches = string.IsNullOrWhiteSpace(q.Signature) ? null : intact && SameCode(q.Signature.Trim().Replace(" ", ""), c.Signature);
        // a certificate number alone (they are sequential) only says whether it exists and its status: the owner and the names need the printed code
        if (matches != true)
            return new(c.Number, "", "", "", null, c.IssuedAt, status, matches, null, null, []);
        return new(c.Number, c.Scope, c.OwnerNameAr, c.OwnerNameEn, c.Nationality, c.IssuedAt, status, matches, c.RevokedAt, intact ? c.RevokeReason : "تعذّر التحقق من سلامة بيانات الشهادة", items);
    }

    /// <summary>constant-time, case-insensitive comparison of two signature codes</summary>
    private static bool SameCode(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a.ToUpperInvariant()), Encoding.UTF8.GetBytes(b.ToUpperInvariant()));
    }

    public async Task<CertificatePage> Handle(ListCertificatesQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.CertificatesManage, ct);
        var x = db.Certificates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Q)) { var t = q.Q.Trim(); x = x.Where(c => c.Number.Contains(t) || c.OwnerNameAr.Contains(t) || c.OwnerNameEn.Contains(t) || c.ItemsJson.Contains(t)); }
        if (q.Status == "revoked") x = x.Where(c => c.RevokedAt != null);
        if (q.Status == "active") x = x.Where(c => c.RevokedAt == null);
        var total = await x.CountAsync(ct);
        var size = Math.Clamp(q.Size, 10, 200);
        var rows = await x.OrderByDescending(c => c.IssuedAt).Skip((Math.Max(1, q.Page) - 1) * size).Take(size).ToListAsync(ct);
        var items = new List<CertificateDto>();
        foreach (var c in rows) items.Add(await DtoAsync(c, ct));
        return new(items, total);
    }

    public async Task<CertificateDto> Handle(RevokeCertificateCommand q, CancellationToken ct)
    {
        var staff = await access.RequireAsync(Permissions.CertificatesManage, ct);
        var reason = (q.Reason ?? "").Trim();
        if (reason.Length < 5) throw new DomainException("اكتب سبب الإلغاء");
        var c = await db.Certificates.FirstOrDefaultAsync(x => x.Id == q.Id, ct) ?? throw DomainException.NotFound("الشهادة غير موجودة");
        if (c.RevokedAt != null) throw new DomainException("الشهادة ملغاة مسبقًا");
        c.RevokedAt = clock.GetUtcNow(); c.RevokedBy = staff.FullName; c.RevokeReason = reason.Length > 300 ? reason[..300] : reason;
        await notifier.NotifyAsync(c.OwnerId, NotificationKinds.Certificate, $"أُلغيت الشهادة {c.Number}", $"السبب: {c.RevokeReason}. يمكنك إصدار شهادة جديدة.", "/dashboard/certificates", ct);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(c, ct);
    }

    public async Task<ReservedNameDto> Handle(SetLatinNameCommand q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.NamesLatin, ct);
        var r = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == q.ReservedNameId, ct) ?? throw DomainException.NotFound("الاسم غير موجود");
        var v = Regex.Replace((q.Latin ?? "").Trim(), @"\s+", " ");
        if (v.Length == 0) r.LatinName = null;
        else
        {
            if (v.Length > 80 || !Regex.IsMatch(v, @"^[A-Za-z][A-Za-z '\-’]*$")) throw new DomainException("الكتابة اللاتينية بحروف إنجليزية فقط (مع - أو ')");
            r.LatinName = v;
        }
        await db.SaveChangesAsync(ct);
        return Mapper.Reserved(r);
    }
}
