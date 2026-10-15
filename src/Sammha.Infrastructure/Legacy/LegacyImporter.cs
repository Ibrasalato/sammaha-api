using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;
using Sammha.Infrastructure.Data;

namespace Sammha.Infrastructure.Legacy;

/// <summary>how the old lookup ids translate; built from the lookup names by keyword, any entry can be overridden by a JSON file</summary>
public sealed class LegacyMap
{
    /// <summary>candidate status per C_Order_Status id: pending | accepted | rejected | withdrawn</summary>
    public Dictionary<int, string> OrderStatus { get; set; } = [];
    /// <summary>C_Bidding_Status ids that mean the auction was cancelled</summary>
    public List<int> BiddingCancelled { get; set; } = [];
    /// <summary>C_AccountStatus ids that mean a suspended account</summary>
    public List<int> AccountSuspended { get; set; } = [];
    /// <summary>C_Sex id → male | female</summary>
    public Dictionary<int, string> Sex { get; set; } = [];
    /// <summary>C_ReservationType ids of «منقية»</summary>
    public List<int> Manqiya { get; set; } = [];
    /// <summary>C_Nationality / C_Country id → ISO 3166 two-letter code</summary>
    public Dictionary<int, string> Nationality { get; set; } = [];
    public Dictionary<int, string> Country { get; set; } = [];
    public Dictionary<int, string> City { get; set; } = [];
}

public sealed class LegacyReport
{
    public SortedDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Warnings { get; } = new(StringComparer.Ordinal);
    public void Count(string k, int n = 1) => Counts[k] = Counts.GetValueOrDefault(k) + n;
    public void Warn(string k, int n = 1) => Warnings[k] = Warnings.GetValueOrDefault(k) + n;

    public void Print(TextWriter o)
    {
        o.WriteLine("── imported ──");
        foreach (var (k, v) in Counts) o.WriteLine($"  {k,-40} {v,10:N0}");
        o.WriteLine("── to review ──");
        if (Warnings.Count == 0) o.WriteLine("  (nothing)");
        foreach (var (k, v) in Warnings) o.WriteLine($"  {k,-40} {v,10:N0}");
    }
}

