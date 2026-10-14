using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Features.Admin;

public record ActivityDto(long Id, DateTimeOffset At, Guid? ActorId, string ActorName, string ActorRole, Guid? OnBehalfOfId, string? OnBehalfOfName,
    string Action, string Category, string CategoryLabel, string? Details, bool Success, string? Error, string? Ip, string? Device);
public record ActivityPage(List<ActivityDto> Items, int Total, List<KeyValuePair<string, string>> Categories);

/// <summary>the activity log (logs.view): filters by period, category, person, result and text</summary>
public record ListActivityQuery(DateTimeOffset? From, DateTimeOffset? To, string? Category, string? Role, string? Q, bool? Failed, int Page = 1, int Size = 100) : IQuery<ActivityPage>;

public class ActivityQueryHandler(IAppDbContext db, Access access) : IRequestHandler<ListActivityQuery, ActivityPage>
{
    /// <summary>a short device label from the browser string</summary>
    public static string? Device(string? ua)
    {
        if (string.IsNullOrEmpty(ua)) return null;
        var os = ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? "Android" : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS") ? "Mac" : ua.Contains("Linux") ? "Linux" : "";
        var br = ua.Contains("Edg/") ? "Edge" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Safari/") ? "Safari" : "";
        var s = $"{br} {os}".Trim();
        return s.Length > 0 ? s : ua[..Math.Min(40, ua.Length)];
    }

    public async Task<ActivityPage> Handle(ListActivityQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.LogsView, ct);
        var x = db.ActivityLogs.AsNoTracking();
        if (q.From != null) x = x.Where(a => a.At >= q.From);
        if (q.To != null) x = x.Where(a => a.At <= q.To);
        if (!string.IsNullOrEmpty(q.Category)) x = x.Where(a => a.Category == q.Category);
        if (!string.IsNullOrEmpty(q.Role)) x = q.Role == "staff" ? x.Where(a => a.ActorRole == Roles.Admin || a.ActorRole == Roles.Committee) : x.Where(a => a.ActorRole == q.Role);
        if (q.Failed == true) x = x.Where(a => !a.Success);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var t = q.Q.Trim();
            x = x.Where(a => a.ActorName.Contains(t) || a.Action.Contains(t) || (a.Details != null && a.Details.Contains(t)) || (a.OnBehalfOfName != null && a.OnBehalfOfName.Contains(t)) || (a.Ip != null && a.Ip.Contains(t)));
        }
        var total = await x.CountAsync(ct);
        var size = Math.Clamp(q.Size, 10, 500);
        var page = Math.Max(1, q.Page);
        var rows = await x.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new ActivityPage(rows.Select(a => new ActivityDto(a.Id, a.At, a.ActorId, a.ActorName, a.ActorRole, a.OnBehalfOfId, a.OnBehalfOfName, a.Action, a.Category,
            ActivityCategories.Arabic.GetValueOrDefault(a.Category, a.Category), a.Details, a.Success, a.Error, a.Ip, Device(a.UserAgent))).ToList(),
            total, ActivityCategories.Arabic.ToList());
    }
}
