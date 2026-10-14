using Microsoft.Extensions.DependencyInjection;
using Sammha.Application.Common;
using Sammha.Domain;
using Sammha.Infrastructure.Data;

namespace Sammha.Infrastructure.Services;

/// <summary>saves activity entries in a separate scope, so they never carry a failed command's pending changes</summary>
public class ActivityWriter(IServiceScopeFactory scopes) : IActivityWriter
{
    public async Task WriteAsync(ActivityLog entry, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SammhaDbContext>();
        if (entry.Error is { Length: > 300 } e) entry.Error = e[..300];
        db.ActivityLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
