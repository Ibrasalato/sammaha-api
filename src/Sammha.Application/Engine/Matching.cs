using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Engine;

/// <summary>Name availability: registered names plus names in live requests and unsettled auctions.</summary>
public class Matching(IAppDbContext db, IOptions<PlatformOptions> rules)
{
    /// <summary>pool entries from other owners' live requests (pending payment, under review, accepted without an auction): not public</summary>
    public const string RequestSource = "request";

    public static bool IsPrivateSource(string? source) => source == RequestSource;

    /// <summary>
    /// the public form of a check (/api/names/check): hits from private sources keep their type, source and score
    /// (they still drive the verdict and the count) but lose the name text.
    /// </summary>
    public static NameCheck Redact(NameCheck c) => c with
    {
        Hits = c.Hits.Select(h => IsPrivateSource(h.Source) ? h with { Name = "" } : h).ToList(),
    };

    public async Task<List<MatchSource>> PoolAsync(Guid? excludeRequestId, CancellationToken ct)
    {
        var pool = await db.ReservedNames.AsNoTracking().Select(r => new MatchSource(r.Name, r.Normalized, r.Type, "reserved")).ToListAsync(ct);
        var live = new[] { RequestStatuses.UnderReview, RequestStatuses.Accepted, RequestStatuses.PendingPayment };
        var rows = await (from c in db.Candidates.AsNoTracking()
                          join r in db.Requests.AsNoTracking() on c.RequestId equals r.Id
                          join a in db.Auctions.AsNoTracking() on c.AuctionId equals (Guid?)a.Id into aj
                          from a in aj.DefaultIfEmpty()
                          where live.Contains(r.Status) && c.Status != CandidateStatuses.Rejected && c.Status != CandidateStatuses.Withdrawn
                                && (excludeRequestId == null || r.Id != excludeRequestId)
                                && !(r.Kind == RequestKinds.Compound && r.Status == RequestStatuses.Accepted)
                                && (a == null || !a.Settled)
                          select new { c.Name, c.Normalized, r.Type, HasAuction = a != null }).ToListAsync(ct);
        pool.AddRange(rows.Select(x => new MatchSource(x.Name, x.Normalized, x.Type, x.HasAuction ? "auction" : RequestSource)));
        return pool;
    }

    public async Task<List<SimilarHit>> SimilarAsync(string normalized, Guid? excludeRequestId, CancellationToken ct) =>
        NameMatcher.FindMatches(normalized, await PoolAsync(excludeRequestId, ct), rules.Value.SimilarThreshold);

    public async Task<List<NameCheck>> CheckAsync(IReadOnlyList<string> names, Guid? excludeRequestId, CancellationToken ct)
    {
        var pool = await PoolAsync(excludeRequestId, ct);
        var seen = new HashSet<string>();
        var outList = new List<NameCheck>();
        foreach (var name in names)
        {
            var normalized = NameMatcher.Normalize(name);
            var invalid = NameMatcher.Validate(name);
            if (invalid != null) { outList.Add(new(name, normalized, "invalid", invalid, [])); continue; }
            if (!seen.Add(normalized)) { outList.Add(new(name, normalized, "duplicate", "مكرر داخل نفس الطلب", [])); continue; }
            var hits = NameMatcher.FindMatches(normalized, pool, rules.Value.SimilarThreshold);
            var exact = hits.FirstOrDefault(h => h.Score >= 1);
            if (exact != null) outList.Add(new(name, normalized, "exact", $"مطابق تمامًا لاسم {(exact.Source == "reserved" ? "مسجل" : "تحت الطلب أو المزاد")}: «{(IsPrivateSource(exact.Source) ? name.Trim() : exact.Name)}»", hits));
            else if (hits.Count > 0) outList.Add(new(name, normalized, "similar", $"متاح، وستراجع اللجنة تشابهه مع {NameMatcher.NamesCount(hits.Count)}", hits));
            else outList.Add(new(name, normalized, "available", "متاح", []));
        }
        return outList;
    }
}
