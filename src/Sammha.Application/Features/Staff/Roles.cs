using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Features.Staff;

// ───── DTOs ─────
public record PermissionDto(string Key, string Group, string Label, string Description);
public record StaffRoleDto(Guid Id, string Name, string? Description, List<string> Permissions, bool IsSystem, int Members);
public record StaffRoleInput(string Name, string? Description, List<string> Permissions);

public record TicketRoutingDto(bool RestrictToHandlers, Dictionary<string, List<Guid>> Handlers, Dictionary<string, string> Emails, List<HandlerDto> Staff);
public record HandlerDto(Guid Id, string FullName, string Role, string? StaffRoleName, bool CanReply, bool Active);
public record TicketRoutingInput(bool RestrictToHandlers, Dictionary<string, List<Guid>> Handlers, Dictionary<string, string>? Emails = null);

// ───── messages ─────
public record GetPermissionCatalogQuery : IQuery<List<PermissionDto>>;
public record ListStaffRolesQuery : IQuery<List<StaffRoleDto>>;
public record SaveStaffRoleCommand(Guid? Id, StaffRoleInput Input) : ICommand<StaffRoleDto>;
public record DeleteStaffRoleCommand(Guid Id) : ICommand<Unit>;
public record SetUserStaffRoleCommand(Guid UserId, Guid StaffRoleId) : ICommand<UserDto>;
public record GetTicketRoutingQuery : IQuery<TicketRoutingDto>;
public record SaveTicketRoutingCommand(TicketRoutingInput Input) : ICommand<TicketRoutingDto>;

