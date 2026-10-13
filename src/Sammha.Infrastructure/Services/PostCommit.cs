using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sammha.Application.Common;

namespace Sammha.Infrastructure.Services;

/// <summary>
/// Buffers side effects (webhooks, realtime pushes) while a transaction is open and runs them after commit;
/// outside a transaction they run at once. A rolled-back transaction discards them.
/// </summary>
public class PostCommit(ILogger<PostCommit> log) : IPostCommit
{
    private readonly List<Func<Task>> _pending = [];
    internal Func<bool>? InTransaction { get; set; }

    public void Enqueue(Func<Task> action)
    {
        if (InTransaction?.Invoke() == true) { _pending.Add(action); return; }
        _ = Run(action);
    }

    public async Task FlushAsync()
    {
        var list = _pending.ToList();
        _pending.Clear();
        foreach (var a in list) await Run(a);
    }

    public void Discard() => _pending.Clear();

    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { log.LogWarning(e, "Post-commit action failed"); }
    }
}
