using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Common;

/// <summary>Entity → DTO, with the computed flags the UI relies on (cancellable, withdrawable, masked names…).</summary>
public class Mapper(IAppDbContext db, TimeProvider clock, ICurrentUser me)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>a staff account with its permissions (owners get none)</summary>
    public static UserDto User(User u, IReadOnlyCollection<string>? permissions, StaffRole? role) => User(u) with
    {
        Permissions = Roles.IsStaff(u.Role) ? (permissions ?? []).ToList() : null,
        StaffRoleId = u.Role == Roles.Committee ? role?.Id : null,
        StaffRoleName = u.Role == Roles.Admin ? "مدير النظام" : u.Role == Roles.Committee ? role?.Name : null,
    };

    public static UserDto User(User u) => new(u.Id, u.FullName, u.Phone, u.Email, u.NationalId, u.Role, u.CreatedAt, u.AvatarUrl, u.ShowAvatar ? true : null, u.Active,
        Profile: u.Role == Roles.Owner ? Profile(u) : null);

    public static ProfileDto Profile(User u)
    {
        var missing = new List<string>();
        if (u.FirstName == null || u.FamilyName == null || u.NationalId == null || u.BirthDate == null || u.Gender == null || u.Nationality == null || u.ResidenceCountry == null) missing.Add("personal");
        if (u.EmailVerifiedAt == null) missing.Add("email");
        if (u.AddressCountry == null || u.AddressCity == null || u.District == null || u.PostalCode == null) missing.Add("address");
        return new ProfileDto(u.FirstName, u.FatherName, u.GrandfatherName, u.FamilyName, u.BirthDate?.ToString("yyyy-MM-dd"), u.Gender, u.Nationality,
            u.ResidenceCountry, u.ResidenceCity, u.AltPhone, u.AddressCountry, u.AddressCity, u.District, u.PostalCode, u.Street,
            u.EmailVerifiedAt != null, (3 - missing.Count) * 100 / 3, missing);
    }

    public static List<SimilarHitDto> Hits(string json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<SimilarHitDto>>(json, Json) ?? [];
    public static string HitsJson(IEnumerable<SimilarHit> hits) =>
        JsonSerializer.Serialize(hits.Select(h => new SimilarHitDto(h.Name, h.Type, h.Source, h.Score)), Json);
    public static List<SimilarHitDto> Hits(IEnumerable<SimilarHit> hits) => hits.Select(h => new SimilarHitDto(h.Name, h.Type, h.Source, h.Score)).ToList();

    // ───── requests ─────
    /// <summary>Owner may cancel before review, or after acceptance while none of its auctions has started.</summary>
    public static bool Cancellable(LogoRequest r, IReadOnlyDictionary<Guid, Auction> auctions, DateTimeOffset now)
    {
        if (r.Status is RequestStatuses.PendingPayment or RequestStatuses.UnderReview) return true;
        if (r.Status != RequestStatuses.Accepted || r.Kind == RequestKinds.Compound) return false;
        return r.Candidates.All(c => c.AuctionId is not { } aid || !auctions.TryGetValue(aid, out var a) || (now < a.StartsAt && a.BidCount == 0));
    }

    /// <summary>A name can be withdrawn while pending or accepted and its auction (if any) has not started.</summary>
    public static bool Withdrawable(LogoRequest r, Candidate c, IReadOnlyDictionary<Guid, Auction> auctions, DateTimeOffset now)
    {
        if (r.Status is not (RequestStatuses.PendingPayment or RequestStatuses.UnderReview or RequestStatuses.Accepted)) return false;
        if (r.Kind == RequestKinds.Compound) return false;
        if (c.Status is not (CandidateStatuses.Pending or CandidateStatuses.Accepted)) return false;
        return c.AuctionId is not { } aid || !auctions.TryGetValue(aid, out var a) || (now < a.StartsAt && a.BidCount == 0);
    }

    public async Task<Dictionary<Guid, Auction>> AuctionsOfAsync(IEnumerable<LogoRequest> requests, CancellationToken ct)
    {
        var ids = requests.SelectMany(r => r.Candidates).Where(c => c.AuctionId != null).Select(c => c.AuctionId!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Auctions.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
    }

    /// <summary>the committee sees every similar name; the applicant does not see names from other owners' unpublished requests</summary>
    public CandidateDto Candidate(LogoRequest r, Candidate c, IReadOnlyDictionary<Guid, Auction> auctions)
    {
        var hits = Hits(c.SimilarJson);
        if (me.Id != null && me.Id == r.UserId) hits = PublicHits(hits);
        return new(c.Id, c.Name, c.Normalized, c.Status, hits, c.RejectReason, c.AuctionId, c.WithdrawnAt, Withdrawable(r, c, auctions, Now));
    }

    /// <summary>similar hits as shown outside the committee: names from live requests (not yet public) are blanked, the hit still counts</summary>
    public static List<SimilarHitDto> PublicHits(IEnumerable<SimilarHitDto> hits) =>
        hits.Select(h => Matching.IsPrivateSource(h.Source) ? h with { Name = "" } : h).ToList();

    public RequestDto Request(LogoRequest r, IReadOnlyDictionary<Guid, Auction> auctions) =>
        new(r.Id, r.Number, r.UserId, r.User?.FullName ?? "", r.Type, r.Candidates.OrderBy(c => c.Position).Select(c => Candidate(r, c, auctions)).ToList(), r.Status,
            r.DepositAmount, r.DepositPaid, r.DepositState, r.CreatedAt, r.SubmittedAt, r.ReviewedAt, r.ReviewerName, r.ReviewNote, Cancellable(r, auctions, Now),
            r.CancelledAt, r.CancelledBy, r.CancelReason, r.Kind, r.BaseName, r.BaseReservedId);

    public async Task<RequestDto> RequestAsync(LogoRequest r, CancellationToken ct) => Request(r, await AuctionsOfAsync([r], ct));

    public async Task<List<RequestDto>> RequestsAsync(List<LogoRequest> list, CancellationToken ct)
    {
        var auctions = await AuctionsOfAsync(list, ct);
        return list.Select(r => Request(r, auctions)).ToList();
    }

    // ───── auctions ─────
    public async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        if (set.Count == 0) return [];
        return await db.Users.AsNoTracking().Where(u => set.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private bool? _staffViewer;

    /// <summary>an active committee/admin session (its token's stamp still current) sees user ids; everyone else gets the public projection</summary>
    public async Task<bool> IsStaffViewerAsync(CancellationToken ct)
    {
        if (_staffViewer is { } known) return known;
        var staff = false;
        if (me.Id is { } uid && me.DelegationId == null)
        {
            var stamp = me.Stamp;
            staff = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == uid && u.Active && u.SecurityStamp == stamp && (u.Role == Roles.Committee || u.Role == Roles.Admin), ct);
        }
        _staffViewer = staff;
        return staff;
    }

    /// <summary>full projection for staff screens: user ids and the winner's full name</summary>
    public static AuctionDto Auction(Auction a, IReadOnlyDictionary<Guid, string> names) =>
        new(a.Id, a.RequestId, a.CandidateId, a.Name, a.Type, a.RequesterId, a.OpeningPrice, a.MinIncrement, a.StartsAt, a.EndsAt, a.CurrentPrice, a.BidCount,
            a.LeaderId, a.LeaderId is { } l && names.TryGetValue(l, out var ln) ? NameMatcher.MaskName(ln) : null, a.Settled,
            a.WinnerId, a.WinnerId is { } w && names.TryGetValue(w, out var wn) ? wn : null, a.FinalPrice, a.WonByRequester,
            a.Extensions > 0 ? a.Extensions : null, a.IsInvestment ? true : null, a.ListingId, a.BidDeposit,
            a.PausedAt != null ? true : null, a.PausedAt, a.PausedAt != null ? a.PauseReason : null, a.CancelledAt != null ? true : null, a.CancelReason, string.IsNullOrEmpty(a.Number) ? null : a.Number, Transliterator.ToLatin(a.Name));

    /// <summary>
    /// public projection: no user ids (the anonymous registry maps ids to full names, which would unmask bidders),
    /// the winner masked like the leader, and flags relative to the viewer instead.
    /// </summary>
    public static AuctionDto PublicAuction(Auction a, IReadOnlyDictionary<Guid, string> names, Guid? viewer)
    {
        var full = Auction(a, names);
        return WithFlags(full with
        {
            RequesterId = null,
            LeaderId = null,
            WinnerId = null,
            WinnerName = full.WinnerName is { } wn ? NameMatcher.MaskName(wn) : null,
        }, a, viewer);
    }

    private static AuctionDto WithFlags(AuctionDto d, Auction a, Guid? viewer) => viewer is not { } v ? d : d with
    {
        IsLeader = a.LeaderId == v ? true : null,
        IsWinner = a.WinnerId == v ? true : null,
        IsRequester = a.RequesterId == v ? true : null,
    };

    /// <summary>for the signed-in caller: the full projection for staff, otherwise the public one with the caller's flags</summary>
    public async Task<List<AuctionDto>> AuctionsAsync(List<Auction> list, CancellationToken ct)
    {
        var names = await NamesAsync(list.SelectMany(a => new[] { a.LeaderId, a.WinnerId }), ct);
        if (await IsStaffViewerAsync(ct)) return list.Select(a => WithFlags(Auction(a, names), a, me.Id)).ToList();
        return list.Select(a => PublicAuction(a, names, me.Id)).ToList();
    }

    /// <summary>
    /// the anonymous public projection, used for realtime pushes that go to everyone in the lobby or auction room.
    /// It deliberately ignores the caller: a push runs inside the bidder's (or staff member's) request, and their flags
    /// or ids must not reach other viewers. Clients refetch their own view (GET /api/auctions/{id}) on each push.
    /// </summary>
    public async Task<AuctionDto> AuctionAsync(Auction a, CancellationToken ct) =>
        PublicAuction(a, await NamesAsync([a.LeaderId, a.WinnerId], ct), null);

    public async Task<AuctionViewDto> ViewAsync(Auction a, CancellationToken ct)
    {
        var bids = await db.Bids.AsNoTracking().Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenByDescending(b => b.CreatedAt).ToListAsync(ct);
        var names = await NamesAsync(bids.Select(b => (Guid?)b.UserId).Concat([a.LeaderId, a.WinnerId]), ct);
        var staff = await IsStaffViewerAsync(ct);
        AuctionMineDto? mine = null;
        if (me.Id is { } uid)
        {
            var watching = await db.Watches.AnyAsync(w => w.AuctionId == a.Id && w.UserId == uid, ct);
            var auto = await db.AutoBids.Where(x => x.AuctionId == a.Id && x.UserId == uid).Select(x => (decimal?)x.Max).FirstOrDefaultAsync(ct);
            var dep = await db.BidDeposits.AnyAsync(d => d.AuctionId == a.Id && d.UserId == uid && d.State == BidDepositStates.Held, ct);
            mine = new AuctionMineDto(watching, auto, dep ? true : null);
        }
        return new AuctionViewDto(staff ? WithFlags(Auction(a, names), a, me.Id) : PublicAuction(a, names, me.Id),
            bids.Select(b => new BidDto(b.Id, b.AuctionId, staff ? b.UserId : (Guid?)null, NameMatcher.MaskName(names.GetValueOrDefault(b.UserId)), b.Amount, b.CreatedAt, b.Auto ? true : null,
                me.Id != null && b.UserId == me.Id ? true : null)).ToList(),
            mine);
    }

    // ───── registry ─────
    public static ReservedNameDto Reserved(ReservedName r, string? avatar = null, Guid? listingId = null, string? logoStatus = null) =>
        new(r.Id, r.Name, r.Normalized, r.Type, r.OwnerId, r.OwnerName, r.Price, r.AcquiredAt, r.Source, r.BaseName, avatar, listingId,
            LatinOf(r), string.IsNullOrWhiteSpace(r.LatinName) ? null : true, LogoUrl(r), logoStatus);

    /// <summary>the approved logo's address (relative to the API), versioned so a new logo is not served from cache</summary>
    public static string? LogoUrl(ReservedName r) => r.LogoAt is { } at && r.OwnerId != null ? $"/api/names/{r.Id}/logo?v={at.ToUnixTimeSeconds()}" : null;

    /// <summary>the registry's Latin spelling: staff's override, or the transliteration</summary>
    public static string LatinOf(ReservedName r) => string.IsNullOrWhiteSpace(r.LatinName) ? Transliterator.ToLatin(r.Name) : r.LatinName!;

    // ───── money ─────
    public static WalletTxDto Tx(WalletTx t) => new(t.Id, t.UserId, t.Amount, t.Kind, t.Note, t.CreatedAt);

    public static InvoiceDto Invoice(Invoice i, User buyer) =>
        new(i.Id, i.Number, i.UserId, buyer.FullName, buyer.Phone, i.AuctionId ?? i.ListingId, i.Name, i.Type, i.Total, i.Vat, i.Subtotal, i.DepositApplied,
            i.AmountDue, i.Status, i.IssuedAt, i.PaidAt, i.OwnerShare, i.OwnerShareTo, i.OwnerShare != null ? i.OwnerSharePaid : null, i.ListingId, i.DueAt, i.CancelledAt, i.RequestNumber);

    public async Task<List<InvoiceDto>> InvoicesAsync(List<Invoice> list, CancellationToken ct)
    {
        var ids = list.Select(i => i.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return list.Select(i => Invoice(i, users[i.UserId])).ToList();
    }

    public async Task<InvoiceDto> InvoiceAsync(Invoice i, CancellationToken ct) => (await InvoicesAsync([i], ct))[0];

    // ───── listings ─────
    public async Task<List<ListingDto>> ListingsAsync(List<NameListing> list, CancellationToken ct)
    {
        var aids = list.Where(l => l.AuctionId != null).Select(l => l.AuctionId!.Value).ToList();
        var auctions = aids.Count == 0 ? [] : await db.Auctions.AsNoTracking().Where(a => aids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return list.Select(l =>
        {
            var own = me.Id == l.SellerId;
            Auction? a = l.AuctionId is { } id ? auctions.GetValueOrDefault(id) : null;
            return new ListingDto(l.Id, l.Number, l.ReservedId, l.Name, l.Type, own ? l.SellerId : null, l.Mode, l.Price, l.CommissionRate, l.Status, l.CreatedAt,
                l.StartsAt, l.EndsAt, l.AuctionId, l.SoldPrice, l.SoldAt, own ? l.Payout : null, own ? l.PayoutPaid : null, a?.CurrentPrice ?? l.Price, a?.BidCount);
        }).ToList();
    }

    public async Task<ListingDto> ListingAsync(NameListing l, CancellationToken ct) => (await ListingsAsync([l], ct))[0];

    // ───── misc ─────
    public static CamelEventDto Event(CamelEvent e) =>
        new(e.Id, e.Title, e.TitleEn, e.Kind, e.StartsOn, e.EndsOn, e.Approx, e.City, e.Country, e.Description, e.Url, e.CityEn, e.CountryEn, e.DescriptionEn);

    public static NotificationDto Notification(Notification n) => new(n.Id, n.UserId, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.Read);

    /// <summary>customer view hides staff-only notes and the assignee</summary>
    public static TicketDto Ticket(Ticket t, bool staff) =>
        new(t.Id, t.Number, t.Kind, t.Topic, t.Subject, t.Name, t.Phone, t.Email, t.RefNumber, t.Message, t.UserId, t.Status, t.Priority,
            staff ? t.Assignee : null,
            t.Messages.Where(m => staff || !m.Internal).OrderBy(m => m.CreatedAt).Select(m => new TicketMessageDto(m.Id, m.By, m.Author, m.Body, m.Internal ? true : null, m.CreatedAt)).ToList(),
            t.CreatedAt, t.UpdatedAt, staff ? t.AssigneeId : null);
}
