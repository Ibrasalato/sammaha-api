using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Features.Admin;

/// <summary>an existing name a requested name matches; Price is what it sold for (registered) or its current bid (in an auction)</summary>
public record AuditMatchDto(string Name, string Type, string Source, double Score, string Relation, decimal? Price, string? PriceKind, DateTimeOffset? At);

public record AuditRowDto(Guid RequestId, string Number, string Kind, string Type, string OwnerName, string RequestStatus, DateTimeOffset? SubmittedAt,
    Guid CandidateId, string Name, string Status, string? RejectReason, string Verdict, List<AuditMatchDto> Matches, AuditMatchDto? TopPrice);

/// <summary>Committee audit of requested names: exact and similar matches (with or without «ال», singular or plural) and the highest price a similar name fetched.</summary>
public record RequestsAuditQuery(string? Status, string? Type) : IQuery<List<AuditRowDto>>;

public class RequestAuditHandler(IAppDbContext db, Access access, IOptions<PlatformOptions> rules) : IRequestHandler<RequestsAuditQuery, List<AuditRowDto>>
{
    private record Known(string Name, string Normalized, string Type, string Source, decimal? Price, string? PriceKind, DateTimeOffset? At, Guid? RequestId);

    public async Task<List<AuditRowDto>> Handle(RequestsAuditQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.RequestsView, ct);
        var status = string.IsNullOrEmpty(q.Status) ? RequestStatuses.UnderReview : q.Status;
        var rq = db.Requests.AsNoTracking().Include(r => r.User).Include(r => r.Candidates).Where(r => r.Status != RequestStatuses.PendingPayment);
        if (status != "all") rq = rq.Where(r => r.Status == status);
        if (NameTypes.IsValid(q.Type)) rq = rq.Where(r => r.Type == q.Type);
        var requests = await rq.OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt).Take(300).ToListAsync(ct);

        // the records: registered names with their price, and names in live requests and running auctions
        var pool = await db.ReservedNames.AsNoTracking()
            .Select(r => new Known(r.Name, r.Normalized, r.Type, "reserved", r.Price, "sold", r.AcquiredAt, null)).ToListAsync(ct);
        var live = new[] { RequestStatuses.UnderReview, RequestStatuses.Accepted };
        var pending = await (from c in db.Candidates.AsNoTracking()
                             join r in db.Requests.AsNoTracking() on c.RequestId equals r.Id
                             join a in db.Auctions.AsNoTracking() on c.AuctionId equals (Guid?)a.Id into aj
                             from a in aj.DefaultIfEmpty()
                             where live.Contains(r.Status) && c.Status != CandidateStatuses.Rejected && c.Status != CandidateStatuses.Withdrawn
                                   && (a == null || (!a.Settled && a.CancelledAt == null))
                             select new { c.Name, c.Normalized, r.Type, r.Id, Current = a != null ? (decimal?)a.CurrentPrice : null, StartsAt = a != null ? (DateTimeOffset?)a.StartsAt : null })
                             .Take(5000).ToListAsync(ct);
        pool.AddRange(pending.Select(x => new Known(x.Name, x.Normalized, x.Type, x.Current != null ? "auction" : "request", x.Current, x.Current != null ? "current" : null, x.StartsAt, x.Id)));

        var threshold = rules.Value.SimilarThreshold;
        var rows = new List<AuditRowDto>();
        foreach (var r in requests)
            foreach (var c in r.Candidates.OrderBy(x => x.Position))
            {
                var matches = new List<AuditMatchDto>();
                foreach (var k in pool)
                {
                    if (k.RequestId == r.Id) continue; // the request's own names
                    // a compound name contains its own base name
                    if (r.Kind == RequestKinds.Compound && k.Normalized == NameMatcher.Normalize(r.BaseName)) continue;
                    var score = NameMatcher.Similarity(c.Normalized, k.Normalized);
                    var forms = NameMatcher.SameForms(c.Normalized, k.Normalized);
                    if (score < threshold && !forms) continue;
                    if (forms && score < 0.9) score = 0.9;
                    matches.Add(new AuditMatchDto(k.Name, k.Type, k.Source, Math.Round(score, 2), NameMatcher.Relation(c.Name, k.Name), k.Price, k.PriceKind, k.At));
                }
                matches = matches.OrderByDescending(m => m.Score).ThenByDescending(m => m.Price ?? 0).Take(12).ToList();
                var top = matches.Where(m => m.PriceKind == "sold" && m.Price > 0).OrderByDescending(m => m.Price).FirstOrDefault();
                var verdict = matches.Any(m => m.Score >= 1) ? "exact" : matches.Count > 0 ? "similar" : "clear";
                rows.Add(new AuditRowDto(r.Id, r.Number, r.Kind, r.Type, r.User.FullName, r.Status, r.SubmittedAt, c.Id, c.Name, c.Status, c.RejectReason, verdict, matches, top));
            }
        return rows;
    }
}
