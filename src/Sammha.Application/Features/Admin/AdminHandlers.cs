using MediatR;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Admin;

/// <summary>Approval committee and platform administration.</summary>
public partial class AdminHandlers(IAppDbContext db, TimeProvider clock, Access access, Mapper map, Matching matching, Money money, Notifier notifier,
    IPasswordHasher hasher, Sessions sessions, IOptions<PlatformOptions> rules)
    : IRequestHandler<GetOverviewQuery, AdminOverviewDto>, IRequestHandler<ListRequestsQuery, List<RequestDto>>, IRequestHandler<ReviewRequestCommand, RequestDto>, IRequestHandler<RejectRequestCommand, RequestDto>, IRequestHandler<CancelRequestCommand, RequestDto>, IRequestHandler<GetAwaitingScheduleQuery, List<AwaitingDto>>, IRequestHandler<ScheduleAuctionsCommand, List<AuctionDto>>, IRequestHandler<ListInvoicesQuery, List<InvoiceDto>>, IRequestHandler<MarkInvoicePaidCommand, InvoiceDto>, IRequestHandler<ListUsersQuery, List<UserDto>>, IRequestHandler<CreateStaffCommand, UserDto>, IRequestHandler<SetUserRoleCommand, UserDto>, IRequestHandler<SetUserActiveCommand, UserDto>, IRequestHandler<SaveEventCommand, CamelEventDto>, IRequestHandler<DeleteEventCommand, MediatR.Unit>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private PlatformOptions R => rules.Value;
    private IQueryable<LogoRequest> Full => db.Requests.Include(r => r.User).Include(r => r.Candidates);
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")] private static partial Regex EmailRx();

    private async Task<AdminOverviewDto> OverviewAsync(CancellationToken ct)
    {
        await access.RequireAsync(Permissions.Overview, ct);
        var now = Now;
        return new AdminOverviewDto(
            await db.Requests.CountAsync(r => r.Status == RequestStatuses.UnderReview, ct),
            await db.Candidates.CountAsync(c => c.Status == CandidateStatuses.Accepted && c.AuctionId == null && c.Request.Status == RequestStatuses.Accepted && c.Request.Kind != RequestKinds.Compound, ct),
            await db.Auctions.CountAsync(a => a.StartsAt <= now && a.EndsAt > now, ct),
            await db.Auctions.CountAsync(a => a.StartsAt > now, ct),
            await db.Invoices.CountAsync(i => i.Status == InvoiceStatuses.Unpaid, ct),
            await db.Invoices.Where(i => i.Status == InvoiceStatuses.Paid).SumAsync(i => (decimal?)i.Total, ct) ?? 0,
            await db.Tickets.CountAsync(t => t.Status == TicketStatuses.New || t.Status == TicketStatuses.InProgress, ct));
    }

    private async Task<List<RequestDto>> ListRequestsAsync(string? status, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.RequestsView, ct);
        var q = Full.AsNoTracking();
        q = string.IsNullOrEmpty(status) || status == "all" ? q.Where(r => r.Status != RequestStatuses.PendingPayment) : q.Where(r => r.Status == status);
        var list = await q.OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt).Take(1000).ToListAsync(ct);
        return await map.RequestsAsync(list, ct);
    }

    private async Task<RequestDto> ReviewAsync(Guid id, List<ReviewDecision> decisions, string? note, CancellationToken ct)
    {
        var staff = await access.RequireAsync(Permissions.RequestsReview, ct);
        await using var uow = await db.BeginAsync([$"r:{id}"], ct);
        var r = await Full.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        if (r.Status != RequestStatuses.UnderReview) throw new DomainException("تمت مراجعة هذا الطلب");
        // policy: a logo request has one name accepted at most; the others are rejected
        if (r.Kind == RequestKinds.Standard)
        {
            var accepted = decisions.Where(d => d.Status == CandidateStatuses.Accepted)
                .Select(d => r.Candidates.FirstOrDefault(c => c.Id == d.CandidateId)).Where(c => c != null && c.Status != CandidateStatuses.Withdrawn).Distinct().ToList();
            if (accepted.Count > 1) throw new DomainException("يُقبل شعار واحد فقط في الطلب الواحد؛ اختر اسمًا واحدًا وارفض البقية");
        }
        foreach (var d in decisions)
        {
            var c = r.Candidates.FirstOrDefault(x => x.Id == d.CandidateId);
            if (c == null || c.Status == CandidateStatuses.Withdrawn) continue;
            if (d.Status == CandidateStatuses.Accepted)
            {
                var exact = (await matching.SimilarAsync(c.Normalized, r.Id, ct)).FirstOrDefault(h => h.Score >= 1);
                // a compound name contains its own base name, which is not a conflict
                if (exact != null && !(r.Kind == RequestKinds.Compound && NameMatcher.Normalize(exact.Name) == NameMatcher.Normalize(r.BaseName)))
                    throw new DomainException($"لا يمكن قبول «{c.Name}»: مطابق لـ«{exact.Name}»");
                c.Status = CandidateStatuses.Accepted;
                c.RejectReason = null;
            }
            else if (d.Status == CandidateStatuses.Rejected)
            {
                c.Status = CandidateStatuses.Rejected;
                c.RejectReason = string.IsNullOrWhiteSpace(d.Reason) ? "مشابه لاسم مسجل" : d.Reason.Trim();
            }
        }
        if (r.Candidates.Any(c => c.Status == CandidateStatuses.Pending)) throw new DomainException("حدد قرارًا لكل اسم");
        var accepted = r.Candidates.Where(c => c.Status == CandidateStatuses.Accepted).OrderBy(c => c.Position).ToList();
        var rejected = r.Candidates.Where(c => c.Status == CandidateStatuses.Rejected).OrderBy(c => c.Position).ToList();
        var any = accepted.Count > 0;
        r.Status = any ? RequestStatuses.Accepted : RequestStatuses.Rejected;
        r.ReviewedAt = Now;
        r.ReviewerId = staff.Id;
        r.ReviewerName = staff.FullName;
        r.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var names = string.Join("، ", accepted.Select(c => c.Name));
        if (r.Kind == RequestKinds.Investment)
        {
            if (any)
                await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, $"قُبل في «استثمار الاسم»: «{string.Join("»، «", accepted.Select(c => c.Name))}»",
                    $"تُطرح الأسماء المقبولة في مزاد عام قريبًا، ولك {Money.Pct(R.InvestOwnerShare)}% من قيمة بيع كل اسم.{(rejected.Count > 0 ? $" ولم يُقبل: {string.Join("، ", rejected.Select(c => c.Name))}." : "")}", "/dashboard/invest", ct);
            else // also sent by SMS through the notification webhook; the study fee is not refunded
                await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, "لم تُقبل الأسماء المقدّمة في «استثمار الاسم»", $"{rejected.FirstOrDefault()?.RejectReason ?? "وفق معايير اللجنة"}. رسوم الدراسة غير مستردة.", "/dashboard/invest", ct);
        }
        else if (r.Kind == RequestKinds.Compound)
        {
            if (any)
            {
                var c = accepted[0];
                var nid = Guid.NewGuid();
                db.ReservedNames.Add(new ReservedName
                {
                    Id = nid, Name = c.Name, Normalized = c.Normalized, Type = r.Type, OwnerId = r.UserId, OwnerName = r.User.FullName, Price = 0,
                    AcquiredAt = Now, Source = ReservedSources.Compound, BaseName = r.BaseName, CertCode = ReservedName.CertFor(nid),
                });
                await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, $"اعتُمد الاسم المركب «{c.Name}»", "سُجّل باسمك مباشرة دون مزاد، وشهادته في «أسمائي».", "/dashboard/names", ct);
            }
            else await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, $"لم يُعتمد الاسم المركب لـ«{r.BaseName}»", rejected.FirstOrDefault()?.RejectReason ?? "راجع ملاحظة اللجنة في الطلب.", $"/dashboard/requests/{r.Id}", ct);
        }
        else
        {
            await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, any ? $"قُبل طلبك {r.Number}" : $"لم يُقبل طلبك {r.Number}",
                any ? $"الأسماء المقبولة: {names}. سيُحدَّد موعد المزاد قريبًا." : "أُعيد ضمان المزاد إلى محفظتك.", $"/dashboard/requests/{r.Id}", ct);
            if (!any) await money.RefundDepositAsync(r, "مرفوض", ct);
        }
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.RequestAsync(r, ct);
    }

    private async Task<RequestDto> RejectAsync(Guid id, string reason, CancellationToken ct)
    {
        var staff = await access.RequireAsync(Permissions.RequestsReview, ct);
        await using var uow = await db.BeginAsync([$"r:{id}"], ct);
        var r = await Full.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null || r.Status != RequestStatuses.UnderReview) throw new DomainException("لا يمكن رفض هذا الطلب");
        var why = (reason ?? "").Trim();
        foreach (var c in r.Candidates.Where(c => c.Status != CandidateStatuses.Withdrawn)) { c.Status = CandidateStatuses.Rejected; c.RejectReason = why; }
        r.Status = RequestStatuses.Rejected;
        r.ReviewedAt = Now;
        r.ReviewerId = staff.Id;
        r.ReviewerName = staff.FullName;
        r.ReviewNote = why;
        if (r.Kind == RequestKinds.Investment)
            await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, "لم تُقبل الأسماء المقدّمة في «استثمار الاسم»", $"{(why.Length > 0 ? why : "وفق معايير اللجنة")}. رسوم الدراسة غير مستردة.", "/dashboard/invest", ct);
        else await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, $"لم يُقبل طلبك {r.Number}", why, $"/dashboard/requests/{r.Id}", ct);
        await money.RefundDepositAsync(r, "مرفوض", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.RequestAsync(r, ct);
    }

    private async Task<RequestDto> CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var staff = await access.RequireAsync(Permissions.RequestsCancel, ct);
        await using var uow = await db.BeginAsync([$"r:{id}"], ct);
        var r = await Full.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null || r.Status is not (RequestStatuses.UnderReview or RequestStatuses.Accepted)) throw new DomainException("لا يمكن إلغاء هذا الطلب");
        if (r.Candidates.Any(c => c.AuctionId != null)) throw new DomainException("لا يمكن إلغاء طلب فُتح له مزاد");
        r.Status = RequestStatuses.Cancelled;
        r.ReviewedAt = Now;
        r.ReviewerId = staff.Id;
        r.ReviewerName = staff.FullName;
        r.ReviewNote = (reason ?? "").Trim();
        r.CancelledAt = Now;
        r.CancelledBy = "committee";
        r.CancelReason = r.ReviewNote;
        await notifier.NotifyAsync(r.UserId, NotificationKinds.Reviewed, $"أُلغي طلبك {r.Number}", r.ReviewNote.Length > 0 ? r.ReviewNote : "ألغته لجنة الاعتماد.", $"/dashboard/requests/{r.Id}", ct);
        await money.RefundDepositAsync(r, "ألغته اللجنة", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.RequestAsync(r, ct);
    }

    private async Task<List<AwaitingDto>> AwaitingScheduleAsync(CancellationToken ct)
    {
        await access.RequireAsync(Permissions.AuctionsSchedule, ct);
        var list = await Full.AsNoTracking()
            .Where(r => r.Status == RequestStatuses.Accepted && r.Kind != RequestKinds.Compound && r.Candidates.Any(c => c.Status == CandidateStatuses.Accepted && c.AuctionId == null))
            .OrderBy(r => r.ReviewedAt).ToListAsync(ct);
        var auctions = await map.AuctionsOfAsync(list, ct);
        return list.SelectMany(r =>
        {
            var dto = map.Request(r, auctions);
            return r.Candidates.Where(c => c.Status == CandidateStatuses.Accepted && c.AuctionId == null).OrderBy(c => c.Position).Select(c =>
            {
                var cd = map.Candidate(r, c, auctions);
                return new AwaitingDto(cd.Id, cd.Name, cd.Normalized, cd.Status, cd.Similar, cd.RejectReason, cd.AuctionId, cd.WithdrawnAt, cd.Withdrawable, dto);
            });
        }).ToList();
    }

    private async Task<List<AuctionDto>> ScheduleAsync(List<Guid> candidateIds, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.AuctionsSchedule, ct);
        if (endsAt <= startsAt) throw new DomainException("موعد النهاية يجب أن يكون بعد البداية");
        if (endsAt <= Now) throw new DomainException("موعد النهاية في الماضي");
        if (candidateIds.Count == 0) throw new DomainException("اختر اسمًا واحدًا على الأقل");
        await using var uow = await db.BeginAsync(["schedule"], ct);
        var cands = await db.Candidates.Include(c => c.Request)
            .Where(c => candidateIds.Contains(c.Id) && c.Status == CandidateStatuses.Accepted && c.AuctionId == null && c.Request.Status == RequestStatuses.Accepted && c.Request.Kind != RequestKinds.Compound)
            .OrderBy(c => c.Request.Number).ThenBy(c => c.Position).ToListAsync(ct);
        var created = new List<Auction>();
        var tz = TimeZoneInfo.FindSystemTimeZoneById(R.TimeZone);
        var when = TimeZoneInfo.ConvertTime(startsAt, tz).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        foreach (var c in cands)
        {
            var a = new Auction
            {
                Number = $"AU-{await db.NextNumberAsync("AuctionNo", ct)}",
                RequestId = c.RequestId, CandidateId = c.Id, Name = c.Name, Type = c.Request.Type, RequesterId = c.Request.UserId,
                OpeningPrice = R.OpeningPrice, MinIncrement = R.MinIncrement, StartsAt = startsAt, EndsAt = endsAt, CurrentPrice = R.OpeningPrice,
                IsInvestment = c.Request.Kind == RequestKinds.Investment,
            };
            db.Auctions.Add(a);
            c.AuctionId = a.Id;
            created.Add(a);
            await notifier.NotifyAsync(c.Request.UserId, NotificationKinds.AuctionOpened, $"حُدّد موعد مزاد «{c.Name}»", $"يبدأ {when} بتوقيت الرياض.", $"/auctions/{a.Id}", ct);
        }
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.AuctionsAsync(created, ct);
    }

    private async Task<List<InvoiceDto>> AllInvoicesAsync(CancellationToken ct)
    {
        await access.RequireAsync(Permissions.InvoicesView, ct);
        var list = await db.Invoices.AsNoTracking().OrderByDescending(i => i.IssuedAt).Take(2000).ToListAsync(ct);
        return await map.InvoicesAsync(list, ct);
    }

    /// <summary>cash or bank transfer recorded by the committee</summary>
    private async Task<InvoiceDto> MarkInvoicePaidAsync(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.InvoicesMarkPaid, ct);
        await using var uow = await db.BeginAsync([$"i:{id}"], ct);
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv == null || inv.Status == InvoiceStatuses.Paid) throw new DomainException("الفاتورة غير موجودة أو مدفوعة");
        await money.MarkInvoicePaidAsync(inv, ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.InvoiceAsync(inv, ct);
    }

    // ───── users & permissions (super admin) ─────
    private async Task<List<UserDto>> ListUsersAsync(CancellationToken ct)
    {
        await access.RequireAsync(Permissions.UsersView, ct);
        var roles = await db.StaffRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);
        StaffRole? RoleOf(User u) => u.Role != Roles.Committee ? null : roles.GetValueOrDefault(u.StaffRoleId ?? Permissions.CommitteeRoleId) ?? roles.GetValueOrDefault(Permissions.CommitteeRoleId);
        return (await db.Users.AsNoTracking().OrderByDescending(u => u.CreatedAt).ToListAsync(ct))
            .Select(u => Roles.IsStaff(u.Role) ? Mapper.User(u, u.Role == Roles.Admin ? Permissions.All : RoleOf(u)?.PermissionList() ?? Permissions.CommitteeDefault, RoleOf(u)) : Mapper.User(u)).ToList();
    }

    private async Task<UserDto> CreateStaffAsync(StaffInput i, CancellationToken ct)
    {
        var actor = await access.RequireAsync(Permissions.UsersCreate, ct);
        if (i.Role == Roles.Admin && actor.Role != Roles.Admin) throw DomainException.Forbidden("إنشاء حساب مدير نظام لمدير النظام فقط");
        var phone = NameMatcher.NormalizePhone(i.Phone);
        var email = (i.Email ?? "").Trim();
        if ((i.FullName ?? "").Trim().Length < 3) throw new DomainException("اكتب اسم الموظف");
        if (!NameMatcher.IsSaudiMobile(phone)) throw new DomainException("رقم الجوال يبدأ بـ 05 ويتكون من 10 أرقام");
        if (!EmailRx().IsMatch(email)) throw new DomainException("البريد الإلكتروني غير صحيح");
        if (await db.Users.AnyAsync(u => u.Phone == phone, ct)) throw new DomainException("رقم الجوال مسجل لحساب آخر");
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new DomainException("البريد الإلكتروني مسجل لحساب آخر");
        if ((i.Password ?? "").Length < 8) throw new DomainException("كلمة المرور المؤقتة 8 أحرف على الأقل");
        if (i.Role is not (Roles.Committee or Roles.Admin)) throw new DomainException("اختر الصلاحية");
        var u = new User { FullName = i.FullName!.Trim(), Phone = phone, Email = email, Role = i.Role, Active = true, CreatedAt = Now };
        if (i.Role == Roles.Committee && i.StaffRoleId is { } rid)
        {
            if (!await db.StaffRoles.AnyAsync(r => r.Id == rid, ct)) throw new DomainException("الدور غير موجود");
            u.StaffRoleId = rid;
        }
        u.PasswordHash = hasher.Hash(i.Password!);
        db.Users.Add(u);
        await db.SaveChangesAsync(ct);
        return await access.DtoAsync(u, ct);
    }

    /// <summary>only admins can change admin accounts or make someone an admin, so a permission never escalates itself</summary>
    private async Task<User> TargetAsync(Guid id, string permission, CancellationToken ct)
    {
        var actor = await access.RequireAsync(permission, ct);
        var t = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("المستخدم غير موجود");
        if (access.IsSuper(t)) throw new DomainException("لا يمكن تعديل حساب مدير النظام الرئيسي");
        if (t.Id == actor.Id) throw new DomainException("لا يمكنك تعديل صلاحيات حسابك");
        if (t.Role == Roles.Admin && actor.Role != Roles.Admin) throw DomainException.Forbidden("حسابات مديري النظام يعدّلها مدير النظام فقط");
        return t;
    }

    private async Task<UserDto> SetRoleAsync(Guid id, string role, CancellationToken ct)
    {
        var t = await TargetAsync(id, Permissions.UsersRoles, ct);
        if (!Roles.All.Contains(role)) throw new DomainException("صلاحية غير معروفة");
        if (role == Roles.Admin && (await access.MeAsync(ct)).Role != Roles.Admin) throw DomainException.Forbidden("ترقية حساب إلى مدير نظام لمدير النظام فقط");
        if (t.Role != role) await sessions.RevokeAllAsync(t, "role", ct); // the role is in the token: sign in again
        t.Role = role;
        await db.SaveChangesAsync(ct);
        return Mapper.User(t);
    }

    private async Task<UserDto> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var t = await TargetAsync(id, Permissions.UsersSuspend, ct);
        if (!active) await sessions.RevokeAllAsync(t, "suspended", ct);
        t.Active = active;
        await db.SaveChangesAsync(ct);
        return Mapper.User(t);
    }

    // ───── events calendar ─────
    private async Task<CamelEventDto> SaveEventAsync(CamelEventDto e, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.EventsManage, ct);
        if (string.IsNullOrWhiteSpace(e.Title) || string.IsNullOrEmpty(e.StartsOn) || string.IsNullOrEmpty(e.EndsOn)) throw new DomainException("أكمل اسم الفعالية والتواريخ");
        if (string.CompareOrdinal(e.EndsOn, e.StartsOn) < 0) throw new DomainException("تاريخ النهاية قبل البداية");
        var id = string.IsNullOrWhiteSpace(e.Id) ? "ev-" + Guid.NewGuid().ToString("N")[..10] : e.Id;
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) { row = new CamelEvent { Id = id }; db.Events.Add(row); }
        row.Title = e.Title.Trim(); row.TitleEn = e.TitleEn ?? ""; row.Kind = e.Kind; row.StartsOn = e.StartsOn; row.EndsOn = e.EndsOn; row.Approx = e.Approx;
        row.City = e.City ?? ""; row.Country = e.Country ?? ""; row.Description = e.Description ?? ""; row.Url = e.Url;
        row.CityEn = e.CityEn; row.CountryEn = e.CountryEn; row.DescriptionEn = e.DescriptionEn;
        await db.SaveChangesAsync(ct);
        return Mapper.Event(row);
    }

    private async Task DeleteEventAsync(string id, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.EventsManage, ct);
        await db.Events.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
    }

    // ───── MediatR entry points ─────
    public Task<AdminOverviewDto> Handle(GetOverviewQuery q, CancellationToken ct) => OverviewAsync(ct);
    public Task<List<RequestDto>> Handle(ListRequestsQuery q, CancellationToken ct) => ListRequestsAsync(q.Status, ct);
    public Task<RequestDto> Handle(ReviewRequestCommand q, CancellationToken ct) => ReviewAsync(q.Id, q.Decisions, q.Note, ct);
    public Task<RequestDto> Handle(RejectRequestCommand q, CancellationToken ct) => RejectAsync(q.Id, q.Reason, ct);
    public Task<RequestDto> Handle(CancelRequestCommand q, CancellationToken ct) => CancelAsync(q.Id, q.Reason, ct);
    public Task<List<AwaitingDto>> Handle(GetAwaitingScheduleQuery q, CancellationToken ct) => AwaitingScheduleAsync(ct);
    public Task<List<AuctionDto>> Handle(ScheduleAuctionsCommand q, CancellationToken ct) => ScheduleAsync(q.CandidateIds, q.StartsAt, q.EndsAt, ct);
    public Task<List<InvoiceDto>> Handle(ListInvoicesQuery q, CancellationToken ct) => AllInvoicesAsync(ct);
    public Task<InvoiceDto> Handle(MarkInvoicePaidCommand q, CancellationToken ct) => MarkInvoicePaidAsync(q.Id, ct);
    public Task<List<UserDto>> Handle(ListUsersQuery q, CancellationToken ct) => ListUsersAsync(ct);
    public Task<UserDto> Handle(CreateStaffCommand q, CancellationToken ct) => CreateStaffAsync(q.Input, ct);
    public Task<UserDto> Handle(SetUserRoleCommand q, CancellationToken ct) => SetRoleAsync(q.Id, q.Role, ct);
    public Task<UserDto> Handle(SetUserActiveCommand q, CancellationToken ct) => SetActiveAsync(q.Id, q.Active, ct);
    public Task<CamelEventDto> Handle(SaveEventCommand q, CancellationToken ct) => SaveEventAsync(q.Event, ct);
    public async Task<MediatR.Unit> Handle(DeleteEventCommand q, CancellationToken ct) { await DeleteEventAsync(q.Id, ct); return Unit.Value; }
}