/// <summary>Staff roles (named permission sets), who holds which role, and who handles which kind of ticket.</summary>
public class StaffHandlers(IAppDbContext db, TimeProvider clock, Access access, SettingsStore settings)
    : IRequestHandler<GetPermissionCatalogQuery, List<PermissionDto>>, IRequestHandler<ListStaffRolesQuery, List<StaffRoleDto>>,
      IRequestHandler<SaveStaffRoleCommand, StaffRoleDto>, IRequestHandler<DeleteStaffRoleCommand, Unit>,
      IRequestHandler<SetUserStaffRoleCommand, UserDto>, IRequestHandler<GetTicketRoutingQuery, TicketRoutingDto>,
      IRequestHandler<SaveTicketRoutingCommand, TicketRoutingDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    private void Log(User by, string field, string? oldValue, string? newValue) =>
        db.SettingsChanges.Add(new SettingsChange { At = Now, UserId = by.Id, UserName = by.FullName, Field = field, OldValue = Clip(oldValue), NewValue = Clip(newValue) });
    private static string? Clip(string? s) => s == null ? null : s.Length <= 400 ? s : s[..397] + "…";

    public async Task<List<PermissionDto>> Handle(GetPermissionCatalogQuery q, CancellationToken ct)
    {
        await access.StaffAsync(ct);
        return Permissions.Catalog.Select(p => new PermissionDto(p.Key, p.Group, p.Label, p.Description)).ToList();
    }

    private async Task<List<StaffRoleDto>> RolesAsync(CancellationToken ct)
    {
        var counts = await db.Users.Where(u => u.Role == Roles.Committee).GroupBy(u => u.StaffRoleId ?? Permissions.CommitteeRoleId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return (await db.StaffRoles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ToListAsync(ct))
            .Select(r => new StaffRoleDto(r.Id, r.Name, r.Description, r.PermissionList().ToList(), r.IsSystem, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<List<StaffRoleDto>> Handle(ListStaffRolesQuery q, CancellationToken ct)
    {
        var me = await access.StaffAsync(ct);
        if (!await access.CanAsync(me, Permissions.RolesManage, ct) && !await access.CanAsync(me, Permissions.UsersView, ct))
            throw DomainException.Forbidden("ليست لديك صلاحية «الأدوار والصلاحيات»");
        return await RolesAsync(ct);
    }

    public async Task<StaffRoleDto> Handle(SaveStaffRoleCommand cmd, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.RolesManage, ct);
        var i = cmd.Input;
        var name = (i.Name ?? "").Trim();
        if (name.Length is < 2 or > 60) throw new DomainException("اكتب اسم الدور (2 إلى 60 حرفًا)");
        var perms = (i.Permissions ?? []).Where(Permissions.All.Contains).Distinct().OrderBy(p => Array.IndexOf(Permissions.All, p)).ToList();
        if (perms.Count == 0) throw new DomainException("اختر صلاحية واحدة على الأقل");
        // a role can only hand out permissions the editor has (only admins hold them all)
        var mine = (await access.PermissionsOfAsync(me, ct)).Permissions;
        var beyond = perms.Where(p => !mine.Contains(p)).ToList();
        if (beyond.Count > 0) throw DomainException.Forbidden($"لا يمكنك منح صلاحية لا تملكها: {string.Join("، ", beyond.Select(Permissions.Label))}");
        if (await db.StaffRoles.AnyAsync(r => r.Name == name && r.Id != cmd.Id, ct)) throw new DomainException("يوجد دور بهذا الاسم");

        StaffRole role;
        if (cmd.Id is { } id)
        {
            role = await db.StaffRoles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("الدور غير موجود");
            Log(me, "StaffRole", $"{role.Name}: {string.Join("، ", role.PermissionList().Select(Permissions.Label))}", $"{name}: {string.Join("، ", perms.Select(Permissions.Label))}");
        }
        else
        {
            role = new StaffRole { CreatedAt = Now };
            db.StaffRoles.Add(role);
            Log(me, "StaffRoleCreated", null, $"{name}: {string.Join("، ", perms.Select(Permissions.Label))}");
        }
        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(i.Description) ? null : i.Description.Trim()[..Math.Min(i.Description.Trim().Length, 200)];
        role.Permissions = string.Join(',', perms);
        await db.SaveChangesAsync(ct);
        return (await RolesAsync(ct)).First(r => r.Id == role.Id);
    }

    public async Task<Unit> Handle(DeleteStaffRoleCommand cmd, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.RolesManage, ct);
        var role = await db.StaffRoles.FirstOrDefaultAsync(r => r.Id == cmd.Id, ct) ?? throw DomainException.NotFound("الدور غير موجود");
        if (role.IsSystem) throw new DomainException("الدور الأساسي «لجنة الاعتماد» لا يُحذف، عدّل صلاحياته بدلًا من ذلك");
        var members = await db.Users.CountAsync(u => u.StaffRoleId == role.Id, ct);
        if (members > 0) throw new DomainException($"انقل الموظفين ({members.ToString(CultureInfo.InvariantCulture)}) إلى دور آخر قبل حذف هذا الدور");
        db.StaffRoles.Remove(role);
        Log(me, "StaffRoleDeleted", role.Name, null);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public async Task<UserDto> Handle(SetUserStaffRoleCommand cmd, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.UsersRoles, ct);
        var t = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct) ?? throw DomainException.NotFound("المستخدم غير موجود");
        if (t.Role != Roles.Committee) throw new DomainException("الأدوار للموظفين فقط؛ مدير النظام يملك كل الصلاحيات");
        if (t.Id == me.Id) throw new DomainException("لا يمكنك تعديل صلاحيات حسابك");
        var role = await db.StaffRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == cmd.StaffRoleId, ct) ?? throw DomainException.NotFound("الدور غير موجود");
        var mine = (await access.PermissionsOfAsync(me, ct)).Permissions;
        if (role.PermissionList().Any(p => !mine.Contains(p))) throw DomainException.Forbidden("لا يمكنك إسناد دور فيه صلاحيات لا تملكها");
        var old = t.StaffRoleId;
        t.StaffRoleId = role.Id;
        if (old != role.Id) Log(me, "UserStaffRole", t.FullName, role.Name);
        await db.SaveChangesAsync(ct);
        return Mapper.User(t, role.PermissionList().ToList(), role);
    }

    // ───── ticket routing ─────
    private async Task<TicketRoutingDto> RoutingAsync(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var routes = await db.TicketRoutes.AsNoTracking().ToListAsync(ct);
        var roles = await db.StaffRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);
        var staff = await db.Users.AsNoTracking().Where(u => u.Role == Roles.Committee || u.Role == Roles.Admin).OrderBy(u => u.FullName).ToListAsync(ct);
        return new TicketRoutingDto(s.TicketsRestrictToHandlers,
            TicketKinds.All.ToDictionary(k => k, k => routes.Where(r => r.Kind == k).Select(r => r.UserId).ToList()),
            TicketKinds.All.ToDictionary(k => k, s.EmailsFor),
            staff.Select(u =>
            {
                var role = u.Role == Roles.Committee ? roles.GetValueOrDefault(u.StaffRoleId ?? Permissions.CommitteeRoleId) : null;
                var canReply = u.Role == Roles.Admin || (role?.PermissionList() ?? Permissions.CommitteeDefault).Contains(Permissions.TicketsReply);
                return new HandlerDto(u.Id, u.FullName, u.Role, u.Role == Roles.Admin ? "مدير النظام" : role?.Name, canReply, u.Active);
            }).ToList());
    }

    public async Task<TicketRoutingDto> Handle(GetTicketRoutingQuery q, CancellationToken ct)
    {
        var me = await access.StaffAsync(ct);
        if (!await access.CanAsync(me, Permissions.SettingsManage, ct) && !await access.CanAsync(me, Permissions.TicketsManage, ct))
            throw DomainException.Forbidden("ليست لديك صلاحية «إعدادات المنصة»");
        return await RoutingAsync(ct);
    }

    public async Task<TicketRoutingDto> Handle(SaveTicketRoutingCommand cmd, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.SettingsManage, ct);
        var current = await RoutingAsync(ct);
        var staffIds = current.Staff.Where(x => x.CanReply).Select(x => x.Id).ToHashSet();
        var wanted = new List<TicketRoute>();
        foreach (var (kind, ids) in cmd.Input.Handlers ?? [])
        {
            if (!TicketKinds.All.Contains(kind)) throw new DomainException("نوع غير معروف");
            foreach (var id in (ids ?? []).Distinct())
            {
                if (!staffIds.Contains(id)) throw new DomainException("اختر موظفين لديهم صلاحية «الرد على التذاكر»");
                wanted.Add(new TicketRoute { Kind = kind, UserId = id });
            }
        }
        string Names(Dictionary<string, List<Guid>> h) => string.Join(" | ", TicketKinds.All.Select(k =>
            $"{TicketKinds.Arabic[k]}: {(h.GetValueOrDefault(k) is { Count: > 0 } l ? string.Join("، ", l.Select(id => current.Staff.FirstOrDefault(x => x.Id == id)?.FullName ?? "?")) : "الجميع")}"));
        var before = Names(current.Handlers);
        db.TicketRoutes.RemoveRange(await db.TicketRoutes.ToListAsync(ct));
        db.TicketRoutes.AddRange(wanted);
        var row = await settings.ForUpdateAsync(ct);
        if (row.TicketsRestrictToHandlers != cmd.Input.RestrictToHandlers)
            Log(me, "TicketsRestrictToHandlers", row.TicketsRestrictToHandlers ? "مفعّل" : "متوقف", cmd.Input.RestrictToHandlers ? "مفعّل" : "متوقف");
        row.TicketsRestrictToHandlers = cmd.Input.RestrictToHandlers;
        foreach (var (kind, raw) in cmd.Input.Emails ?? [])
        {
            if (!TicketKinds.All.Contains(kind)) throw new DomainException("نوع غير معروف");
            var list = (raw ?? "").Split([',', '،', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var bad = list.FirstOrDefault(e => !System.Text.RegularExpressions.Regex.IsMatch(e, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"));
            if (bad != null) throw new DomainException($"البريد غير صحيح: {bad}");
            if (list.Count > 10) throw new DomainException("عشرة عناوين بريد على الأقصى لكل نوع");
            var value = string.Join(", ", list);
            if (value.Length > 400) throw new DomainException("عناوين البريد طويلة جدًا");
            var old = row.EmailsFor(kind);
            if (old == value) continue;
            switch (kind)
            {
                case TicketKinds.Complaint: row.ComplaintEmails = value; break;
                case TicketKinds.Report: row.ReportEmails = value; break;
                case TicketKinds.Inquiry: row.InquiryEmails = value; break;
                default: row.SuggestionEmails = value; break;
            }
            Log(me, $"TicketEmails:{kind}", old, value);
        }
        var after = Names(TicketKinds.All.ToDictionary(k => k, k => wanted.Where(w => w.Kind == k).Select(w => w.UserId).ToList()));
        if (after != before) Log(me, "TicketRouting", before, after);
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
        return await RoutingAsync(ct);
    }
}
