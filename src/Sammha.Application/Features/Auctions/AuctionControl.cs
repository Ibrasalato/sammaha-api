using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Domain;

namespace Sammha.Application.Features.Auctions;

/// <summary>which auctions an action applies to: chosen ones, or every running and upcoming auction (optionally of one type)</summary>
public record AuctionScope(List<Guid>? Ids = null, bool All = false, string? Type = null);
public record SkippedDto(Guid Id, string Name, string Reason);
public record AuctionActionResult(int Affected, List<SkippedDto> Skipped);

public record ListAdminAuctionsQuery(string? State, string? Type) : IQuery<List<AuctionDto>>;
public record PauseAuctionsCommand(AuctionScope Scope, string? Reason) : ICommand<AuctionActionResult>;
public record ResumeAuctionsCommand(AuctionScope Scope) : ICommand<AuctionActionResult>;
public record ExtendAuctionsCommand(AuctionScope Scope, int Minutes) : ICommand<AuctionActionResult>;
public record CancelAuctionsCommand(AuctionScope Scope, string Reason) : ICommand<AuctionActionResult>;

/// <summary>
/// The administration's auction controls. Each auction is changed under its own lock, so a bid or the settlement sweep
/// never races an action; auctions that cannot take the action are skipped and reported.
/// </summary>
public class AuctionControlHandlers(IAppDbContext db, TimeProvider clock, Access access, Mapper map, AuctionEngine engine, Notifier notifier,
    IAuctionRealtime realtime, IPostCommit postCommit)
    : IRequestHandler<ListAdminAuctionsQuery, List<AuctionDto>>, IRequestHandler<PauseAuctionsCommand, AuctionActionResult>,
      IRequestHandler<ResumeAuctionsCommand, AuctionActionResult>, IRequestHandler<ExtendAuctionsCommand, AuctionActionResult>,
      IRequestHandler<CancelAuctionsCommand, AuctionActionResult>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private const int MaxBatch = 500;

    public async Task<List<AuctionDto>> Handle(ListAdminAuctionsQuery q, CancellationToken ct)
    {
        var me = await access.StaffAsync(ct);
        if (!await access.CanAsync(me, Permissions.AuctionsManage, ct) && !await access.CanAsync(me, Permissions.AuctionsCancel, ct) && !await access.CanAsync(me, Permissions.AuctionsSchedule, ct))
            throw DomainException.Forbidden("ليست لديك صلاحية «التحكم في المزادات»");
        var now = Now;
        var query = db.Auctions.AsNoTracking();
        query = q.State switch
        {
            "live" => query.Where(a => !a.Settled && a.PausedAt == null && a.StartsAt <= now && a.EndsAt > now),
            "scheduled" => query.Where(a => !a.Settled && a.PausedAt == null && a.StartsAt > now),
            "paused" => query.Where(a => a.PausedAt != null && a.CancelledAt == null),
            "cancelled" => query.Where(a => a.CancelledAt != null),
            "ended" => query.Where(a => a.CancelledAt == null && (a.Settled || a.EndsAt <= now) && a.PausedAt == null),
            "active" => query.Where(a => !a.Settled && a.CancelledAt == null),
            _ => query,
        };
        if (NameTypes.IsValid(q.Type)) query = query.Where(a => a.Type == q.Type);
        var list = await query.OrderBy(a => a.Settled).ThenBy(a => a.EndsAt).Take(1000).ToListAsync(ct);
        return await map.AuctionsAsync(list, ct);
    }

    private async Task<List<Guid>> ResolveAsync(AuctionScope s, CancellationToken ct)
    {
        if (s.All)
        {
            var q = db.Auctions.Where(a => !a.Settled && a.CancelledAt == null);
            if (NameTypes.IsValid(s.Type)) q = q.Where(a => a.Type == s.Type);
            return await q.OrderBy(a => a.EndsAt).Select(a => a.Id).Take(MaxBatch).ToListAsync(ct);
        }
        var ids = (s.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw new DomainException("اختر مزادًا واحدًا على الأقل");
        if (ids.Count > MaxBatch) throw new DomainException($"{MaxBatch} مزاد على الأكثر في الإجراء الواحد");
        return ids;
    }

    /// <summary>runs the change on each auction under its lock; returns null from the change to apply, or a reason to skip</summary>
    private async Task<AuctionActionResult> ForEachAsync(AuctionScope scope, string permission, string logField, string? logNote,
        Func<Auction, Task<string?>> change, CancellationToken ct)
    {
        var me = await access.RequireAsync(permission, ct);
        var ids = await ResolveAsync(scope, ct);
        var done = new List<string>();
        var skipped = new List<SkippedDto>();
        foreach (var id in ids)
        {
            await using var uow = await db.BeginAsync([$"a:{id}"], ct);
            var a = await db.Auctions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) { skipped.Add(new SkippedDto(id, "", "غير موجود")); continue; }
            var why = await change(a);
            if (why != null) { skipped.Add(new SkippedDto(a.Id, a.Name, why)); continue; }
            await db.SaveChangesAsync(ct);
            postCommit.Enqueue(() => realtime.ChangedAsync(id));
            await uow.CommitAsync(ct);
            done.Add(a.Name);
        }
        if (done.Count > 0)
        {
            var scopeText = scope.All ? (NameTypes.IsValid(scope.Type) ? $"كل مزادات {NameTypes.Arabic.GetValueOrDefault(scope.Type!, scope.Type!)}" : "كل المزادات") : string.Join("، ", done.Take(8)) + (done.Count > 8 ? $" و{done.Count - 8} غيرها" : "");
            db.SettingsChanges.Add(new SettingsChange
            {
                At = Now, UserId = me.Id, UserName = me.FullName, Field = logField,
                OldValue = $"{done.Count.ToString(CultureInfo.InvariantCulture)} مزاد", NewValue = Clip(logNote == null ? scopeText : $"{scopeText} · {logNote}"),
            });
            await db.SaveChangesAsync(ct);
        }
        return new AuctionActionResult(done.Count, skipped);
    }

    private static string Clip(string s) => s.Length <= 400 ? s : s[..397] + "…";
    private static string? Note(string? s, int max = 300) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];

    /// <summary>bidders and watchers of the auction (each once)</summary>
    private async Task NotifyFollowersAsync(Auction a, string title, string body, CancellationToken ct)
    {
        var users = await db.Bids.Where(b => b.AuctionId == a.Id).Select(b => b.UserId)
            .Union(db.Watches.Where(w => w.AuctionId == a.Id).Select(w => w.UserId)).Distinct().Take(2000).ToListAsync(ct);
        foreach (var u in users) await notifier.NotifyAsync(u, NotificationKinds.AuctionUpdate, title, body, $"/auctions/{a.Id}", ct);
    }

    private string? Closed(Auction a) =>
        a.CancelledAt != null ? "ملغى" : a.Settled ? "انتهى وتمت الترسية" : a.PausedAt == null && a.EndsAt <= Now ? "انتهى وجارٍ الترسية" : null;

    public Task<AuctionActionResult> Handle(PauseAuctionsCommand cmd, CancellationToken ct)
    {
        var reason = Note(cmd.Reason);
        return ForEachAsync(cmd.Scope, Permissions.AuctionsManage, "AuctionPause", reason, async a =>
        {
            if (Closed(a) is { } c) return c;
            if (a.PausedAt != null) return "متوقف مسبقًا";
            a.PausedAt = Now;
            a.PauseReason = reason;
            await NotifyFollowersAsync(a, $"توقف مزاد «{a.Name}» مؤقتًا", reason ?? "أوقفت إدارة المنصة المزاد مؤقتًا، ويُضاف وقت التوقف إلى مدته عند الاستئناف.", ct);
            return null;
        }, ct);
    }

    public Task<AuctionActionResult> Handle(ResumeAuctionsCommand cmd, CancellationToken ct) =>
        ForEachAsync(cmd.Scope, Permissions.AuctionsManage, "AuctionResume", null, async a =>
        {
            if (a.CancelledAt != null) return "ملغى";
            if (a.PausedAt is not { } at) return "غير متوقف";
            // the paused time is given back: the end moves by it, and the start too when the auction had not opened yet
            var paused = Now - at;
            if (at < a.StartsAt) a.StartsAt += paused;
            a.EndsAt += paused;
            a.PausedAt = null;
            a.PauseReason = null;
            await NotifyFollowersAsync(a, $"استؤنف مزاد «{a.Name}»", $"ينتهي {a.EndsAt.ToOffset(TimeSpan.FromHours(3)):yyyy/MM/dd HH:mm} بتوقيت الرياض.", ct);
            return null;
        }, ct);

    public Task<AuctionActionResult> Handle(ExtendAuctionsCommand cmd, CancellationToken ct)
    {
        if (cmd.Minutes is < 1 or > 43_200) throw new DomainException("مدة التمديد من دقيقة إلى 30 يومًا");
        var label = cmd.Minutes % 1440 == 0 ? $"{cmd.Minutes / 1440} يوم" : cmd.Minutes % 60 == 0 ? $"{cmd.Minutes / 60} ساعة" : $"{cmd.Minutes} دقيقة";
        return ForEachAsync(cmd.Scope, Permissions.AuctionsManage, "AuctionExtend", $"+{label}", async a =>
        {
            if (Closed(a) is { } c) return c;
            a.EndsAt = a.EndsAt.AddMinutes(cmd.Minutes);
            if (a.PhaseAt(Now) == "live" || a.PausedAt != null)
                await NotifyFollowersAsync(a, $"مُدّد مزاد «{a.Name}»", $"أُضيف {label}، وينتهي {a.EndsAt.ToOffset(TimeSpan.FromHours(3)):yyyy/MM/dd HH:mm} بتوقيت الرياض.", ct);
            return null;
        }, ct);
    }

    public Task<AuctionActionResult> Handle(CancelAuctionsCommand cmd, CancellationToken ct)
    {
        var reason = Note(cmd.Reason) ?? throw new DomainException("اكتب سبب الإلغاء، يصل للمالك والمزايدين");
        return ForEachAsync(cmd.Scope, Permissions.AuctionsCancel, "AuctionCancel", reason, async a =>
        {
            if (Closed(a) is { } c) return c;
            await engine.CancelAsync(a, reason, ct);
            return null;
        }, ct);
    }
}
