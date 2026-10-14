using MediatR;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Requests;

/// <summary>Logo requests (three names), compound names and «استثمار الاسم», as their owner sees them.</summary>
public class RequestHandlers(IAppDbContext db, TimeProvider clock, Access access, Mapper map, Matching matching, Money money, SettingsStore settings, IOptions<PlatformOptions> rules)
    : IRequestHandler<GetMyRequestsQuery, List<RequestDto>>, IRequestHandler<GetRequestQuery, RequestDto>, IRequestHandler<CreateRequestCommand, RequestDto>, IRequestHandler<CreateCompoundRequestCommand, RequestDto>, IRequestHandler<CreateInvestmentRequestCommand, RequestDto>, IRequestHandler<GetMyInvestmentsQuery, List<InvestmentItemDto>>, IRequestHandler<PayDepositCommand, RequestDto>, IRequestHandler<CancelMyRequestCommand, CancelResult>, IRequestHandler<WithdrawNameCommand, WithdrawResult>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private PlatformOptions R => rules.Value;

    private IQueryable<LogoRequest> Full => db.Requests.Include(r => r.User).Include(r => r.Candidates);

    private async Task<List<RequestDto>> MineAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await Full.AsNoTracking().Where(r => r.UserId == u.Id).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return await map.RequestsAsync(list, ct);
    }

    /// <summary>owner sees their own; staff see any (with similar names recomputed for review)</summary>
    private async Task<RequestDto> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var r = await Full.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null || (u.Role == Roles.Owner && r.UserId != u.Id)) throw DomainException.NotFound("الطلب غير موجود");
        if (u.Role != Roles.Owner && r.Status == RequestStatuses.UnderReview)
        {
            foreach (var c in r.Candidates)
            {
                var hits = await matching.SimilarAsync(c.Normalized, r.Id, ct);
                if (r.Kind == RequestKinds.Compound && r.BaseName != null) hits = hits.Where(h => NameMatcher.Normalize(h.Name) != NameMatcher.Normalize(r.BaseName)).ToList();
                c.SimilarJson = Mapper.HitsJson(hits);
            }
            await db.SaveChangesAsync(ct);
        }
        return await map.RequestAsync(r, ct);
    }

    private async Task<LogoRequest> NewRequestAsync(User u, string type, string kind, decimal deposit, IEnumerable<NameCheck> checks, CancellationToken ct)
    {
        var no = await db.NextNumberAsync("RequestNo", ct);
        var r = new LogoRequest
        {
            Number = $"SM-{no}", UserId = u.Id, User = u, Type = type, Kind = kind, Status = RequestStatuses.PendingPayment,
            DepositAmount = deposit, DepositState = DepositStates.None, CreatedAt = Now,
        };
        var pos = 0;
        foreach (var c in checks)
            r.Candidates.Add(new Candidate { Position = ++pos, Name = c.Name.Trim(), Normalized = c.Normalized, SimilarJson = Mapper.HitsJson(c.Hits) });
        db.Requests.Add(r);
        return r;
    }

    private async Task<RequestDto> CreateAsync(string type, List<string> names, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await settings.EnsureOpenAsync(Intakes.Requests, ct);
        if (!NameTypes.IsValid(type)) throw new DomainException("اختر النوع");
        if (names.Count != R.NamesPerRequest) throw new DomainException($"أدخل {R.NamesPerRequest} أسماء");
        var checks = await matching.CheckAsync(names, null, ct);
        var blocked = checks.Where(c => c.Verdict is "exact" or "invalid" or "duplicate").ToList();
        if (blocked.Count > 0) throw new DomainException($"غيّر الأسماء المرفوضة: {string.Join("، ", blocked.Select(b => string.IsNullOrWhiteSpace(b.Name) ? "—" : b.Name))}");
        var r = await NewRequestAsync(u, type, RequestKinds.Standard, R.DepositAmount, checks, ct);
        await db.SaveChangesAsync(ct);
        return await map.RequestAsync(r, ct);
    }

    private async Task<RequestDto> CreateCompoundAsync(Guid reservedId, string fullName, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await settings.EnsureOpenAsync(Intakes.Compound, ct);
        var b = await db.ReservedNames.FirstOrDefaultAsync(x => x.Id == reservedId && x.OwnerId == u.Id, ct) ?? throw new DomainException("اختر اسمًا من أسمائك المسجلة");
        var unpaid = await db.Invoices.FirstOrDefaultAsync(i => i.UserId == u.Id && i.Name == b.Name && i.Status == InvoiceStatuses.Unpaid, ct);
        if (unpaid != null) throw new DomainException($"سدّد فاتورة «{b.Name}» أولًا ({unpaid.Number})");
        var name = Regex.Replace((fullName ?? "").Trim(), @"\s+", " ");
        var n = NameMatcher.Normalize(name);
        if (!n.Contains(b.Normalized) || n == b.Normalized) throw new DomainException($"الاسم المركب يتكون من «{b.Name}» مع كلمة أخرى");
        if (name.Split(' ').Length > 4 || name.Length > 40) throw new DomainException("الاسم المركب طويل، اكتفِ بكلمتين أو ثلاث");
        var check = (await matching.CheckAsync([name], null, ct))[0];
        if (check.Verdict == "invalid") throw new DomainException(check.Message);
        if (check.Verdict == "exact") throw new DomainException($"«{name}» مسجل أو مطلوب مسبقًا");
        if (await db.Requests.AnyAsync(r => r.Kind == RequestKinds.Compound && r.BaseReservedId == b.Id && r.Status == RequestStatuses.UnderReview, ct))
            throw new DomainException($"لديك طلب اسم مركب لـ«{b.Name}» تحت المراجعة");
        var hits = check.Hits.Where(h => NameMatcher.Normalize(h.Name) != b.Normalized).ToList();
        var req = await NewRequestAsync(u, b.Type, RequestKinds.Compound, 0, [check with { Name = name, Hits = hits }], ct);
        req.Status = RequestStatuses.UnderReview;
        req.SubmittedAt = Now;
        req.BaseName = b.Name;
        req.BaseReservedId = b.Id;
        await db.SaveChangesAsync(ct);
        return await map.RequestAsync(req, ct);
    }

    private async Task<RequestDto> CreateInvestmentAsync(string type, List<string> names, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await settings.EnsureOpenAsync(Intakes.Investment, ct);
        if (!NameTypes.IsValid(type)) throw new DomainException("اختر النوع");
        var list = names.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToList();
        if (list.Count == 0) throw new DomainException("اكتب اسمًا واحدًا على الأقل");
        if (list.Count > R.InvestMaxNames) throw new DomainException($"الحد الأقصى {R.InvestMaxNames} أسماء في الطلب");
        var checks = await matching.CheckAsync(list, null, ct);
        foreach (var c in checks)
        {
            if (c.Verdict == "invalid") throw new DomainException(c.Message);
            if (c.Verdict == "duplicate") throw new DomainException($"«{c.Name.Trim()}» مكرر في الطلب");
            if (c.Verdict == "exact") throw new DomainException($"«{c.Name.Trim()}» مسجل أو مطلوب مسبقًا، اختر اسمًا جديدًا غير مكرر");
        }
        var r = await NewRequestAsync(u, type, RequestKinds.Investment, R.InvestFee, checks, ct);
        await db.SaveChangesAsync(ct);
        return await map.RequestAsync(r, ct);
    }

    private async Task<List<InvestmentItemDto>> MyInvestmentsAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await Full.AsNoTracking().Where(r => r.UserId == u.Id && r.Kind == RequestKinds.Investment).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var auctions = await map.AuctionsOfAsync(list, ct);
        var aids = auctions.Keys.ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.AuctionId != null && aids.Contains(i.AuctionId.Value)).ToDictionaryAsync(i => i.AuctionId!.Value, ct);
        var auctionDtos = (await map.AuctionsAsync(auctions.Values.ToList(), ct)).ToDictionary(a => a.Id);
        var outList = new List<InvestmentItemDto>();
        foreach (var r in list)
        {
            var dto = map.Request(r, auctions);
            foreach (var c in r.Candidates.OrderBy(c => c.Position))
            {
                AuctionDto? a = c.AuctionId is { } aid ? auctionDtos.GetValueOrDefault(aid) : null;
                var inv = c.AuctionId is { } id ? invoices.GetValueOrDefault(id) : null;
                outList.Add(new InvestmentItemDto(dto, map.Candidate(r, c, auctions), a, inv?.OwnerShare, inv?.OwnerShare != null ? inv.OwnerSharePaid : null,
                    a is { Settled: true } && a.WinnerId == r.UserId));
            }
        }
        return outList;
    }

    /// <summary>Pays the auction guarantee (or the investment study fee) from the wallet and sends the request to the committee.</summary>
    private async Task<RequestDto> PayDepositAsync(Guid id, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"u:{u.Id}", $"r:{id}"], ct);
        var r = await Full.FirstOrDefaultAsync(x => x.Id == id && x.UserId == u.Id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        if (r.Status != RequestStatuses.PendingPayment) throw new DomainException("تم سداد ضمان هذا الطلب");
        // a name may have been taken since the request was drafted
        var ordered = r.Candidates.OrderBy(c => c.Position).ToList();
        var checks = await matching.CheckAsync(ordered.Select(c => c.Name).ToList(), r.Id, ct);
        var taken = checks.FirstOrDefault(c => c.Verdict == "exact");
        if (taken != null) throw new DomainException($"أصبح الاسم «{taken.Name}» محجوزًا، ألغِ الطلب وأنشئ طلبًا جديدًا");
        var fee = r.Kind == RequestKinds.Investment;
        await money.DebitAsync(u.Id, r.DepositAmount, fee ? TxKinds.ServiceFee : TxKinds.Deposit,
            fee ? $"رسوم دراسة طلب استثمار الاسم {r.Number}" : $"ضمان مزاد الطلب {r.Number}", r.Id, ct);
        for (var i = 0; i < ordered.Count; i++) ordered[i].SimilarJson = Mapper.HitsJson(checks[i].Hits);
        r.Status = RequestStatuses.UnderReview;
        r.DepositPaid = true;
        r.DepositState = fee ? DepositStates.Fee : DepositStates.Held;
        r.SubmittedAt = Now;
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return await map.RequestAsync(r, ct);
    }

    private async Task<decimal> CancelMineAsync(Guid id, string? reason, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"r:{id}"], ct);
        var r = await Full.FirstOrDefaultAsync(x => x.Id == id && x.UserId == u.Id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        if (r.Status == RequestStatuses.Cancelled) throw new DomainException("الطلب ملغى مسبقًا");
        var auctions = await map.AuctionsOfAsync([r], ct);
        if (!Mapper.Cancellable(r, auctions, Now))
            throw new DomainException(r.Status == RequestStatuses.Accepted ? "بدأ مزاد أحد أسماء هذا الطلب، فلا يمكن إلغاؤه الآن" : "لا يمكن إلغاء هذا الطلب");
        await RemoveScheduledAsync(r.Candidates.Where(c => c.AuctionId != null).Select(c => c.AuctionId!.Value).ToList(), ct);
        foreach (var c in r.Candidates) c.AuctionId = null;
        var held = r.DepositState == DepositStates.Held ? r.DepositAmount : 0;
        r.Status = RequestStatuses.Cancelled;
        r.CancelledAt = Now;
        r.CancelledBy = "owner";
        r.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(300, reason.Trim().Length)];
        await money.RefundDepositAsync(r, "ألغاه المالك", ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return held;
    }

    private async Task<(decimal Refunded, bool RequestCancelled)> WithdrawNameAsync(Guid id, Guid candidateId, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        await using var uow = await db.BeginAsync([$"r:{id}"], ct);
        var r = await Full.FirstOrDefaultAsync(x => x.Id == id && x.UserId == u.Id, ct);
        var c = r?.Candidates.FirstOrDefault(x => x.Id == candidateId);
        if (r == null || c == null) throw DomainException.NotFound("الاسم غير موجود");
        var auctions = await map.AuctionsOfAsync([r], ct);
        if (!Mapper.Withdrawable(r, c, auctions, Now))
            throw new DomainException(c.Status == CandidateStatuses.Withdrawn ? "سُحب هذا الاسم مسبقًا" : c.AuctionId != null ? "بدأ مزاد هذا الاسم، فلا يمكن سحبه" : "لا يمكن سحب هذا الاسم");
        if (c.AuctionId is { } aid) { await RemoveScheduledAsync([aid], ct); c.AuctionId = null; }
        c.Status = CandidateStatuses.Withdrawn;
        c.WithdrawnAt = Now;
        (decimal, bool) result = (0, false);
        if (!r.Candidates.Any(x => x.Status is CandidateStatuses.Pending or CandidateStatuses.Accepted))
        {
            var held = r.DepositState == DepositStates.Held ? r.DepositAmount : 0;
            var others = await db.Auctions.Where(a => a.RequestId == r.Id).Select(a => a.Settled).ToListAsync(ct);
            if (others.Count == 0)
            {
                r.Status = RequestStatuses.Cancelled;
                r.CancelledAt = Now;
                r.CancelledBy = "owner";
                r.CancelReason = "سحب المالك كل الأسماء";
                await money.RefundDepositAsync(r, "سحب المالك كل الأسماء", ct);
                result = (held, true);
            }
            else if (others.All(s => s))
            {
                await money.RefundDepositAsync(r, "لم يُرسَ عليك أي اسم", ct);
                result = (r.DepositState == DepositStates.Refunded ? held : 0, false);
            }
        }
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return result;
    }

    /// <summary>removes auctions that were scheduled but have not started</summary>
    private async Task RemoveScheduledAsync(List<Guid> auctionIds, CancellationToken ct)
    {
        if (auctionIds.Count == 0) return;
        await db.Watches.Where(w => auctionIds.Contains(w.AuctionId)).ExecuteDeleteAsync(ct);
        await db.AutoBids.Where(w => auctionIds.Contains(w.AuctionId)).ExecuteDeleteAsync(ct);
        var list = await db.Auctions.Where(a => auctionIds.Contains(a.Id)).ToListAsync(ct);
        db.Auctions.RemoveRange(list);
    }

    // ───── MediatR entry points ─────
    public Task<List<RequestDto>> Handle(GetMyRequestsQuery q, CancellationToken ct) => MineAsync(ct);
    public Task<RequestDto> Handle(GetRequestQuery q, CancellationToken ct) => GetAsync(q.Id, ct);
    public Task<RequestDto> Handle(CreateRequestCommand q, CancellationToken ct) => CreateAsync(q.Type, q.Names, ct);
    public Task<RequestDto> Handle(CreateCompoundRequestCommand q, CancellationToken ct) => CreateCompoundAsync(q.ReservedId, q.FullName, ct);
    public Task<RequestDto> Handle(CreateInvestmentRequestCommand q, CancellationToken ct) => CreateInvestmentAsync(q.Type, q.Names, ct);
    public Task<List<InvestmentItemDto>> Handle(GetMyInvestmentsQuery q, CancellationToken ct) => MyInvestmentsAsync(ct);
    public Task<RequestDto> Handle(PayDepositCommand q, CancellationToken ct) => PayDepositAsync(q.RequestId, ct);
    public async Task<CancelResult> Handle(CancelMyRequestCommand q, CancellationToken ct) { return new CancelResult(await CancelMineAsync(q.RequestId, q.Reason, ct)); }
    public async Task<WithdrawResult> Handle(WithdrawNameCommand q, CancellationToken ct) { var (r, c) = await WithdrawNameAsync(q.RequestId, q.CandidateId, ct); return new WithdrawResult(r, c); }
}
