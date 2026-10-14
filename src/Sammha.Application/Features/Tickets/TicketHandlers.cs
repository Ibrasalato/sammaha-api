using MediatR;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Tickets;

/// <summary>Complaints, reports, enquiries and suggestions (public form, tracking, staff handling, email through n8n).</summary>
public partial class TicketHandlers(IAppDbContext db, TimeProvider clock, Access access, Notifier notifier, IPostCommit postCommit, IWebhookPublisher webhooks,
    SettingsStore settings, IOptions<IntegrationOptions> integrations)
    : IRequestHandler<SubmitTicketCommand, TicketDto>, IRequestHandler<TrackTicketQuery, TicketDto?>, IRequestHandler<FollowUpTicketCommand, TicketDto>, IRequestHandler<GetMyTicketsQuery, List<TicketDto>>, IRequestHandler<ListTicketsQuery, List<TicketDto>>, IRequestHandler<GetTicketQuery, TicketDto>, IRequestHandler<UpdateTicketCommand, TicketDto>, IRequestHandler<ReplyTicketCommand, TicketDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    [GeneratedRegex(@"^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$")] private static partial Regex EmailRx();

    private static string Clip(string? s, int max) { var t = (s ?? "").Trim(); return t.Length > max ? t[..max] : t; }

    private async Task<TicketDto> SubmitAsync(TicketInput i, CancellationToken ct)
    {
        var t = new Ticket
        {
            Kind = i.Kind, Topic = Clip(i.Topic, 60), Subject = Clip(i.Subject, 120), Name = Clip(i.Name, 80), Phone = NameMatcher.NormalizePhone(i.Phone),
            Email = string.IsNullOrWhiteSpace(i.Email) ? null : Clip(i.Email, 120), RefNumber = string.IsNullOrWhiteSpace(i.RefNumber) ? null : Clip(i.RefNumber, 40),
            Message = Clip(i.Message, 3000), CreatedAt = Now, UpdatedAt = Now,
        };
        if (!TicketKinds.All.Contains(t.Kind)) throw new DomainException("اختر نوع الطلب");
        if (t.Name.Length < 2) throw new DomainException("اكتب الاسم");
        if (!NameMatcher.IsSaudiMobile(t.Phone)) throw new DomainException("اكتب رقم جوال سعودي صحيح يبدأ بـ 05");
        if (t.Email != null && !EmailRx().IsMatch(t.Email)) throw new DomainException("البريد الإلكتروني غير صحيح");
        if (t.Subject.Length < 3) throw new DomainException("اكتب عنوانًا مختصرًا");
        if (t.Message.Length < 10) throw new DomainException("اكتب التفاصيل (10 أحرف على الأقل)");
        t.UserId = (await access.MaybeAsync(ct))?.Id;
        t.Priority = t.Kind == TicketKinds.Report ? TicketPriorities.Urgent : TicketPriorities.Normal;
        var seq = await db.NextNumberAsync("TicketNo", ct);
        t.Number = $"BL-{Now.Year % 100:D2}{seq:D4}";
        db.Tickets.Add(t);
        await RouteAsync(t, ct);
        await db.SaveChangesAsync(ct);
        await EmailAsync("created", t);
        return Mapper.Ticket(t, false);
    }

    /// <summary>
    /// Settings → complaints and enquiries: the ticket goes to whichever handler of its kind has the fewest open tickets,
    /// and every handler of that kind is notified. With no handlers set, it stays unassigned for the whole team.
    /// </summary>
    private async Task RouteAsync(Ticket t, CancellationToken ct)
    {
        var handlers = await db.TicketRoutes.Where(r => r.Kind == t.Kind).Join(db.Users.Where(u => u.Active), r => r.UserId, u => u.Id, (r, u) => u).ToListAsync(ct);
        if (handlers.Count == 0) return;
        var ids = handlers.Select(h => h.Id).ToList();
        var open = await db.Tickets.Where(x => x.AssigneeId != null && ids.Contains(x.AssigneeId.Value) && x.Status != TicketStatuses.Closed)
            .GroupBy(x => x.AssigneeId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var pick = handlers.OrderBy(h => open.GetValueOrDefault(h.Id)).ThenBy(h => h.FullName).First();
        t.AssigneeId = pick.Id;
        t.Assignee = pick.FullName;
        var kind = TicketKinds.Arabic.GetValueOrDefault(t.Kind, t.Kind);
        foreach (var h in handlers)
            await notifier.NotifyAsync(h.Id, NotificationKinds.Ticket, h.Id == pick.Id ? $"{kind} جديد مُسند إليك: {t.Number}" : $"{kind} جديد: {t.Number}",
                $"{t.Subject} · من {t.Name}", $"/admin/tickets/{t.Id}", ct);
    }

    /// <summary>with «الرد يقتصر على المسؤولين» on, only that kind's handlers, the assignee and admins may act on a ticket</summary>
    private async Task EnsureHandlerAsync(User u, Ticket t, CancellationToken ct)
    {
        if (u.Role == Roles.Admin || t.AssigneeId == u.Id) return;
        if (!(await settings.GetAsync(ct)).TicketsRestrictToHandlers) return;
        var routed = await db.TicketRoutes.Where(r => r.Kind == t.Kind).Select(r => r.UserId).ToListAsync(ct);
        if (routed.Count > 0 && !routed.Contains(u.Id))
            throw DomainException.Forbidden($"هذا النوع ({TicketKinds.Arabic.GetValueOrDefault(t.Kind, t.Kind)}) يعالجه الموظفون المسؤولون عنه فقط");
    }

    private Task<Ticket?> FindAsync(string number, string phone, CancellationToken ct)
    {
        var n = (number ?? "").Trim().ToUpperInvariant();
        var p = NameMatcher.NormalizePhone(phone);
        return db.Tickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Number == n && t.Phone == p, ct);
    }

    private async Task<TicketDto?> TrackAsync(string number, string phone, CancellationToken ct) =>
        await FindAsync(number, phone, ct) is { } t ? Mapper.Ticket(t, false) : null;

    private async Task<TicketDto> FollowUpAsync(string number, string phone, string body, CancellationToken ct)
    {
        var t = await FindAsync(number, phone, ct) ?? throw DomainException.NotFound("لم نجد طلبًا بهذا الرقم والجوال");
        if (t.Status == TicketStatuses.Closed) throw new DomainException("هذا الطلب مغلق، افتح طلبًا جديدًا");
        var text = Clip(body, 3000);
        if (text.Length < 2) throw new DomainException("اكتب ردك");
        db.TicketMessages.Add(new TicketMessage { TicketId = t.Id, By = "customer", Author = t.Name, Body = text, CreatedAt = Now });
        if (t.Status == TicketStatuses.Answered) t.Status = TicketStatuses.InProgress;
        t.UpdatedAt = Now;
        // the assignee hears about the customer's follow-up at once
        if (t.AssigneeId is { } aid)
            await notifier.NotifyAsync(aid, NotificationKinds.Ticket, $"رد جديد من العميل على {t.Number}", text.Length > 120 ? text[..120] : text, $"/admin/tickets/{t.Id}", ct);
        await db.SaveChangesAsync(ct);
        return Mapper.Ticket(await db.Tickets.Include(x => x.Messages).FirstAsync(x => x.Id == t.Id, ct), false);
    }

    private async Task<List<TicketDto>> MineAsync(CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var list = await db.Tickets.AsNoTracking().Include(t => t.Messages).Where(t => t.UserId == u.Id || t.Phone == u.Phone).OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return list.Select(t => Mapper.Ticket(t, false)).ToList();
    }

    private async Task<List<TicketDto>> ListAsync(CancellationToken ct)
    {
        await access.RequireAsync(Permissions.TicketsView, ct);
        var list = await db.Tickets.AsNoTracking().Include(t => t.Messages).OrderByDescending(t => t.CreatedAt).Take(1000).ToListAsync(ct);
        return list.Select(t => Mapper.Ticket(t, true)).ToList();
    }

    private async Task<TicketDto> GetAsync(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.TicketsView, ct);
        var t = await db.Tickets.AsNoTracking().Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        return Mapper.Ticket(t, true);
    }

    private async Task<TicketDto> UpdateAsync(Guid id, string? status, string? priority, string? assignee, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.TicketsManage, ct);
        var t = await db.Tickets.Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        await EnsureHandlerAsync(me, t, ct);
        var changed = status != null && status != t.Status;
        if (status != null) { if (!TicketStatuses.All.Contains(status)) throw new DomainException("حالة غير معروفة"); t.Status = status; }
        if (priority != null) { if (!TicketPriorities.All.Contains(priority)) throw new DomainException("أولوية غير معروفة"); t.Priority = priority; }
        if (assignee != null)
        {
            t.Assignee = Clip(assignee, 80);
            // keep the id in step when the name is a staff member's (routing balances on it)
            var who = t.Assignee.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.FullName == t.Assignee && (u.Role == Roles.Committee || u.Role == Roles.Admin), ct);
            t.AssigneeId = who?.Id;
        }
        t.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (changed && t.Status is TicketStatuses.Closed or TicketStatuses.InProgress) await EmailAsync("status", t);
        return Mapper.Ticket(t, true);
    }

    private async Task<TicketDto> ReplyAsync(Guid id, string body, bool isInternal, CancellationToken ct)
    {
        var u = await access.RequireAsync(Permissions.TicketsReply, ct);
        var t = await db.Tickets.Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        await EnsureHandlerAsync(u, t, ct);
        var text = Clip(body, 3000);
        if (text.Length < 2) throw new DomainException("اكتب الرد");
        db.TicketMessages.Add(new TicketMessage { TicketId = t.Id, By = "staff", Author = u.FullName, Body = text, Internal = isInternal, CreatedAt = Now });
        if (!isInternal)
        {
            t.Status = TicketStatuses.Answered;
            if (t.UserId is { } uid) await notifier.NotifyAsync(uid, NotificationKinds.Ticket, $"رد فريق سمّها على طلبك {t.Number}", text.Length > 120 ? text[..120] : text, "/dashboard/tickets", ct);
        }
        else if (t.Status == TicketStatuses.New) t.Status = TicketStatuses.InProgress;
        if (t.Assignee == null) { t.Assignee = u.FullName; t.AssigneeId = u.Id; }
        t.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (!isInternal) await EmailAsync("reply", t, text);
        return Mapper.Ticket(await db.Tickets.AsNoTracking().Include(x => x.Messages).FirstAsync(x => x.Id == t.Id, ct), true);
    }

    /// <summary>the payload n8n «Sammha 6 — Tickets Email» expects</summary>
    private async Task EmailAsync(string evt, Ticket t, string? reply = null)
    {
        var url = integrations.Value.TicketWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        // Settings → complaints and enquiries: each kind has its own team inbox(es)
        var teamTo = (await settings.GetAsync()).EmailsFor(t.Kind).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var site = integrations.Value.SiteUrl.TrimEnd('/');
        var body = new
        {
            @event = evt,
            ticket = new
            {
                number = t.Number, kind = t.Kind, kindLabel = TicketKinds.Arabic.GetValueOrDefault(t.Kind, t.Kind), topic = t.Topic, subject = t.Subject,
                name = t.Name, phone = t.Phone, email = t.Email ?? "", message = t.Message, refNumber = t.RefNumber ?? "",
                status = t.Status, statusLabel = TicketStatuses.Arabic.GetValueOrDefault(t.Status, t.Status), trackUrl = $"{site}/#/contact/track/{t.Number}",
            },
            reply = reply ?? "",
            teamTo,
            assignee = t.Assignee ?? "",
        };
        postCommit.Enqueue(() => webhooks.PublishAsync(url, body));
    }

    // ───── MediatR entry points ─────
    public Task<TicketDto> Handle(SubmitTicketCommand q, CancellationToken ct) => SubmitAsync(q.Input, ct);
    public Task<TicketDto?> Handle(TrackTicketQuery q, CancellationToken ct) => TrackAsync(q.Number, q.Phone, ct);
    public Task<TicketDto> Handle(FollowUpTicketCommand q, CancellationToken ct) => FollowUpAsync(q.Number, q.Phone, q.Body, ct);
    public Task<List<TicketDto>> Handle(GetMyTicketsQuery q, CancellationToken ct) => MineAsync(ct);
    public Task<List<TicketDto>> Handle(ListTicketsQuery q, CancellationToken ct) => ListAsync(ct);
    public Task<TicketDto> Handle(GetTicketQuery q, CancellationToken ct) => GetAsync(q.Id, ct);
    public Task<TicketDto> Handle(UpdateTicketCommand q, CancellationToken ct) => UpdateAsync(q.Id, q.Status, q.Priority, q.Assignee, ct);
    public Task<TicketDto> Handle(ReplyTicketCommand q, CancellationToken ct) => ReplyAsync(q.Id, q.Body, q.Internal, ct);
}
