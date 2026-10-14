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

    private (Guid User, IReadOnlySet<string> Set, StaffRole? Role)? _perms;

    /// <summary>admins have every permission; committee members have their staff role's (the default committee role when none is set)</summary>
    public async Task<(IReadOnlySet<string> Permissions, StaffRole? Role)> PermissionsOfAsync(User u, CancellationToken ct)
    {
        if (_perms is { } c && c.User == u.Id) return (c.Set, c.Role);
        IReadOnlySet<string> set;
        StaffRole? role = null;
        if (u.Role == Roles.Admin) set = Permissions.All.ToHashSet();
        else if (u.Role == Roles.Committee)
        {
            var id = u.StaffRoleId ?? Permissions.CommitteeRoleId;
            role = await db.StaffRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
                ?? await db.StaffRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == Permissions.CommitteeRoleId, ct);
            set = (role?.PermissionList() ?? Permissions.CommitteeDefault).ToHashSet();
        }
        else set = new HashSet<string>();
        _perms = (u.Id, set, role);
        return (set, role);
    }

    /// <summary>the signed-in staff member, if they hold this permission</summary>
    public async Task<User> RequireAsync(string permission, CancellationToken ct)
    {
        var u = await StaffAsync(ct);
        var (set, _) = await PermissionsOfAsync(u, ct);
        if (!set.Contains(permission)) throw DomainException.Forbidden($"ليست لديك صلاحية «{Permissions.Label(permission)}»، تواصل مع مدير النظام");
        return u;
    }

    public async Task<bool> CanAsync(User u, string permission, CancellationToken ct) => (await PermissionsOfAsync(u, ct)).Permissions.Contains(permission);

    public async Task<UserDto> DtoAsync(User u, CancellationToken ct)
    {
        if (!Roles.IsStaff(u.Role)) return Mapper.User(u);
        var (set, role) = await PermissionsOfAsync(u, ct);
        return Mapper.User(u, set, role);
    }

    public bool IsSuper(User u) => string.Equals(u.Email, rules.Value.SuperAdminEmail, StringComparison.OrdinalIgnoreCase);
}
