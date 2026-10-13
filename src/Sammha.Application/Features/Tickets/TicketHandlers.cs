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
    IOptions<IntegrationOptions> integrations)
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
        await db.SaveChangesAsync(ct);
        Email("created", t);
        return Mapper.Ticket(t, false);
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
        await access.StaffAsync(ct);
        var list = await db.Tickets.AsNoTracking().Include(t => t.Messages).OrderByDescending(t => t.CreatedAt).Take(1000).ToListAsync(ct);
        return list.Select(t => Mapper.Ticket(t, true)).ToList();
    }

    private async Task<TicketDto> GetAsync(Guid id, CancellationToken ct)
    {
        await access.StaffAsync(ct);
        var t = await db.Tickets.AsNoTracking().Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        return Mapper.Ticket(t, true);
    }

    private async Task<TicketDto> UpdateAsync(Guid id, string? status, string? priority, string? assignee, CancellationToken ct)
    {
        await access.StaffAsync(ct);
        var t = await db.Tickets.Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        var changed = status != null && status != t.Status;
        if (status != null) { if (!TicketStatuses.All.Contains(status)) throw new DomainException("حالة غير معروفة"); t.Status = status; }
        if (priority != null) { if (!TicketPriorities.All.Contains(priority)) throw new DomainException("أولوية غير معروفة"); t.Priority = priority; }
        if (assignee != null) t.Assignee = Clip(assignee, 80);
        t.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (changed && t.Status is TicketStatuses.Closed or TicketStatuses.InProgress) Email("status", t);
        return Mapper.Ticket(t, true);
    }

    private async Task<TicketDto> ReplyAsync(Guid id, string body, bool isInternal, CancellationToken ct)
    {
        var u = await access.StaffAsync(ct);
        var t = await db.Tickets.Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        var text = Clip(body, 3000);
        if (text.Length < 2) throw new DomainException("اكتب الرد");
        db.TicketMessages.Add(new TicketMessage { TicketId = t.Id, By = "staff", Author = u.FullName, Body = text, Internal = isInternal, CreatedAt = Now });
        if (!isInternal)
        {
            t.Status = TicketStatuses.Answered;
            if (t.UserId is { } uid) await notifier.NotifyAsync(uid, NotificationKinds.Ticket, $"رد فريق سمّها على طلبك {t.Number}", text.Length > 120 ? text[..120] : text, "/dashboard/tickets", ct);
        }
        else if (t.Status == TicketStatuses.New) t.Status = TicketStatuses.InProgress;
        t.Assignee ??= u.FullName;
        t.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (!isInternal) Email("reply", t, text);
        return Mapper.Ticket(await db.Tickets.AsNoTracking().Include(x => x.Messages).FirstAsync(x => x.Id == t.Id, ct), true);
    }

    /// <summary>the payload n8n «Sammha 6 — Tickets Email» expects</summary>
    private void Email(string evt, Ticket t, string? reply = null)
    {
        var url = integrations.Value.TicketWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
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
