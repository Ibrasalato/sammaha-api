using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Domain;

namespace Sammha.Application.Common;

/// <summary>Who is calling, re-checked against the database on every request (a suspended account stops at once).</summary>
public class Access(IAppDbContext db, ICurrentUser current, IOptions<PlatformOptions> rules)
{
    private User? _me;

    public async Task<User> MeAsync(CancellationToken ct)
    {
        if (_me != null) return _me;
        if (current.Id is not { } id) throw DomainException.Unauthorized();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.Unauthorized();
        if (!u.Active) throw DomainException.Unauthorized("هذا الحساب موقوف");
        // password changed or account suspended since this token was issued
        if (current.Stamp != u.SecurityStamp) throw DomainException.Unauthorized("انتهت الجلسة، سجّل الدخول من جديد");
        return _me = u;
    }

    /// <summary>run the rest of this scope as the given user (card-payment webhooks completing an owner's action)</summary>
    public void ActAs(User u) => _me = u;

    public async Task<User?> MaybeAsync(CancellationToken ct) => _me ?? (current.Id is null ? null : await MeAsync(ct));

    public async Task<User> StaffAsync(CancellationToken ct)
    {
        var u = await MeAsync(ct);
        if (!Roles.IsStaff(u.Role)) throw DomainException.Forbidden("هذه الصفحة للجنة الاعتماد فقط");
        return u;
    }

    public async Task<User> AdminAsync(CancellationToken ct)
    {
        var u = await MeAsync(ct);
        if (u.Role != Roles.Admin) throw DomainException.Forbidden("هذه العملية لمدير النظام فقط");
        return u;
    }

    public bool IsSuper(User u) => string.Equals(u.Email, rules.Value.SuperAdminEmail, StringComparison.OrdinalIgnoreCase);
}
