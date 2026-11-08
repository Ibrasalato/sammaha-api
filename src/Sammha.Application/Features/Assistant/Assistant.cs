using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Features.Admin;
using Sammha.Domain;

namespace Sammha.Application.Features.Assistant;

/// <summary>
/// The staff WhatsApp assistant (n8n) asks the platform here. Everything is read-only, and each answer is limited
/// to what the staff member's own role may see on the dashboard. Op: identify | summary | name | owner | tickets | auctions | violations.
/// </summary>
public record AssistantAskQuery(string Op, string? Phone, Guid? StaffId, string? Q, string? State) : IQuery<AssistantReply>;

public record AssistantReply(bool Ok, string? Message, object? Data);

public class AssistantHandlers(IAppDbContext db, Access access, TimeProvider clock) : IRequestHandler<AssistantAskQuery, AssistantReply>
{
    private const int Max = 10;
    private static AssistantReply No(string m) => new(false, m, null);
    private static AssistantReply Yes(object data) => new(true, null, data);

    public async Task<AssistantReply> Handle(AssistantAskQuery a, CancellationToken ct)
    {
        if (a.Op == "identify") return await IdentifyAsync(a.Phone, ct);

        var staff = a.StaffId is { } id
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Active && (x.Role == Roles.Committee || x.Role == Roles.Admin), ct)
            : null;
        if (staff == null) return No("غير مصرح");
        var (perms, _) = await access.PermissionsOfAsync(staff, ct);
        bool Can(string p) => perms.Contains(p);
        AssistantReply Deny(string p) => No($"ما عندك صلاحية «{Permissions.Label(p)}»");
        var q = (a.Q ?? "").Trim();
        if (q.Length > 80) q = q[..80];