/// <summary>
/// Moves the previous Sammha platform's data into this database. Every record remembers its old id (LegacyLinks) so a
/// second run adds nothing twice. Runs in one transaction: a dry run rolls it back after the report.
/// Never imported: staff accounts (recreated), saved payment cards, logs and job tables.
/// </summary>
public sealed class LegacyImporter(SammhaDbContext db, IPasswordHasher hasher, SettingsStore settings, IOptions<PlatformOptions> rules, TimeProvider clock, ICertificateSigner signer)
{
    public const string Sentinel = "المنصة السابقة";
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);
    private readonly LegacyReport _r = new();
    private DateTimeOffset _now;
    private int _pending;
    private CancellationToken _ct;

    private static readonly string[] Tables =
    [
        "Members", "Orders", "Biddings", "Biddings_Details", "Biddings_Winners", "Bills", "CompoundNames_Master", "CompoundNames_Details",
        "Members_Notifications", "Name_Logo", "MemberCamels", "Black_List", "MemberExcluded", "Request", "PaymentCard", "Administrators", "ContactUs", "C_Settings",
    ];

    // ───────────────────────── inspect ─────────────────────────

    /// <summary>table sizes and how each lookup value will be read — no personal data</summary>
    public async Task<LegacyMap> InspectAsync(LegacySource src, TextWriter o, CancellationToken ct)
    {
        o.WriteLine("── tables ──");
        foreach (var t in Tables) o.WriteLine($"  {t,-28} {await src.CountAsync(t, ct),10:N0}");
        var map = await AutoMapAsync(src, ct);
        async Task Show(string table, Func<int, string> how)
        {
            o.WriteLine($"── {table} ──");
            foreach (var r in await src.ReadAsync(table, ct)) o.WriteLine($"  {r.Id,4}  {NameOf(r),-40} → {how(r.Id)}");
        }
        await Show("C_Order_Status", id => map.OrderStatus.GetValueOrDefault(id, "pending"));
        await Show("C_Bidding_Status", id => map.BiddingCancelled.Contains(id) ? "cancelled" : "normal");
        await Show("C_AccountStatus", id => map.AccountSuspended.Contains(id) ? "suspended" : "active");
        await Show("C_Sex", id => map.Sex.GetValueOrDefault(id, "?"));
        await Show("C_ReservationType", id => map.Manqiya.Contains(id) ? "manqiya" : "single (قعدان/بكار by sex)");
        foreach (var t in new[] { "C_BidPayStatus", "C_WinCancleReason", "C_BiddingType", "C_RequestType", "C_RequestStatus", "C_BillType" }) await Show(t, _ => "info");
        o.WriteLine($"nationalities mapped: {map.Nationality.Count}, countries: {map.Country.Count}, cities: {map.City.Count}");
        if (await src.CountAsync("PaymentCard", ct) > 0)
            o.WriteLine("!! PaymentCard holds card numbers/CVV: it is NOT imported. Wipe it on the old server (PCI DSS forbids storing CVV).");
        return map;
    }

    private static string NameOf(Row r)
    {
        foreach (var c in r.Columns)
            if (!c.Equals("ID", StringComparison.OrdinalIgnoreCase) && !c.Contains("Code") && !c.Contains("En") && !c.Contains("Old") && r[c] is string s) return s.Trim();
        return "";
    }

    public static async Task<LegacyMap> AutoMapAsync(LegacySource src, CancellationToken ct)
    {
        var m = new LegacyMap();
        foreach (var r in await src.ReadAsync("C_Order_Status", ct))
        {
            var n = NameOf(r);
            m.OrderStatus[r.Id] = Ar.Has(n, "رفض", "مرفوض") ? "rejected"
                : Ar.Has(n, "سحب", "انسحاب", "ملغ", "الغاء", "الغي") ? "withdrawn"
                : Ar.Has(n, "قبول", "مقبول", "موافق", "معتمد", "اعتماد", "تحويل", "مزاد", "طرح") ? "accepted" : "pending";
        }
        foreach (var r in await src.ReadAsync("C_Bidding_Status", ct))
            if (Ar.Has(NameOf(r), "ملغ", "الغاء", "الغي")) m.BiddingCancelled.Add(r.Id);
        foreach (var r in await src.ReadAsync("C_AccountStatus", ct))
            if (Ar.Has(NameOf(r), "موقوف", "ايقاف", "معطل", "محظور", "مجمد", "محذوف", "غير")) m.AccountSuspended.Add(r.Id);
        foreach (var r in await src.ReadAsync("C_Sex", ct))
        {
            var n = NameOf(r);
            if (Ar.Has(n, "انث", "female")) m.Sex[r.Id] = "female";
            else if (Ar.Has(n, "ذكر", "male")) m.Sex[r.Id] = "male";
        }
        foreach (var r in await src.ReadAsync("C_ReservationType", ct))
            if (Ar.Has(NameOf(r), "منقي")) m.Manqiya.Add(r.Id);
        foreach (var r in await src.ReadAsync("C_Nationality", ct))
            if (Iso2(r.S("Nationality_Code")) is { } c) m.Nationality[r.Id] = c;
        foreach (var r in await src.ReadAsync("C_Country", ct))
            if (Iso2(r.S("Country_Code")) is { } c) m.Country[r.Id] = c;
        foreach (var r in await src.ReadAsync("C_City", ct))
            if (r.S("City_NameAr") is { } c) m.City[r.Id] = Cut(c, 60)!;
        return m;
    }

    private static string? Iso2(string? c) => c is { Length: 2 } && c.All(char.IsAsciiLetter) ? c.ToUpperInvariant() : null;

    public static LegacyMap Merge(LegacyMap auto, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return auto;
        var o = JsonSerializer.Deserialize<LegacyMap>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        foreach (var (k, v) in o.OrderStatus) auto.OrderStatus[k] = v;
        foreach (var (k, v) in o.Sex) auto.Sex[k] = v;
        foreach (var (k, v) in o.Nationality) auto.Nationality[k] = v;
        foreach (var (k, v) in o.Country) auto.Country[k] = v;
        foreach (var (k, v) in o.City) auto.City[k] = v;
        if (o.BiddingCancelled.Count > 0) auto.BiddingCancelled = o.BiddingCancelled;
        if (o.AccountSuspended.Count > 0) auto.AccountSuspended = o.AccountSuspended;
        if (o.Manqiya.Count > 0) auto.Manqiya = o.Manqiya;
        return auto;
    }

    // ───────────────────────── import ─────────────────────────

    public async Task<LegacyReport> ImportAsync(LegacySource src, LegacyMap map, bool commit, CancellationToken ct, string? filesRoot = null)
    {
        _ct = ct;
        _now = clock.GetUtcNow();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Database.SetCommandTimeout(600);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var links = await db.LegacyLinks.AsNoTracking().ToDictionaryAsync(l => (l.Kind, l.LegacyId), l => l.NewId, ct);
        // the old platform's guarantee and auction settings apply first, so live auctions carry them
        await SettingsAsync(src);
        var deposit = await settings.DepositAmountAsync(rules.Value.DepositAmount, ct);
        var increment = await settings.MinIncrementAsync(rules.Value.MinIncrement, ct);
        var deadline = await settings.PaymentDeadlineHoursAsync(ct);

        // ── members ──
        var users = new Dictionary<int, Guid>();
        var fullNames = new Dictionary<Guid, string>();
        var newUsers = new HashSet<int>();
        await MembersAsync(src, map, links, users, fullNames, newUsers);

        // ── «الحظر»: Black_List (an account, with dates) and MemberExcluded (an ID number, for good) become bans, kept with their dates ──
        var banDay = DateOnly.FromDateTime(_now.ToOffset(Riyadh).DateTime);
        foreach (var r in await src.ReadAsync("Black_List", ct))
        {
            if (links.ContainsKey((LegacyKinds.Ban, r.Id))) continue;
            if (!users.TryGetValue(r.I("BL_Member_FK") ?? 0, out var uid)) { _r.Warn("bans.withoutMember"); continue; }
            var start = r.Dt("BL_StartDate") is { } sd ? DateOnly.FromDateTime(sd) : banDay;
            DateOnly? end = r.Dt("BL_EndDate") is { } ed ? DateOnly.FromDateTime(ed) : null;
            if (end < start) end = start;
            var who = await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => new { u.FullName, u.NationalId }).FirstAsync(ct);
            var ban = new Ban { UserId = uid, NationalId = who.NationalId, Name = who.FullName, Reason = Sentinel, StartsOn = start, EndsOn = end, Source = BanSources.LegacyBlacklist, CreatedAt = _now, CreatedByName = Sentinel };
            Add(ban);
            Link(LegacyKinds.Ban, r.Id, ban.Id);
            _r.Count(ban.InForce(banDay) ? "bans.active" : "bans.ended");
        }
        foreach (var r in await src.ReadAsync("MemberExcluded", ct))
        {
            if (links.ContainsKey((LegacyKinds.Excluded, r.Id))) continue;
            var nid = new string((r.S("IdentityNumber") ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (nid.Length < 5) { _r.Warn("bans.excludedWithoutId"); continue; }
            var acc = await db.Users.AsNoTracking().Where(u => u.Role == Roles.Owner && u.NationalId == nid).Select(u => new { u.Id, u.FullName }).FirstOrDefaultAsync(ct);
            var ban = new Ban
            {
                UserId = acc?.Id, NationalId = Cut(nid, 20), Name = Cut(r.S("Name"), 150) ?? acc?.FullName ?? nid, Reason = Cut(r.S("Note"), 500) ?? Sentinel,
                StartsOn = banDay, EndsOn = null, Source = BanSources.LegacyExcluded, CreatedAt = _now, CreatedByName = Sentinel,
            };
            Add(ban);
            Link(LegacyKinds.Excluded, r.Id, ban.Id);
            _r.Count("bans.excluded");
        }
        await FlushAsync(true);

        // ── load the rest ──
        var bills = await src.ReadAsync("Bills", ct);
        var byType = bills.ToLookup(b => b.I("Bill_Type_FK") ?? 0);
        var refundedBills = byType[3].Select(b => b.I("Bill_PaiedRelatedToBillId")).Where(x => x != null).Select(x => x!.Value).ToHashSet();
        bool Paid(Row b) => b.B("Bill_IsPaied") && !b.B("Bill_IsCancled");
        bool Refunded(Row b) => b.B("Bill_IsRefunded") || b.B("Bill_IsRefundedToWallit") || refundedBills.Contains(b.Id);
        var t1 = byType[1].ToLookup(b => b.I("Bill_Data_FK") ?? 0);
        var t2 = byType[2].ToLookup(b => b.I("Bill_Data_FK") ?? 0);
        var t4 = byType[4].ToLookup(b => b.I("Bill_Data_FK") ?? 0);
        var t5 = byType[5].ToLookup(b => b.I("Bill_Data_FK") ?? 0);
        var t8 = byType[8].ToLookup(b => b.I("Bill_Data_FK") ?? 0);
        foreach (var g in byType) if (g.Key is 7 or 9 or 0) _r.Warn($"bills.type{g.Key}.skipped", g.Count());

        var orders = (await src.ReadAsync("Orders", ct)).ToDictionary(o => o.Id);
        var biddings = await src.ReadAsync("Biddings", ct);
        var winners = (await src.ReadAsync("Biddings_Winners", ct)).ToLookup(w => w.I("BidWin_BidCode_FK") ?? 0);
        var details = new Dictionary<int, List<(int Member, decimal Value, DateTimeOffset At)>>();
        await foreach (var d in src.StreamAsync("Biddings_Details", ct))
        {
            var bid = d.I("BidDet_Master_FK") ?? 0;
            if (!details.TryGetValue(bid, out var l)) details[bid] = l = [];
            l.Add((d.I("BidDet_Member_FK") ?? 0, d.D("BidDet_Value") ?? 0, At(d.Dt("BidDet_Datetime")) ?? _now));
        }

        static string[] Pos() => ["First", "Second", "Third"];
        // which order choice each auction came from
        var choiceOf = new Dictionary<int, (int Order, int Pos)>();
        foreach (var o in orders.Values)
            for (var p = 0; p < 3; p++)
                if (o.I($"Order_{Pos()[p]}_BID_ID") is { } b) choiceOf.TryAdd(b, (o.Id, p + 1));
        foreach (var b in biddings)
            if (!choiceOf.ContainsKey(b.Id) && b.I("Bid_Order_FK") is { } oid && orders.ContainsKey(oid))
                choiceOf[b.Id] = (oid, b.I("Bid_OrderOption_FK") is int op and >= 1 and <= 3 ? op : 1);
        bool Excepted(int order, int pos) => orders.TryGetValue(order, out var o) && o.B($"Order_{Pos()[pos - 1]}_Selected") && o.B($"Order_{Pos()[pos - 1]}_Exception");

        string TypeOf(Row? o)
        {
            if (o == null) return NameTypes.Qaadan;
            if (o.I("Order_ResType_FK") is { } rt && map.Manqiya.Contains(rt)) return NameTypes.Manqiya;
            return o.I("Order_Sex_FK") is { } sx && map.Sex.GetValueOrDefault(sx) == "female" ? NameTypes.Bakkar : NameTypes.Qaadan;
        }

        // ── what happened to each auction (decided before anything is written) ──
        var outcomes = new Dictionary<int, Outcome>();
        foreach (var b in biddings)
        {
            var oc = new Outcome { Row = b, Id = links.GetValueOrDefault((LegacyKinds.Bidding, b.Id), Guid.NewGuid()) };
            if (choiceOf.TryGetValue(b.Id, out var ch)) { oc.Order = ch.Order; oc.Pos = ch.Pos; }
            oc.Starts = At(b.Dt("Bid_Start")) ?? _now;
            oc.Ends = At(b.Dt("Bid_End")) ?? oc.Starts;
            oc.Cancelled = b.I("Bid_Status_FK") is { } st && map.BiddingCancelled.Contains(st);
            var dets = details.GetValueOrDefault(b.Id) ?? [];
            var top = dets.OrderByDescending(d => d.Value).ThenBy(d => d.At).Cast<(int Member, decimal Value, DateTimeOffset At)?>().FirstOrDefault();
            oc.Top = top;
            oc.Ended = oc.Cancelled || oc.Ends <= _now;
            outcomes[b.Id] = oc;
            if (!oc.Ended || oc.Cancelled) { oc.Kind = oc.Cancelled ? OutcomeKind.Cancelled : OutcomeKind.Live; continue; }

            var rows = winners[b.Id].ToList();
            var final = rows.Where(w => w.B("BidWin_IsFinalWinner") && w.Dt("BidWin_Withdrawal_Date") == null).OrderByDescending(w => w.Dt("BidWin_Date")).FirstOrDefault()
                        ?? rows.Where(w => w.Dt("BidWin_Withdrawal_Date") == null).OrderByDescending(w => w.Dt("BidWin_Date")).FirstOrDefault();
            var excepted = oc.Order != 0 && Excepted(oc.Order, oc.Pos);
            oc.Winner = final?.I("BidWin_Member_FK") ?? (rows.Count == 0 ? top?.Member : rows.OrderByDescending(w => w.Dt("BidWin_Date")).First().I("BidWin_Member_FK"));
            oc.Price = final?.D("BidWin_Value") ?? top?.Value ?? b.D("Bid_MaxVlaue") ?? 0;
            if (oc.Winner == null) { oc.Kind = OutcomeKind.NoWinner; continue; }
            oc.Bill4 = t4[b.Id].Where(x => x.I("Bill_Member_FK") == oc.Winner).OrderByDescending(Paid).ThenByDescending(x => x.Id).FirstOrDefault();
            var reg8 = t8[b.Id].FirstOrDefault(Paid);
            if (oc.Bill4 != null && Paid(oc.Bill4)) { oc.Kind = OutcomeKind.Owned; oc.Owner = oc.Winner; oc.OwnedAt = At(oc.Bill4.Dt("Bill_PayDateTime")) ?? oc.Ends; }
            else if (reg8 != null) { oc.Kind = OutcomeKind.Registered; oc.Owner = reg8.I("Bill_Member_FK"); oc.Bill8 = reg8; oc.OwnedAt = At(reg8.Dt("Bill_PayDateTime")) ?? oc.Ends; }
            else if (excepted || final == null || (oc.Bill4 != null && oc.Bill4.B("Bill_IsCancled"))) oc.Kind = OutcomeKind.Ico;
            else oc.Kind = OutcomeKind.Unpaid;
        }

        // ── requests and their names ──
        var reqNumbers = (await db.Requests.Select(r => r.Number).ToListAsync(ct)).ToHashSet();
        var candIds = new Dictionary<(int, int), Guid>();
        var reqIds = new Dictionary<int, Guid>();
        var newOrders = new HashSet<int>();
        var reserved = (await db.ReservedNames.Select(n => n.Normalized).ToListAsync(ct)).ToHashSet();
        foreach (var o in orders.Values)
        {
            if (links.TryGetValue((LegacyKinds.Order, o.Id), out var have)) { reqIds[o.Id] = have; continue; }
            if (!users.TryGetValue(o.I("Order_Member_FK") ?? 0, out var uid)) { _r.Warn("orders.withoutMember"); continue; }
            var req = new LogoRequest
            {
                Number = reqNumbers.Contains($"SM-{o.Id}") ? $"SM-L{o.Id}" : $"SM-{o.Id}", UserId = uid, Type = TypeOf(o),
                Kind = o.B("IsInvestment") ? RequestKinds.Investment : RequestKinds.Standard, CreatedAt = At(o.Dt("Order_Date")) ?? _now,
            };
            reqIds[o.Id] = req.Id;
            newOrders.Add(o.Id);
            var states = new List<string>();
            // Order_X_Selected marks the slots the owner used (one name → First; two → First+Second; three → all)
            var anySelected = Pos().Any(x => o.B($"Order_{x}_Selected"));
            for (var p = 1; p <= 3; p++)
            {
                var pre = $"Order_{Pos()[p - 1]}_";
                if (o.S(pre + "Choice") is not { } name) continue;
                if (anySelected && !o.B(pre + "Selected")) { _r.Warn("candidates.unselectedSlotSkipped"); continue; }
                var st = o.B(pre + "IsConvertedToBid") ? "accepted" : map.OrderStatus.GetValueOrDefault(o.I(pre + "Status_FK") ?? 0, "pending");
                var c = new Candidate
                {
                    RequestId = req.Id, Position = p, Name = Cut(name, 150)!, Normalized = NameMatcher.Normalize(name),
                    Status = st switch { "accepted" => CandidateStatuses.Accepted, "rejected" => CandidateStatuses.Rejected, "withdrawn" => CandidateStatuses.Withdrawn, _ => CandidateStatuses.Pending },
                    RejectReason = Cut(o.S(pre + "RejectReasone"), 400),
                };
                if (c.Status == CandidateStatuses.Withdrawn) c.WithdrawnAt = req.CreatedAt;
                var bidId = o.I(pre + "BID_ID") ?? outcomes.Values.FirstOrDefault(x => x.Order == o.Id && x.Pos == p)?.Row.Id;
                if (bidId is { } bi && outcomes.TryGetValue(bi, out var oc)) c.AuctionId = oc.Id;
                candIds[(o.Id, p)] = c.Id;
                states.Add(c.Status);
                req.Candidates.Add(c);
                _r.Count("candidates");
                // an excepted choice that never reached an auction still belongs to the organization
                if (Excepted(o.Id, p) && c.AuctionId == null) IcoName(name, req.Type, null, null, null, null, req.CreatedAt, reserved);
            }

            var dep = t1[o.Id].Where(Paid).OrderBy(b => b.Id).FirstOrDefault();
            req.DepositAmount = dep?.D("Bill_Total") ?? t1[o.Id].FirstOrDefault()?.D("Bill_Total") ?? 0;
            req.DepositPaid = dep != null;
            var mine = outcomes.Values.Where(x => x.Order == o.Id).ToList();
            req.DepositState = dep == null ? DepositStates.None
                : Refunded(dep) ? DepositStates.Refunded
                : mine.Any(x => x.Kind == OutcomeKind.Ico && x.Winner == o.I("Order_Member_FK")) ? DepositStates.Forfeited
                : DepositStates.Held;
            if (req.DepositState == DepositStates.Held && mine.All(x => x.Kind is not (OutcomeKind.Live or OutcomeKind.Unpaid)) && !states.Contains(CandidateStatuses.Pending))
                _r.Warn("deposits.heldOnFinishedRequest (finance review)");
            req.Status = states.Contains(CandidateStatuses.Accepted) ? RequestStatuses.Accepted
                : states.Count > 0 && states.All(s => s == CandidateStatuses.Rejected) ? RequestStatuses.Rejected
                : states.Count > 0 && states.All(s => s is CandidateStatuses.Rejected or CandidateStatuses.Withdrawn) ? RequestStatuses.Cancelled
                : req.DepositPaid || o.B("IsBillPaied") ? RequestStatuses.UnderReview : RequestStatuses.PendingPayment;
            if (req.Status != RequestStatuses.PendingPayment) req.SubmittedAt = At(o.Dt("PaiedDateTime")) ?? req.CreatedAt;
            if (req.Status == RequestStatuses.Cancelled) { req.CancelledAt = req.CreatedAt; req.CancelledBy = Sentinel; }
            Add(req);
            Link(LegacyKinds.Order, o.Id, req.Id);
            _r.Count("requests");
            await FlushAsync();
        }
        await FlushAsync(true);

        // ── auctions, bids, invoices, names ──
        var aucNumbers = (await db.Auctions.Select(a => a.Number).ToListAsync(ct)).ToHashSet();
        var invNumbers = (await db.Invoices.Select(i => i.Number).ToListAsync(ct)).ToHashSet();
        foreach (var oc in outcomes.Values)
        {
            if (links.ContainsKey((LegacyKinds.Bidding, oc.Row.Id))) continue;
            var b = oc.Row;
            var o = oc.Order != 0 ? orders.GetValueOrDefault(oc.Order) : null;
            var requester = o != null ? users.GetValueOrDefault(o.I("Order_Member_FK") ?? 0) : Guid.Empty;
            if (oc.Order != 0 && !candIds.ContainsKey((oc.Order, oc.Pos)) && reqIds.TryGetValue(oc.Order, out var rq))
                candIds[(oc.Order, oc.Pos)] = await db.Candidates.Where(c => c.RequestId == rq && c.Position == oc.Pos).Select(c => c.Id).FirstOrDefaultAsync(ct);
            var name = b.S("Bid_Name") ?? (o != null ? o.S($"Order_{Pos()[oc.Pos - 1]}_Choice") : null) ?? "";
            var type = TypeOf(o);
            var u = (int? m) => m is { } x && users.TryGetValue(x, out var g) ? g : (Guid?)null;
            var dets = details.GetValueOrDefault(b.Id) ?? [];
            var a = new Auction
            {
                Id = oc.Id, Number = aucNumbers.Contains($"AU-{b.Id}") ? $"AU-L{b.Id}" : $"AU-{b.Id}",
                RequestId = reqIds.TryGetValue(oc.Order, out var r0) ? r0 : null,
                CandidateId = candIds.TryGetValue((oc.Order, oc.Pos), out var cid) && cid != Guid.Empty ? cid : null,
                Name = Cut(name, 150)!, Type = type, RequesterId = requester,
                OpeningPrice = b.D("Bid_StartValue") ?? 0, MinIncrement = increment, StartsAt = oc.Starts, EndsAt = oc.Ends,
                CurrentPrice = oc.Top?.Value ?? b.D("Bid_StartValue") ?? 0, BidCount = dets.Count, LeaderId = u(oc.Top?.Member),
                Settled = oc.Ended, IsInvestment = o?.B("IsInvestment") == true,
                InvestorId = u(b.I("InvestedByMember")), InvestorSharePercent = b.I("InvestedPersenategValue"),
            };
            if (oc.Cancelled) { a.CancelledAt = oc.Ends; a.CancelReason = $"ألغي في {Sentinel}"; }
            if (oc.Ended && oc.Winner is { } w)
            {
                a.WinnerId = u(w);
                a.FinalPrice = oc.Price;
                a.WonByRequester = a.WinnerId != null && a.WinnerId == requester;
            }
            if (oc.Kind == OutcomeKind.Live) a.BidDeposit = deposit > 0 ? deposit : null;
            Add(a);
            Link(LegacyKinds.Bidding, b.Id, a.Id);
            _r.Count($"auctions.{oc.Kind.ToString().ToLowerInvariant()}");

            foreach (var d in dets)
            {
                if (u(d.Member) is not { } bu) { _r.Warn("bids.withoutMember"); continue; }
                Add(new Bid { AuctionId = a.Id, UserId = bu, Amount = d.Value, CreatedAt = d.At });
                _r.Count("bids");
                await FlushAsync();
            }

            // entry guarantees paid by other bidders
            foreach (var g in t2[b.Id].Where(Paid).GroupBy(x => x.I("Bill_Member_FK") ?? 0))
            {
                if (u(g.Key) is not { } du) continue;
                var bill = g.First();
                Add(new BidDeposit
                {
                    AuctionId = a.Id, UserId = du, Amount = bill.D("Bill_Total") ?? 0, CreatedAt = At(bill.Dt("Bill_PayDateTime")) ?? a.StartsAt,
                    State = Refunded(bill) ? BidDepositStates.Refunded : oc.Kind == OutcomeKind.Ico && oc.Winner == g.Key ? BidDepositStates.Forfeited : BidDepositStates.Held,
                });
                _r.Count("bidDeposits");
            }

            var winnerId = u(oc.Winner);
            switch (oc.Kind)
            {
                case OutcomeKind.Owned when winnerId is { } wid:
                    Invoice(oc.Bill4!, wid, a, name, type, InvoiceStatuses.Paid, invNumbers);
                    Owned(name, type, wid, oc.Price, oc.OwnedAt, a.IsInvestment ? ReservedSources.Investment : ReservedSources.Auction, a.Id, reserved, fullNames);
                    break;
                case OutcomeKind.Registered when u(oc.Owner) is { } oid:
                    Invoice(oc.Bill8!, oid, a, name, type, InvoiceStatuses.Paid, invNumbers);
                    Owned(name, type, oid, oc.Bill8!.D("Bill_Value") ?? 0, oc.OwnedAt, ReservedSources.Legacy, a.Id, reserved, fullNames);
                    break;
                case OutcomeKind.Ico:
                    if (oc.Bill4 != null && winnerId is { } lw) Invoice(oc.Bill4, lw, a, name, type, InvoiceStatuses.Cancelled, invNumbers);
                    var forfeited = winnerId == requester && o != null ? t1[o.Id].FirstOrDefault(Paid)?.D("Bill_Total")
                        : t2[b.Id].FirstOrDefault(x => Paid(x) && x.I("Bill_Member_FK") == oc.Winner)?.D("Bill_Total");
                    IcoName(name, type, a.Id, winnerId, winnerId is { } fw ? fullNames.GetValueOrDefault(fw) : null, oc.Price, oc.Ends, reserved, forfeited);
                    break;
                case OutcomeKind.Unpaid when winnerId is { } uw:
                    // the winner gets the full payment period from the migration day; the sweeper forfeits it after that
                    var inv = oc.Bill4 != null ? Invoice(oc.Bill4, uw, a, name, type, InvoiceStatuses.Unpaid, invNumbers) : NewInvoice(uw, a, name, type, oc.Price, invNumbers);
                    inv.DueAt = _now.AddHours(deadline);
                    _r.Warn("invoices.unpaidGivenNewDeadline");
                    break;
            }
            await FlushAsync();
        }
        await FlushAsync(true);

        // ── compound names ──
        var compDetails = (await src.ReadAsync("CompoundNames_Details", ct)).ToLookup(d => d.I("CompDet_Master_FK") ?? 0);
        var ownedByUser = await db.ReservedNames.Where(n => n.OwnerId != null).Select(n => new { n.OwnerId, n.Name, n.Type }).ToListAsync(ct);
        foreach (var m in await src.ReadAsync("CompoundNames_Master", ct))
        {
            if (links.ContainsKey((LegacyKinds.Compound, m.Id))) continue;
            if (!users.TryGetValue(m.I("CompReg_User_FK") ?? 0, out var uid)) { _r.Warn("compound.withoutMember"); continue; }
            var paid = m.B("CompReg_IsBillPaied") || t5[m.Id].Any(Paid);
            if (!paid) { _r.Warn("compound.unpaidSkipped"); continue; }
            var at = At(m.Dt("CompReg_PaiedDateTime")) ?? At(m.Dt("CompReg_Date")) ?? _now;
            var names = compDetails[m.Id].Select(d => d.S("CompDet_Name")).Where(n => n != null).Select(n => n!).ToList();
            foreach (var n in names)
            {
                var norm = NameMatcher.Normalize(n);
                var baseName = ownedByUser.Where(x => x.OwnerId == uid && norm.Contains(NameMatcher.Normalize(x.Name))).OrderByDescending(x => x.Name.Length).FirstOrDefault();
                if (baseName == null) _r.Warn("compound.baseNotFound (type set to قعدان)");
                Owned(n, baseName?.Type ?? NameTypes.Qaadan, uid, 0, at, ReservedSources.Compound, null, reserved, fullNames, baseName?.Name);
            }
            var total = (decimal?)m.D("CompReg_FullValue") ?? t5[m.Id].FirstOrDefault(Paid)?.D("Bill_Total") ?? 0;
            var vat = m.D("CompReg_Vat") ?? 0;
            var no = $"INV-{at.Year}-C{m.Id}";
            if (total > 0 && invNumbers.Add(no))
                Add(new Invoice { Number = no, UserId = uid, Name = Cut(string.Join("، ", names), 150) ?? "", Type = NameTypes.Qaadan, Total = total, Vat = vat, Subtotal = total - vat, AmountDue = total, Status = InvoiceStatuses.Paid, IssuedAt = at, PaidAt = at });
            Link(LegacyKinds.Compound, m.Id, uid);
            _r.Count("compound.registrations");
            await FlushAsync();
        }
        await FlushAsync(true);

        // ── ownership transfers (the old Request table, types named «نقل…») ──
        var reqTypes = (await src.ReadAsync("C_RequestType", ct)).ToDictionary(r => r.Id, NameOf);
        var reqStatus = (await src.ReadAsync("C_RequestStatus", ct)).ToDictionary(r => r.Id, NameOf);
        var winnerById = winners.SelectMany(g => g).GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
        var billById = bills.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());
        var oldRequests = await src.ReadAsync("Request", ct);
        foreach (var q in oldRequests.Where(q => Ar.Has(reqTypes.GetValueOrDefault(q.I("RequestTypeId") ?? 0), "نقل")).OrderBy(q => q.Dt("DateTime")))
        {
            if (links.ContainsKey((LegacyKinds.Transfer, q.Id))) continue;
            if (!users.TryGetValue(q.I("MemberId") ?? 0, out var from) || !users.TryGetValue(q.I("ToMemberId") ?? 0, out var to)) { _r.Warn("transfers.withoutMember"); continue; }
            ReservedName? rn = null;
            if (q.I("Biddings_WinnersId") is { } wid && winnerById.TryGetValue(wid, out var w) && outcomes.TryGetValue(w.I("BidWin_BidCode_FK") ?? 0, out var oc))
                rn = await db.ReservedNames.FirstOrDefaultAsync(n => n.AuctionId == oc.Id, ct);
            if (rn == null && (q.S("Name") ?? q.S("Title")) is { } nm)
            {
                var norm = NameMatcher.Normalize(nm);
                rn = await db.ReservedNames.FirstOrDefaultAsync(n => n.Normalized == norm, ct);
            }
            if (rn == null) { _r.Warn("transfers.nameNotFound"); continue; }
            var st = reqStatus.GetValueOrDefault(q.I("RequestStatusId") ?? 0);
            var status = Ar.Has(st, "رفض") ? TransferStatuses.Rejected
                : Ar.Has(st, "ملغ", "الغاء", "الغي", "سحب") ? TransferStatuses.Cancelled
                : Ar.Has(st, "مقبول", "قبول", "معتمد", "اعتماد", "منفذ", "تنفيذ", "موافق", "مكتمل", "اكتمل", "تم") ? TransferStatuses.Completed
                : TransferStatuses.UnderReview;
            var at = At(q.Dt("DateTime")) ?? _now;
            var done = At(q.Dt("LatestUpdateDateTime")) ?? at;
            var t = new NameTransfer
            {
                Number = $"NT-L{q.Id}", ReservedId = rn.Id, Name = rn.Name, Type = rn.Type, FromId = from, FromName = fullNames.GetValueOrDefault(from, ""),
                ToId = to, ToName = fullNames.GetValueOrDefault(to, ""), Reason = TransferReasons.Other, Note = Cut(q.S("Title") ?? Sentinel, 500),
                Fee = q.I("BillId") is { } bid && billById.TryGetValue(bid, out var fb) && fb.B("Bill_IsPaied") ? fb.D("Bill_Total") ?? 0 : 0,
                Status = status, CreatedAt = at, RespondedAt = at, ReviewNote = Cut(q.S("RejectResion") ?? q.S("LatestNote"), 500),
                ReviewerName = status is TransferStatuses.UnderReview ? null : Cut(q.S("LatestNoteBy"), 120) ?? Sentinel,
                ReviewedAt = status is TransferStatuses.UnderReview ? null : done,
                CompletedAt = status == TransferStatuses.Completed ? done : null, CancelledAt = status == TransferStatuses.Cancelled ? done : null,
            };
            Add(t);
            if (status == TransferStatuses.Completed)
            {
                if (rn.OwnerId == from)
                {
                    rn.OwnerId = to; rn.OwnerName = t.ToName; rn.Source = ReservedSources.Transfer; rn.AcquiredAt = done;
                    _pending++;
                }
                else if (rn.OwnerId != to) _r.Warn("transfers.completedButOwnerDiffers");
            }
            Link(LegacyKinds.Transfer, q.Id, t.Id);
            _r.Count($"transfers.{status}");
            await FlushAsync(true);
        }
        // ── «طلب اسم مستثنى»: taking part in a festival without a name, against a fine (the old Request table, types named «مستثنى/استثناء») ──
        bool IsExemption(Row q) => Ar.Has(reqTypes.GetValueOrDefault(q.I("RequestTypeId") ?? 0), "مستثن", "استثناء");
        var categories = (await src.ReadAsync("C_RequestTypeType", ct)).ToDictionary(r => r.Id, r => (Name: r.S("Name") ?? "", Price: r.D("PriceNoVat") ?? 0));
        foreach (var q in oldRequests.Where(IsExemption).OrderBy(q => q.Dt("DateTime")))
        {
            if (links.ContainsKey((LegacyKinds.Exemption, q.Id))) continue;
            if (!users.TryGetValue(q.I("MemberId") ?? 0, out var owner)) { _r.Warn("exemptions.withoutMember"); continue; }
            var cat = q.I("RequestTypeTypeId") is { } cid && categories.TryGetValue(cid, out var cv) ? cv : (Name: "", Price: 0m);
            // the old platform kept the chip in the request's name or title
            var chip = ChipIn(q.S("Name")) ?? ChipIn(q.S("Title"));
            if (chip == null) _r.Warn("exemptions.withoutChip (review)");
            var st = reqStatus.GetValueOrDefault(q.I("RequestStatusId") ?? 0);
            var bill = q.I("BillId") is { } bid && billById.TryGetValue(bid, out var b) ? b : null;
            var billPaid = bill != null && bill.B("Bill_IsPaied") && !bill.B("Bill_IsRefunded") && !bill.B("Bill_IsCancled");
            var status = billPaid || Ar.Has(st, "تنفيذ", "تم الدفع") ? ExemptionStatuses.Paid
                : Ar.Has(st, "رفض", "مرفوض") ? ExemptionStatuses.Rejected
                : Ar.Has(st, "ملغ", "الغاء", "الغي", "سحب") ? ExemptionStatuses.Cancelled
                : Ar.Has(st, "مقبول", "غير مدفوع", "قبول", "موافق") ? ExemptionStatuses.Priced
                : ExemptionStatuses.Pending;
            decimal? amount = bill?.D("Bill_Total") is { } total and > 0 ? Math.Round(total, 2, MidpointRounding.AwayFromZero) : cat.Price > 0 ? cat.Price : null;
            if (status == ExemptionStatuses.Priced && amount == null) status = ExemptionStatuses.Pending;
            var at = At(q.Dt("DateTime")) ?? _now;
            var done = At(q.Dt("LatestUpdateDateTime")) ?? at;
            var festival = cat.Name.Length == 0 ? Sentinel : Cut(System.Text.RegularExpressions.Regex.Replace(cat.Name.Trim(), @"^(استثناء|غرامة\s*-?)\s*", ""), 150)!;
            var e = new Exemption
            {
                Number = $"EX-L{q.Id}", UserId = owner, CamelChip = chip ?? "", CamelType = Ar.Has(cat.Name, "بكار", "بكره", "بكرة") ? NameTypes.Bakkar : NameTypes.Qaadan,
                FestivalName = festival, OwnerNote = Cut(q.S("Title"), 500), Status = status, CreatedAt = at, Amount = amount,
                StaffNote = Cut(q.S("LatestNote"), 500), PricedAt = amount != null && status != ExemptionStatuses.Pending ? done : null,
                PricedByName = amount != null && status != ExemptionStatuses.Pending ? Cut(q.S("LatestNoteBy"), 120) ?? Sentinel : null,
                RejectedAt = status == ExemptionStatuses.Rejected ? done : null, RejectReason = status == ExemptionStatuses.Rejected ? Cut(q.S("RejectResion") ?? q.S("LatestNote"), 500) ?? Sentinel : null,
                RejectedByName = status == ExemptionStatuses.Rejected ? Cut(q.S("LatestNoteBy"), 120) ?? Sentinel : null,
                CancelledAt = status == ExemptionStatuses.Cancelled ? done : null,
            };
            if (status == ExemptionStatuses.Paid)
            {
                var paidAt = At(bill?.Dt("Bill_PayDateTime")) ?? done;
                var who = await db.Users.AsNoTracking().Where(u => u.Id == owner).Select(u => new { u.NationalId, u.FullNameEn }).FirstOrDefaultAsync(ct);
                e.PaidAt = paidAt;
                e.OwnerNameAr = fullNames.GetValueOrDefault(owner, "");
                e.OwnerNameEn = who?.FullNameEn;
                e.NationalId = who?.NationalId;
                e.IssuedAt = DateTimeOffset.FromUnixTimeSeconds(paidAt.ToUnixTimeSeconds());
                e.Signature = signer.Sign(e.SignedContent());
            }
            Add(e);
            Link(LegacyKinds.Exemption, q.Id, e.Id);
            _r.Count($"exemptions.{status}");
            await FlushAsync(true);
        }
        foreach (var g in oldRequests.Where(q => !Ar.Has(reqTypes.GetValueOrDefault(q.I("RequestTypeId") ?? 0), "نقل") && !IsExemption(q)).GroupBy(q => reqTypes.GetValueOrDefault(q.I("RequestTypeId") ?? 0) ?? "?"))
            _r.Warn($"later.Request.type[{g.Key}]", g.Count());

        // ── wallets: past top-ups and refunds as history, then one adjustment so the balance equals the old balance ──
        var balances = new Dictionary<int, decimal>();
        await foreach (var m in src.StreamAsync("Members", ct)) if (newUsers.Contains(m.Id)) balances[m.Id] = Math.Round(m.D("WalletAmmount") ?? 0, 2);
        var topups = byType[6].Where(Paid).ToLookup(b => b.I("Bill_Member_FK") ?? 0);
        var refunds = byType[3].Where(b => !b.B("Bill_IsCancled")).ToLookup(b => b.I("Bill_Member_FK") ?? 0);
        foreach (var mid in newUsers)
        {
            var uid = users[mid];
            decimal sum = 0;
            foreach (var b in topups[mid]) { var v = b.D("Bill_Total") ?? 0; sum += v; Add(new WalletTx { UserId = uid, Amount = v, Kind = TxKinds.TopUp, Note = $"شحن المحفظة ({Sentinel})", CreatedAt = At(b.Dt("Bill_PayDateTime")) ?? At(b.Dt("Bill_Datetime")) ?? _now }); }
            foreach (var b in refunds[mid]) { var v = b.D("Bill_Total") ?? 0; sum += v; Add(new WalletTx { UserId = uid, Amount = v, Kind = TxKinds.Refund, Note = $"استرداد ضمان ({Sentinel})", CreatedAt = At(b.Dt("Bill_RefundedDate")) ?? At(b.Dt("Bill_Datetime")) ?? _now }); }
            var target = balances.GetValueOrDefault(mid);
            if (target < 0) { _r.Warn("wallets.negativeBalance"); }
            var diff = Math.Round(target - sum, 2);
            if (diff != 0) Add(new WalletTx { UserId = uid, Amount = diff, Kind = TxKinds.Adjustment, Note = $"تسوية الرصيد المنقول من {Sentinel}", CreatedAt = _now });
            if (target != 0) _r.Count("wallets.withBalance");
            await FlushAsync();
        }
        await FlushAsync(true);

        // ── notifications ──
        await foreach (var n in src.StreamAsync("Members_Notifications", ct))
        {
            var mid = n.I("MemNotf_MEM_ID_FK") ?? 0;
            if (!newUsers.Contains(mid)) continue;
            var body = n.S("MemNotf_Description");
            if (body == null) continue;
            var at = ParseDate(n.S("MemNotf_Date"));
            if (at == null) _r.Warn("notifications.dateUnreadable");
            Add(new Notification { UserId = users[mid], Kind = "legacy", Title = "إشعار", Body = Cut(body, 400)!, CreatedAt = at ?? _now, Read = n.B("MemNotf_IsSeen") });
            _r.Count("notifications");
            await FlushAsync();
        }
        await FlushAsync(true);

        // ── «تواصل معنا» messages → closed enquiry tickets (kept for the record; the old site kept no replies) ──
        var phoneOwners = (await db.Users.AsNoTracking().Select(x => new { x.Phone, x.Id }).ToListAsync(ct))
            .GroupBy(x => x.Phone).ToDictionary(g => g.Key, g => g.First().Id);
        await foreach (var c in src.StreamAsync("ContactUs", ct))
        {
            if (links.ContainsKey((LegacyKinds.Ticket, c.Id))) continue;
            var msg = c.S("Cont_Msg")?.Trim();
            if (string.IsNullOrEmpty(msg)) { _r.Warn("tickets.emptySkipped"); continue; }
            var raw = c.S("Cont_Mobile") ?? "";
            var at = At(c.Dt("Cont_Date")) ?? _now;
            var t = new Ticket
            {
                Kind = TicketKinds.Inquiry, Topic = "النظام السابق", Subject = Cut(c.S("Cont_Tilte")?.Trim() is { Length: > 0 } st ? st : "رسالة تواصل", 120)!,
                Name = Cut(c.S("Cont_Name")?.Trim() is { Length: > 0 } nm ? nm : "—", 80)!, Phone = Cut(NameMatcher.NormalizePhone(raw), 20)!,
                Message = Cut(msg, 3000)!, Status = TicketStatuses.Closed, CreatedAt = at, UpdatedAt = at,
            };
            if (Phones.FromInput(raw) is { } up && phoneOwners.TryGetValue(up, out var byIntl)) t.UserId = byIntl;
            else if (phoneOwners.TryGetValue(t.Phone, out var byLocal)) t.UserId = byLocal;
            if (t.UserId == null) _r.Warn("tickets.noMatchingAccount");
            var seq = await db.NextNumberAsync("TicketNo", ct);
            t.Number = $"BL-{at.ToOffset(Riyadh).Year % 100:D2}{seq:D4}";
            t.Messages.Add(new TicketMessage { TicketId = t.Id, By = "staff", Author = Sentinel, Body = "رسالة منقولة من نموذج «تواصل معنا» في المنصة السابقة، ولا يوجد لها رد مسجل.", Internal = true, CreatedAt = at });
            Add(t);
            Link(LegacyKinds.Ticket, c.Id, t.Id);
            _r.Count("tickets");
            await FlushAsync();
        }
        await FlushAsync(true);

        // waiting for their own features
        // ── mark logos (Name_Logo): the image files come from the old site's folder (--files) ──
        if (filesRoot != null && Directory.Exists(filesRoot))
        {
            var root = Path.GetFullPath(filesRoot).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            var logos = (await src.ReadAsync("Name_Logo", ct)).OrderByDescending(l => l.Dt("Name_Date")).ToList();
            var done = new HashSet<Guid>(await db.Logos.Select(l => l.ReservedId).ToListAsync(ct));
            foreach (var l in logos)
            {
                if (links.ContainsKey((LegacyKinds.Logo, l.Id))) continue;
                ReservedName? rn = null;
                if (l.I("Name_Bid_FK") is { } bid && outcomes.TryGetValue(bid, out var oc)) rn = await db.ReservedNames.FirstOrDefaultAsync(n => n.AuctionId == oc.Id, ct);
                if (rn == null && l.S("Name") is { } nm) { var norm = NameMatcher.Normalize(nm); rn = await db.ReservedNames.FirstOrDefaultAsync(n => n.Normalized == norm, ct); }
                if (rn?.OwnerId == null) { _r.Warn("logos.nameNotFound"); continue; }
                if (!done.Add(rn.Id)) { _r.Warn("logos.olderVersionSkipped"); continue; }
                var rel = (l.S("Name_Image_Path") ?? "").Replace('\\', '/').TrimStart('~', '/');
                var path = Path.GetFullPath(Path.Combine(root, rel));
                if (rel.Length == 0 || !path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path)) { _r.Warn("logos.fileMissing"); continue; }
                var bytes = await File.ReadAllBytesAsync(path, ct);
                var type = Sammha.Application.Features.Logos.LogoHandlers.Sniff(bytes);
                if (type == null || bytes.Length > Sammha.Application.Features.Logos.LogoHandlers.MaxBytes) { _r.Warn("logos.notAnImageOrTooLarge"); continue; }
                var at = At(l.Dt("Name_Date")) ?? _now;
                var logo = new NameLogo { ReservedId = rn.Id, OwnerId = rn.OwnerId.Value, Name = rn.Name, FileName = Cut(Path.GetFileName(path), 120)!, ContentType = type, Bytes = bytes,
                    Status = LogoStatuses.Approved, UploadedAt = at, ReviewedAt = at, ReviewedBy = Sentinel };
                Add(logo);
                rn.LogoAt = at;
                _pending++;
                Link(LegacyKinds.Logo, l.Id, logo.Id);
                _r.Count("logos");
                await FlushAsync(true);
            }
        }
        foreach (var t in new[] { "MemberCamels" })
            if (await src.CountAsync(t, ct) is > 0 and var c) _r.Warn($"later.{t}", c);

        // ── numbering continues after the old numbers ──
        if (orders.Count > 0) await BumpAsync("RequestNo", orders.Keys.Max() + 1);
        if (biddings.Count > 0) await BumpAsync("AuctionNo", biddings.Max(b => b.Id) + 1);

        if (commit) await tx.CommitAsync(ct);
        else await tx.RollbackAsync(ct);
        // a dry run must not leave its settings in the cache
        settings.Invalidate();
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        return _r;
    }

    /// <summary>C_Settings (one row, ID = 1): the guarantee, the opening prices, the minimum raise and the default auction length</summary>
    private async Task SettingsAsync(LegacySource src)
    {
        var row = (await src.ReadAsync("C_Settings", _ct)).OrderBy(r => r.Id == 1 ? 0 : 1).ThenBy(r => r.Id).FirstOrDefault();
        if (row == null) return;
        var s = await settings.ForUpdateAsync(_ct);
        var changed = 0;
        void Apply(string field, decimal? value, decimal? current, Action<decimal> set)
        {
            // only a value the old platform actually set
            if (value is not { } raw || raw <= 0) return;
            var v = Math.Round(raw, 2);
            if (current == v) return;
            set(v);
            changed++;
            Add(new SettingsChange
            {
                At = _now, UserName = Sentinel, Field = field,
                OldValue = current?.ToString(CultureInfo.InvariantCulture), NewValue = v.ToString(CultureInfo.InvariantCulture),
            });
        }
        Apply("DepositAmount", row.D("Sett_GuaranteeValue"), s.DepositAmount, v => s.DepositAmount = v);
        Apply("OpeningPrice", row.D("Sett_Fardeyya_BidStartValue"), s.OpeningPrice, v => s.OpeningPrice = v);
        Apply("ManqiyaOpeningPrice", row.D("Sett_Manqeyya_BidStartValue"), s.ManqiyaOpeningPrice, v => s.ManqiyaOpeningPrice = v);
        Apply("MinIncrement", row.D("Sett_Fardeyya_BidMinValue"), s.MinIncrement, v => s.MinIncrement = v);
        if (row.I("Sett_Default_BidPeriod_InDays") is int period && period > 0 && s.AuctionDays != Math.Min(period, 60))
        {
            var days = Math.Min(period, 60);
            Add(new SettingsChange { At = _now, UserName = Sentinel, Field = "AuctionDays", OldValue = s.AuctionDays?.ToString(CultureInfo.InvariantCulture), NewValue = days.ToString(CultureInfo.InvariantCulture) });
            s.AuctionDays = days;
            changed++;
        }
        // no equivalent here: the guarantee for a withdrawn win and the reservation fee
        if (row.D("Sett_GuaranteeValue_Withdrawn") is > 0) _r.Warn("settings.guaranteeWithdrawnIgnored");
        if ((row.D("Sett_Fardeyya_ResFees") ?? row.D("Sett_Manqeyya_ResFees")) is > 0) _r.Warn("settings.resFeesIgnored");
        if (changed == 0) return;
        s.UpdatedAt = _now;
        s.UpdatedBy = Sentinel;
        _pending++;
        _r.Count("settings.fromLegacy", changed);
        await FlushAsync(true);
        settings.Invalidate();
    }

    private enum OutcomeKind { Live, Cancelled, NoWinner, Owned, Registered, Ico, Unpaid }

    private sealed class Outcome
    {
        public Row Row = null!;
        public Guid Id;
        public int Order, Pos;
        public DateTimeOffset Starts, Ends, OwnedAt;
        public bool Cancelled, Ended;
        public (int Member, decimal Value, DateTimeOffset At)? Top;
        public int? Winner, Owner;
        public decimal Price;
        public Row? Bill4, Bill8;
        public OutcomeKind Kind;
    }

    private async Task MembersAsync(LegacySource src, LegacyMap map, Dictionary<(string, int), Guid> links, Dictionary<int, Guid> users, Dictionary<Guid, string> fullNames, HashSet<int> newUsers)
    {
        var existing = await db.Users.AsNoTracking().Select(u => new { u.Id, u.Phone, u.Email, u.NationalId, u.Role, u.FullName }).ToListAsync(_ct);
        var phones = existing.ToDictionary(u => u.Phone, u => (u.Id, u.Role));
        var emails = existing.Select(u => u.Email.ToLowerInvariant()).ToHashSet();
        var nids = existing.Where(u => u.NationalId != null).Select(u => u.NationalId!).ToHashSet();
        foreach (var u in existing) fullNames[u.Id] = u.FullName;

        var created = new HashSet<Guid>();
        await foreach (var m in src.StreamAsync("Members", _ct))
        {
            if (links.TryGetValue((LegacyKinds.Member, m.Id), out var have)) { users[m.Id] = have; continue; }
            string? Part(string c) => Cut(m.S(c), 30);
            var parts = new[] { Part("MEM_FirstName"), Part("MEM_SecondName"), Part("MEM_ThirdName"), Part("MEM_FamilyName") };
            var full = Cut(string.Join(' ', parts.Where(p => p != null)), 120) ?? "";
            var phone = Phones.Normalize(m.S("MEM_Mobile_Key"), m.S("MEM_Mobile")) ?? Phones.FromInput(m.S("MEM_Mobile"));
            if (phone != null && phones.TryGetValue(phone, out var match))
            {
                if (match.Role == Roles.Owner && !created.Contains(match.Id))
                {
                    // the same owner already has an account here (registered on the new platform first): keep one account
                    users[m.Id] = match.Id;
                    Link(LegacyKinds.Member, m.Id, match.Id);
                    _r.Warn("members.mergedWithExistingAccount");
                    continue;
                }
                _r.Warn("members.duplicatePhone (placeholder phone)");
                phone = null;
            }
            if (phone == null) { phone = $"L-{m.Id}"; if (m.S("MEM_Mobile") == null) _r.Warn("members.noPhone"); else _r.Warn("members.invalidPhone"); }

            var email = m.S("MEM_Email")?.ToLowerInvariant();
            if (email == null || !email.Contains('@') || email.Length > 254 || !emails.Add(email))
            {
                if (email != null) _r.Warn("members.duplicateOrInvalidEmail");
                email = $"member{m.Id}@legacy.sammha.invalid";
                emails.Add(email);
            }
            var nid = Cut(m.S("MEM_ID_No"), 20);
            if (nid != null && !nids.Add(nid)) { _r.Warn("members.duplicateNationalId"); nid = null; }

            var u = new User
            {
                FullName = full, FirstName = parts[0], FatherName = parts[1], GrandfatherName = parts[2], FamilyName = parts[3],
                Phone = phone, Email = email, NationalId = nid, Role = Roles.Owner, PasswordHash = Password(m.S("MEM_Password")),
                CreatedAt = At(m.Dt("MEM_Register_Date")) ?? _now, BirthDate = m.Dt("MEM_DOB") is { } dob ? DateOnly.FromDateTime(dob) : null,
                Gender = m.I("MEM_Sex_FK") is { } sx ? map.Sex.GetValueOrDefault(sx) : null,
                Nationality = m.I("MEM_Nationality_FK") is { } na ? map.Nationality.GetValueOrDefault(na) : null,
                ResidenceCountry = m.I("MEM_Country_FK") is { } co ? map.Country.GetValueOrDefault(co) : null,
                ResidenceCity = m.I("MEM_City_FK") is { } ci ? map.City.GetValueOrDefault(ci) : null,
                AltPhone = Phones.Normalize(m.S("MEM_AnotherContactNo_Key"), m.S("MEM_AnotherContactNo")) is { Length: <= 16 } alt ? alt : null,
                AddressCountry = Cut(m.S("PaymentAddressCountry"), 60), AddressCity = Cut(m.S("PaymentAddressCity"), 60),
                District = Cut(m.S("PaymentAddressState"), 60), PostalCode = Cut(m.S("PaymentAddressPostcode"), 10), Street = Cut(m.S("PaymentAddressStreet1"), 120),
                Active = !(m.I("MEM_Status_FK") is { } s && map.AccountSuspended.Contains(s)),
            };
            if (!u.Active) _r.Count("members.suspended");
            phones[phone] = (u.Id, Roles.Owner);
            created.Add(u.Id);
            Add(u);
            users[m.Id] = u.Id;
            fullNames[u.Id] = full;
            newUsers.Add(m.Id);
            Link(LegacyKinds.Member, m.Id, u.Id);
            _r.Count("members");

            var iban = m.S("MEM_BankCardIBANNumber")?.Replace(" ", "").ToUpperInvariant();
            if (iban is { Length: >= 15 and <= 34 })
            {
                Add(new BankAccount { UserId = u.Id, Iban = iban, BankName = Cut(m.S("MEM_BankCardBankName"), 80) ?? "", AccountName = Cut(m.S("MEM_BankCardUserName"), 120) ?? full, Status = BankAccountStatuses.Pending, UpdatedAt = _now });
                _r.Count("bankAccounts (pending verification)");
            }
            await FlushAsync();
        }
        await FlushAsync(true);
    }

    /// <summary>the old platform used ASP.NET Identity: its hashes (v2 and v3) verify here unchanged</summary>
    private string Password(string? old)
    {
        if (old == null) { _r.Warn("members.noPassword (sign in by code, then set one)"); return hasher.Hash(Guid.NewGuid().ToString("N")); }
        try
        {
            var b = Convert.FromBase64String(old);
            if ((b.Length == 49 && b[0] == 0x00) || (b.Length >= 61 && b[0] == 0x01)) return old;
        }
        catch (FormatException) { }
        _r.Warn("members.passwordNotIdentityHash (hashed as typed)");
        return hasher.Hash(old);
    }

    private void Owned(string name, string type, Guid owner, decimal price, DateTimeOffset at, string source, Guid? auctionId, HashSet<string> reserved, Dictionary<Guid, string> fullNames, string? baseName = null)
    {
        var norm = NameMatcher.Normalize(name);
        if (!reserved.Add(norm)) { _r.Warn("names.duplicateSkipped"); return; }
        var id = Guid.NewGuid();
        Add(new ReservedName
        {
            Id = id, Name = Cut(name, 60)!, Normalized = norm, Type = type, OwnerId = owner, OwnerName = fullNames.GetValueOrDefault(owner, ""), Price = price,
            AcquiredAt = at, Source = source, AuctionId = auctionId, BaseName = baseName, CertCode = ReservedName.CertFor(id),
        });
        _r.Count($"names.{source}");
    }

    private void IcoName(string name, string type, Guid? auctionId, Guid? from, string? fromName, decimal? price, DateTimeOffset at, HashSet<string> reserved, decimal? deposit = null)
    {
        var norm = NameMatcher.Normalize(name);
        if (!reserved.Add(norm)) { _r.Warn("names.duplicateSkipped"); return; }
        var id = Guid.NewGuid();
        Add(new ReservedName
        {
            Id = id, Name = Cut(name, 60)!, Normalized = norm, Type = type, OwnerId = null, OwnerName = ReservedSources.IcoOwnerName, Price = 0,
            AcquiredAt = at, Source = ReservedSources.Exception, AuctionId = auctionId, CertCode = ReservedName.CertFor(id),
            ForfeitedFromId = from, ForfeitedFromName = fromName, ForfeitedAt = from != null ? at : null, ForfeitedPrice = price, ForfeitedDeposit = deposit,
        });
        _r.Count("names.ico");
    }

    private Invoice Invoice(Row bill, Guid user, Auction a, string name, string type, string status, HashSet<string> numbers)
    {
        var at = At(bill.Dt("Bill_Datetime")) ?? a.EndsAt;
        var no = status == InvoiceStatuses.Paid ? $"INV-{at.Year}-L{bill.Id}" : $"PR-{at.Year}-L{bill.Id}";
        if (!numbers.Add(no)) no = $"INV-L{bill.Id}-{Guid.NewGuid().ToString("N")[..4]}";
        var total = bill.D("Bill_Total") ?? 0;
        var vat = bill.D("Bill_VAT") ?? 0;
        var inv = new Invoice
        {
            Number = no, UserId = user, AuctionId = a.Id, Name = Cut(name, 150)!, Type = type, Total = total, Vat = vat, Subtotal = bill.D("Bill_Value") ?? total - vat,
            AmountDue = total, Status = status, IssuedAt = at,
            PaidAt = status == InvoiceStatuses.Paid ? At(bill.Dt("Bill_PayDateTime")) ?? at : null,
            CancelledAt = status == InvoiceStatuses.Cancelled ? at : null,
        };
        Add(inv);
        _r.Count($"invoices.{status}");
        return inv;
    }

    private Invoice NewInvoice(Guid user, Auction a, string name, string type, decimal price, HashSet<string> numbers)
    {
        var vat = Math.Round(price * rules.Value.VatRate / (1 + rules.Value.VatRate), 2);
        var no = $"PR-{a.EndsAt.Year}-A{a.Number[3..]}";
        numbers.Add(no);
        var inv = new Invoice { Number = no, UserId = user, AuctionId = a.Id, Name = Cut(name, 150)!, Type = type, Total = price, Vat = vat, Subtotal = price - vat, AmountDue = price, Status = InvoiceStatuses.Unpaid, IssuedAt = a.EndsAt };
        Add(inv);
        _r.Count("invoices.unpaid");
        return inv;
    }

    private async Task BumpAsync(string sequence, int next)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT CAST(current_value AS int) FROM sys.sequences WHERE name = @n";
        var p = cmd.CreateParameter(); p.ParameterName = "@n"; p.Value = sequence; cmd.Parameters.Add(p);
        var cur = Convert.ToInt32(await cmd.ExecuteScalarAsync(_ct));
        if (cur >= next) return;
        cmd.Parameters.Clear();
        cmd.CommandText = $"ALTER SEQUENCE [{sequence}] RESTART WITH {next}";
        await cmd.ExecuteNonQueryAsync(_ct);
        _r.Count($"sequence.{sequence}.restartedAt", next);
    }

    // ───────────────────────── helpers ─────────────────────────

    private void Add(object e) { db.Add(e); _pending++; }

    private void Link(string kind, int legacyId, Guid id) => Add(new LegacyLink { Kind = kind, LegacyId = legacyId, NewId = id, ImportedAt = _now });

    private async Task FlushAsync(bool force = false)
    {
        if (_pending == 0 || (!force && _pending < 2000)) return;
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(_ct);
        db.ChangeTracker.Clear();
        _pending = 0;
    }

    /// <summary>the old database keeps Riyadh wall-clock times</summary>
    private static DateTimeOffset? At(DateTime? d) => d is { } x && x.Year > 1900 ? new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Unspecified), Riyadh).ToUniversalTime() : null;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy/MM/dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy hh:mm:ss tt",
        "dd/MM/yyyy hh:mm tt", "d/M/yyyy h:mm:ss tt", "d/M/yyyy h:mm tt", "d/M/yyyy HH:mm", "M/d/yyyy h:mm:ss tt", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd",
    ];

    /// <summary>notification dates were stored as text</summary>
    public static DateTimeOffset? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = new string(s.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c).ToArray());
        t = t.Replace("ص", "AM").Replace("م", "PM").Replace('،', ' ').Trim();
        if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)) return At(d);
        if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d)) return At(d);
        return null;
    }

    /// <summary>a camel chip number written in free text: the longest run of letters and digits with at least 6 digits</summary>
    public static string? ChipIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var best = System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z0-9]+").Select(m => m.Value)
            .Where(v => v.Count(char.IsAsciiDigit) >= 6 && v.Length <= 40).OrderByDescending(v => v.Length).FirstOrDefault();
        return best?.ToUpperInvariant();
    }

    private static string? Cut(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
}