        switch (a.Op)
        {
            case "summary": return Can(Permissions.Overview) ? await SummaryAsync(Can(Permissions.InvoicesView), ct) : Deny(Permissions.Overview);
            case "name": return Can(Permissions.OwnersView) || Can(Permissions.RequestsView) ? await NameAsync(q, Can(Permissions.OwnersFull), ct) : Deny(Permissions.OwnersView);
            case "owner": return Can(Permissions.OwnersView) ? await OwnerAsync(q, Can(Permissions.OwnersFull), Can(Permissions.InvoicesView), Can(Permissions.ViolationsView), ct) : Deny(Permissions.OwnersView);
            case "tickets": return Can(Permissions.TicketsView) ? await TicketsAsync(q, a.State, ct) : Deny(Permissions.TicketsView);
            case "auctions":
                return Can(Permissions.Overview) || Can(Permissions.AuctionsManage) || Can(Permissions.AuctionsSchedule) ? await AuctionsAsync(q, a.State, ct) : Deny(Permissions.AuctionsManage);
            case "violations": return Can(Permissions.ViolationsView) ? await ViolationsAsync(q, a.State, ct) : Deny(Permissions.ViolationsView);
            default: return No("عملية غير معروفة");
        }
    }

    private async Task<AssistantReply> IdentifyAsync(string? raw, CancellationToken ct)
    {
        // WhatsApp sends digits with the country code: 9665xxxxxxxx → 05xxxxxxxx, others → +{digits}
        var digits = NameMatcher.NormalizePhone(raw);
        if (digits.Length < 8) return No("رقم غير صحيح");
        var phone = digits.StartsWith("05") ? digits : "+" + digits;
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Phone == phone && x.Active && (x.Role == Roles.Committee || x.Role == Roles.Admin), ct);
        if (u == null) return No("هذا الرقم غير مسجل لموظف");
        var (perms, _) = await access.PermissionsOfAsync(u, ct);
        return Yes(new { staffId = u.Id, name = u.FullName, role = u.Role, permissions = perms.OrderBy(p => p).ToList() });
    }

    private async Task<AssistantReply> SummaryAsync(bool money, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var pendingRequests = await db.Requests.CountAsync(r => r.Status == RequestStatuses.UnderReview, ct);
        var openTickets = await db.Tickets.CountAsync(t => t.Status != TicketStatuses.Closed, ct);
        var newTickets = await db.Tickets.CountAsync(t => t.Status == TicketStatuses.New, ct);
        var live = await db.Auctions.CountAsync(x => x.CancelledAt == null && x.StartsAt <= now && x.EndsAt > now, ct);
        var scheduled = await db.Auctions.CountAsync(x => x.CancelledAt == null && x.StartsAt > now, ct);
        var unpaidViolations = await db.Violations.CountAsync(v => v.Status == ViolationStatuses.Unpaid, ct);
        var unpaidInvoices = money ? await db.Invoices.CountAsync(i => i.Status == InvoiceStatuses.Unpaid, ct) : (int?)null;
        var unpaidTotal = money ? await db.Invoices.Where(i => i.Status == InvoiceStatuses.Unpaid).SumAsync(i => (decimal?)i.AmountDue, ct) ?? 0m : (decimal?)null;
        return Yes(new
        {
            requestsUnderReview = pendingRequests, openTickets, newTickets, liveAuctions = live, scheduledAuctions = scheduled,
            unpaidViolations, unpaidInvoices, unpaidInvoicesTotal = unpaidTotal,
        });
    }

    private async Task<AssistantReply> NameAsync(string q, bool full, CancellationToken ct)
    {
        if (q.Length < 2) return No("اكتب الاسم");
        var n = NameMatcher.Normalize(q);
        var found = await db.ReservedNames.AsNoTracking().Where(r => r.Normalized == n || r.Name.Contains(q)).OrderBy(r => r.Name).Take(Max).ToListAsync(ct);
        var ownerIds = found.Where(f => f.OwnerId != null).Select(f => f.OwnerId!.Value).Distinct().ToList();
        var phones = full ? await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Phone, ct) : new Dictionary<Guid, string>();
        var names = found.Select(r => new
        {
            r.Name, type = NameTypes.Arabic.GetValueOrDefault(r.Type, r.Type), owner = r.OwnerName,
            ownerPhone = full && r.OwnerId is { } o ? phones.GetValueOrDefault(o) : null,
            price = r.Price, acquiredAt = r.AcquiredAt, certificate = r.CertCode,
            toOrganization = r.ForfeitedFromId != null,
        }).ToList();
        // a name that is not registered may still be in a request or an auction
        var inAuction = await db.Auctions.AsNoTracking().Where(x => x.Name.Contains(q)).OrderByDescending(x => x.StartsAt).Take(5)
            .Select(x => new { x.Number, x.Name, x.CurrentPrice, x.BidCount, x.StartsAt, x.EndsAt, cancelled = x.CancelledAt != null, settled = x.Settled }).ToListAsync(ct);
        return Yes(new { registered = names, auctions = inAuction });
    }

    private async Task<AssistantReply> OwnerAsync(string q, bool full, bool invoices, bool violations, CancellationToken ct)
    {
        if (q.Length < 3) return No("اكتب الاسم أو الجوال أو الهوية");
        var phone = Phones.FromInput(q.StartsWith('+') || char.IsDigit(q[0]) ? q : null);
        var digits = NameMatcher.NormalizePhone(q);
        var users = db.Users.AsNoTracking().Where(u => u.Role == Roles.Owner);
        var found = await (phone != null ? users.Where(u => u.Phone == phone)
            : digits.Length == 10 && !digits.StartsWith("05") ? users.Where(u => u.NationalId == digits)
            : users.Where(u => u.FullName.Contains(q))).OrderBy(u => u.FullName).Take(5).ToListAsync(ct);
        if (found.Count == 0) return No("ما لقيت مالك بهذه البيانات");
        var ids = found.Select(u => u.Id).ToList();
        var names = await db.ReservedNames.AsNoTracking().Where(r => r.OwnerId != null && ids.Contains(r.OwnerId.Value)).OrderBy(r => r.Name).ToListAsync(ct);
        var unpaid = invoices ? await db.Invoices.AsNoTracking().Where(i => ids.Contains(i.UserId) && i.Status == InvoiceStatuses.Unpaid).ToListAsync(ct) : [];
        var fines = violations ? await db.Violations.AsNoTracking().Where(v => ids.Contains(v.UserId) && v.Status == ViolationStatuses.Unpaid).ToListAsync(ct) : [];
        var owners = found.Select(u => new
        {
            u.FullName, phone = full ? u.Phone : null, nationalId = full ? u.NationalId : OwnersHandlers.Mask(u.NationalId), u.Active, joinedAt = u.CreatedAt,
            names = names.Where(n => n.OwnerId == u.Id).Select(n => new { n.Name, type = NameTypes.Arabic.GetValueOrDefault(n.Type, n.Type) }).ToList(),
            unpaidInvoices = invoices ? unpaid.Where(i => i.UserId == u.Id).Select(i => new { i.Number, i.Name, i.AmountDue, i.DueAt }).ToList() : null,
            unpaidViolations = violations ? fines.Where(v => v.UserId == u.Id).Select(v => new { v.Number, v.TypeName, v.Amount, v.FestivalName }).ToList() : null,
        }).ToList();
        return Yes(new { owners });
    }

    private async Task<AssistantReply> TicketsAsync(string q, string? state, CancellationToken ct)
    {
        var t = db.Tickets.AsNoTracking().AsQueryable();
        if (state is "open") t = t.Where(x => x.Status != TicketStatuses.Closed);
        else if (state != null && TicketStatuses.All.Contains(state)) t = t.Where(x => x.Status == state);
        if (q.Length > 0) t = t.Where(x => x.Number == q || x.Subject.Contains(q) || x.Name.Contains(q));
        var rows = await t.OrderByDescending(x => x.UpdatedAt).Take(Max).Select(x => new
        {
            x.Number, kind = x.Kind, x.Subject, from = x.Name, x.Status, x.Priority, x.Assignee, x.CreatedAt, x.UpdatedAt,
            preview = x.Message.Length > 200 ? x.Message.Substring(0, 200) : x.Message,
        }).ToListAsync(ct);
        return Yes(new { tickets = rows });
    }

    private async Task<AssistantReply> AuctionsAsync(string q, string? state, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var x = db.Auctions.AsNoTracking().AsQueryable();
        x = state switch
        {
            "live" => x.Where(a => a.CancelledAt == null && a.StartsAt <= now && a.EndsAt > now),
            "scheduled" => x.Where(a => a.CancelledAt == null && a.StartsAt > now),
            "ended" => x.Where(a => a.CancelledAt != null || a.EndsAt <= now),
            _ => x,
        };
        if (q.Length > 0) x = x.Where(a => a.Number == q || a.Name.Contains(q));
        var rows = await x.OrderByDescending(a => a.StartsAt).Take(Max).Select(a => new
        {
            a.Number, a.Name, type = a.Type, a.OpeningPrice, a.CurrentPrice, a.BidCount, a.StartsAt, a.EndsAt, paused = a.PausedAt != null,
            cancelled = a.CancelledAt != null, settled = a.Settled, a.FinalPrice,
        }).ToListAsync(ct);
        return Yes(new { auctions = rows });
    }

    private async Task<AssistantReply> ViolationsAsync(string q, string? state, CancellationToken ct)
    {
        var v = db.Violations.AsNoTracking().AsQueryable();
        if (state != null && ViolationStatuses.All.Contains(state)) v = v.Where(x => x.Status == state);
        if (q.Length > 0) v = v.Where(x => x.Number == q || x.TypeName.Contains(q) || x.CamelChip == q);
        var rows = await v.OrderByDescending(x => x.IssuedAt).Take(Max).Join(db.Users, x => x.UserId, u => u.Id, (x, u) => new
        {
            x.Number, owner = u.FullName, type = x.TypeName, x.Amount, x.Status, x.FestivalName, x.CamelChip, x.IssuedAt, x.PaidAt,
        }).ToListAsync(ct);
        return Yes(new { violations = rows });
    }
}
