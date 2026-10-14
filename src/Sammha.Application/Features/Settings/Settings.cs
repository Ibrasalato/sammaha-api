using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sammha.Application.Common;
using Sammha.Domain;

namespace Sammha.Application.Features.Settings;

// ───── DTOs ─────

/// <summary>What every visitor needs: which services accept requests, the site banner, contact details, the password rule.</summary>
public record PublicSettingsDto(
    bool RequestsOpen, string? RequestsClosedNote, bool CompoundOpen, string? CompoundClosedNote,
    bool ResaleAuctionOpen, bool InstantSaleOpen, string? SaleClosedNote, bool InvestmentOpen, string? InvestmentClosedNote,
    bool SignupOpen, string SupportEmail, string SupportPhone, string WhatsApp,
    string? Announcement, string AnnouncementTone, bool Maintenance, string? MaintenanceNote,
    int PasswordMinLength, int StaffIdleMinutes, decimal DepositAmount = 0, int PaymentDeadlineHours = 72, decimal TransferFee = 0,
    bool TransferOpen = true, string? TransferClosedNote = null, List<string>? ComingSoon = null,
    decimal InvestFee = 0, decimal CompoundFee = 0, decimal ResaleCommission = 0, decimal InstantCommission = 0, Dictionary<string, ServiceText>? ServiceTexts = null);

public record AdminSettingsDto(
    bool RequestsOpen, string? RequestsClosedNote, bool CompoundOpen, string? CompoundClosedNote,
    bool ResaleAuctionOpen, bool InstantSaleOpen, string? SaleClosedNote, bool InvestmentOpen, string? InvestmentClosedNote,
    bool SignupOpen, string SupportEmail, string SupportPhone, string WhatsApp,
    bool AnnouncementOn, string? Announcement, string AnnouncementTone, bool Maintenance, string? MaintenanceNote,
    int PasswordMinLength, int MaxLoginAttempts, int LockoutMinutes, int StaffIdleMinutes,
    DateTimeOffset UpdatedAt, string? UpdatedBy, RulesDto Rules, int PaymentDeadlineHours = 72, decimal TransferFee = 0,
    bool TransferOpen = true, string? TransferClosedNote = null, List<string>? ComingSoon = null,
    decimal InvestFee = 0, decimal CompoundFee = 0, decimal ResaleCommission = 0, decimal InstantCommission = 0, Dictionary<string, ServiceText>? ServiceTexts = null);

/// <summary>fees and rules from the server configuration (read-only in the dashboard)</summary>
public record RulesDto(decimal DepositAmount, int NamesPerRequest, decimal OpeningPrice, decimal MinIncrement, int ExtendMinutes, decimal VatRate,
    decimal InvestFee, decimal InvestOwnerShare, int InvestMaxNames, int ResaleNoticeHours, int ResaleAuctionDays, decimal ResaleCommission,
    decimal ResaleBidDeposit, int InstantSaleHours, decimal InstantCommission);

/// <summary>a partial update: only the fields that are sent change</summary>
public record SettingsPatch(
    bool? RequestsOpen = null, string? RequestsClosedNote = null, bool? CompoundOpen = null, string? CompoundClosedNote = null,
    bool? ResaleAuctionOpen = null, bool? InstantSaleOpen = null, string? SaleClosedNote = null, bool? InvestmentOpen = null, string? InvestmentClosedNote = null,
    bool? SignupOpen = null, string? SupportEmail = null, string? SupportPhone = null, string? WhatsApp = null,
    bool? AnnouncementOn = null, string? Announcement = null, string? AnnouncementTone = null, bool? Maintenance = null, string? MaintenanceNote = null,
    int? PasswordMinLength = null, int? MaxLoginAttempts = null, int? LockoutMinutes = null, int? StaffIdleMinutes = null, decimal? DepositAmount = null, int? PaymentDeadlineHours = null, decimal? TransferFee = null,
    bool? TransferOpen = null, string? TransferClosedNote = null, List<string>? ComingSoon = null,
    decimal? InvestFee = null, decimal? CompoundFee = null, decimal? ResaleCommission = null, decimal? InstantCommission = null, Dictionary<string, ServiceText>? ServiceTexts = null);

public record SettingsChangeDto(long Id, DateTimeOffset At, string UserName, string Field, string? OldValue, string? NewValue);

public record PreferencesDto(bool NotifyOutbid, bool NotifyReminders, bool NotifyResults, bool NotifyRequests);

/// <summary>one signed-in device (a refresh-token family)</summary>
public record SessionDto(Guid Id, DateTimeOffset SignedInAt, DateTimeOffset LastActiveAt, DateTimeOffset ExpiresAt, string? Ip, string? Device, bool Current);

public record SecuritySummaryDto(DateTimeOffset? LastLoginAt, int ActiveSessions, int PasswordMinLength);

// ───── messages ─────
public record GetPublicSettingsQuery : IQuery<PublicSettingsDto>;
public record GetAdminSettingsQuery : IQuery<AdminSettingsDto>;
public record UpdateSettingsCommand(SettingsPatch Patch) : ICommand<AdminSettingsDto>;
public record GetSettingsHistoryQuery(int Take = 100) : IQuery<List<SettingsChangeDto>>;
/// <summary>emergency: ends every session on the platform except the caller's</summary>
public record SignOutEveryoneCommand : ICommand<int>;

public record GetMyPreferencesQuery : IQuery<PreferencesDto>;
public record UpdateMyPreferencesCommand(PreferencesDto Preferences) : ICommand<PreferencesDto>;
public record GetMySessionsQuery : IQuery<List<SessionDto>>;
public record RevokeMySessionCommand(Guid SessionId) : ICommand<List<SessionDto>>;
public record GetMySecurityQuery : IQuery<SecuritySummaryDto>;

// ───── handlers ─────
public class SettingsHandlers(IAppDbContext db, TimeProvider clock, Access access, SettingsStore store, ICurrentUser current,
    IOptions<PlatformOptions> rules)
    : IRequestHandler<GetPublicSettingsQuery, PublicSettingsDto>, IRequestHandler<GetAdminSettingsQuery, AdminSettingsDto>,
      IRequestHandler<UpdateSettingsCommand, AdminSettingsDto>, IRequestHandler<GetSettingsHistoryQuery, List<SettingsChangeDto>>,
      IRequestHandler<SignOutEveryoneCommand, int>, IRequestHandler<GetMyPreferencesQuery, PreferencesDto>,
      IRequestHandler<UpdateMyPreferencesCommand, PreferencesDto>, IRequestHandler<GetMySessionsQuery, List<SessionDto>>,
      IRequestHandler<RevokeMySessionCommand, List<SessionDto>>, IRequestHandler<GetMySecurityQuery, SecuritySummaryDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<PublicSettingsDto> Handle(GetPublicSettingsQuery q, CancellationToken ct)
    {
        var s = await store.GetAsync(ct);
        return new PublicSettingsDto(s.RequestsOpen, s.RequestsClosedNote, s.CompoundOpen, s.CompoundClosedNote, s.ResaleAuctionOpen, s.InstantSaleOpen,
            s.SaleClosedNote, s.InvestmentOpen, s.InvestmentClosedNote, s.SignupOpen, s.SupportEmail, s.SupportPhone, s.WhatsApp,
            s.AnnouncementOn ? s.Announcement : null, s.AnnouncementTone, s.Maintenance, s.MaintenanceNote, s.PasswordMinLength, s.StaffIdleMinutes,
            s.DepositAmount ?? rules.Value.DepositAmount, s.PaymentDeadlineHours ?? 72, s.TransferFee ?? 0, s.TransferOpen, s.TransferClosedNote, ServiceKeys.Parse(s.ComingSoon),
            s.InvestFee ?? rules.Value.InvestFee, s.CompoundFee ?? 0, s.ResaleCommission ?? rules.Value.ResaleCommission, s.InstantCommission ?? rules.Value.InstantCommission, ServiceTexts.Parse(s.ServiceTextsJson));
    }

    private AdminSettingsDto Admin(PlatformSettings s)
    {
        var r = rules.Value;
        return new AdminSettingsDto(s.RequestsOpen, s.RequestsClosedNote, s.CompoundOpen, s.CompoundClosedNote, s.ResaleAuctionOpen, s.InstantSaleOpen,
            s.SaleClosedNote, s.InvestmentOpen, s.InvestmentClosedNote, s.SignupOpen, s.SupportEmail, s.SupportPhone, s.WhatsApp,
            s.AnnouncementOn, s.Announcement, s.AnnouncementTone, s.Maintenance, s.MaintenanceNote,
            s.PasswordMinLength, s.MaxLoginAttempts, s.LockoutMinutes, s.StaffIdleMinutes, s.UpdatedAt, s.UpdatedBy,
            new RulesDto(s.DepositAmount ?? r.DepositAmount, r.NamesPerRequest, r.OpeningPrice, r.MinIncrement, r.ExtendMinutes, r.VatRate, r.InvestFee, r.InvestOwnerShare,
                r.InvestMaxNames, r.ResaleNoticeHours, r.ResaleAuctionDays, r.ResaleCommission, r.ResaleBidDeposit, r.InstantSaleHours, r.InstantCommission), s.PaymentDeadlineHours ?? 72, s.TransferFee ?? 0, s.TransferOpen, s.TransferClosedNote, ServiceKeys.Parse(s.ComingSoon),
            s.InvestFee ?? r.InvestFee, s.CompoundFee ?? 0, s.ResaleCommission ?? r.ResaleCommission, s.InstantCommission ?? r.InstantCommission, ServiceTexts.Parse(s.ServiceTextsJson));
    }

    public async Task<AdminSettingsDto> Handle(GetAdminSettingsQuery q, CancellationToken ct)
    {
        var me = await access.StaffAsync(ct);
        if (!await access.CanAsync(me, Permissions.SettingsManage, ct) && !await access.CanAsync(me, Permissions.SecurityManage, ct))
            throw DomainException.Forbidden("ليست لديك صلاحية «إعدادات المنصة»");
        return Admin(await store.GetAsync(ct));
    }

    public async Task<AdminSettingsDto> Handle(UpdateSettingsCommand cmd, CancellationToken ct)
    {
        var me = await access.StaffAsync(ct);
        var p = cmd.Patch;
        // operational switches need «إعدادات المنصة»; the password and sign-in policy needs «سياسة الأمان»
        var touchesSecurity = p.PasswordMinLength != null || p.MaxLoginAttempts != null || p.LockoutMinutes != null || p.StaffIdleMinutes != null;
        var touchesGeneral = p with { PasswordMinLength = null, MaxLoginAttempts = null, LockoutMinutes = null, StaffIdleMinutes = null } != new SettingsPatch();
        if (touchesSecurity) await access.RequireAsync(Permissions.SecurityManage, ct);
        if (touchesGeneral) await access.RequireAsync(Permissions.SettingsManage, ct);
        var s = await store.ForUpdateAsync(ct);
        var changes = new List<SettingsChange>();

        void Add(string field, string? old, string? value) =>
            changes.Add(new SettingsChange { At = Now, UserId = me.Id, UserName = me.FullName, Field = field, OldValue = old, NewValue = value });
        void SetV<T>(string field, T? value, Func<T> get, Action<T> set) where T : struct
        {
            if (value is not { } v || EqualityComparer<T>.Default.Equals(get(), v)) return;
            Add(field, Show(get()), Show(v));
            set(v);
        }
        void SetS(string field, string? value, Func<string> get, Action<string> set)
        {
            if (value is null || get() == value) return;
            Add(field, get(), value);
            set(value);
        }
        static string? Note(string? v, int max = 300) => v == null ? null : v.Trim().Length == 0 ? "" : v.Trim()[..Math.Min(v.Trim().Length, max)];
        static int Range(int? v, int min, int max, string label) => v is { } x && (x < min || x > max) ? throw new DomainException($"{label} بين {min} و{max}") : v ?? 0;

        if (p.SupportEmail is { Length: > 0 } em && !System.Text.RegularExpressions.Regex.IsMatch(em.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new DomainException("بريد الدعم غير صحيح");
        if (p.AnnouncementTone != null && p.AnnouncementTone is not ("info" or "warn" or "ok")) throw new DomainException("لون الإعلان غير معروف");
        if (p.PasswordMinLength != null) Range(p.PasswordMinLength, 8, 32, "طول كلمة المرور");
        if (p.MaxLoginAttempts != null) Range(p.MaxLoginAttempts, 3, 20, "محاولات الدخول");
        if (p.LockoutMinutes != null) Range(p.LockoutMinutes, 1, 1440, "مدة القفل بالدقائق");
        if (p.StaffIdleMinutes != null) Range(p.StaffIdleMinutes, 0, 480, "مهلة الخمول بالدقائق");
        if (p.PaymentDeadlineHours != null) Range(p.PaymentDeadlineHours, 1, 720, "مهلة سداد قيمة الشعار بالساعات");
        if (p.DepositAmount is { } dep && (dep < 0 || dep > 1_000_000)) throw new DomainException("مبلغ الضمان بين 0 و1,000,000 ريال");
        if (p.TransferFee is { } tf && (tf < 0 || tf > 100_000)) throw new DomainException("رسوم نقل الملكية بين 0 و100,000 ريال");
        if (p.InvestFee is { } ivf && (ivf < 0 || ivf > 100_000)) throw new DomainException("رسوم دراسة الاستثمار بين 0 و100,000 ريال");
        if (p.CompoundFee is { } cf && (cf < 0 || cf > 100_000)) throw new DomainException("رسوم الاسم المركب بين 0 و100,000 ريال");
        if (p.ResaleCommission is { } rc && (rc < 0 || rc > 0.5m)) throw new DomainException("عمولة مزاد إعادة البيع بين 0% و50%");
        if (p.InstantCommission is { } ic && (ic < 0 || ic > 0.5m)) throw new DomainException("عمولة البيع الفوري بين 0% و50%");

        SetV("RequestsOpen", p.RequestsOpen, () => s.RequestsOpen, v => s.RequestsOpen = v);
        SetS("RequestsClosedNote", Note(p.RequestsClosedNote), () => s.RequestsClosedNote ?? "", v => s.RequestsClosedNote = v);
        SetV("CompoundOpen", p.CompoundOpen, () => s.CompoundOpen, v => s.CompoundOpen = v);
        SetS("CompoundClosedNote", Note(p.CompoundClosedNote), () => s.CompoundClosedNote ?? "", v => s.CompoundClosedNote = v);
        SetV("ResaleAuctionOpen", p.ResaleAuctionOpen, () => s.ResaleAuctionOpen, v => s.ResaleAuctionOpen = v);
        SetV("InstantSaleOpen", p.InstantSaleOpen, () => s.InstantSaleOpen, v => s.InstantSaleOpen = v);
        SetS("SaleClosedNote", Note(p.SaleClosedNote), () => s.SaleClosedNote ?? "", v => s.SaleClosedNote = v);
        SetV("InvestmentOpen", p.InvestmentOpen, () => s.InvestmentOpen, v => s.InvestmentOpen = v);
        SetS("InvestmentClosedNote", Note(p.InvestmentClosedNote), () => s.InvestmentClosedNote ?? "", v => s.InvestmentClosedNote = v);
        SetV("InvestFee", p.InvestFee is { } a0 ? Math.Round(a0, 2) : null, () => s.InvestFee ?? rules.Value.InvestFee, v => s.InvestFee = v);
        SetV("CompoundFee", p.CompoundFee is { } a1 ? Math.Round(a1, 2) : null, () => s.CompoundFee ?? 0, v => s.CompoundFee = v);
        SetV("ResaleCommission", p.ResaleCommission is { } a2 ? Math.Round(a2, 4) : null, () => s.ResaleCommission ?? rules.Value.ResaleCommission, v => s.ResaleCommission = v);
        SetV("InstantCommission", p.InstantCommission is { } a3 ? Math.Round(a3, 4) : null, () => s.InstantCommission ?? rules.Value.InstantCommission, v => s.InstantCommission = v);
        if (p.ServiceTexts != null)
        {
            var texts = ServiceTexts.Parse(s.ServiceTextsJson);
            foreach (var (k, t) in p.ServiceTexts)
            {
                if (!ServiceKeys.All.Contains(k)) throw new DomainException("خدمة غير معروفة");
                var ar = Note(t.Ar, 300); var en = Note(t.En, 300);
                if (string.IsNullOrEmpty(ar) && string.IsNullOrEmpty(en)) texts.Remove(k); else texts[k] = new ServiceText(ar, en);
            }
            SetS("ServiceTexts", ServiceTexts.Write(texts), () => s.ServiceTextsJson, v => s.ServiceTextsJson = v);
        }
        SetV("TransferOpen", p.TransferOpen, () => s.TransferOpen, v => s.TransferOpen = v);
        SetS("TransferClosedNote", Note(p.TransferClosedNote), () => s.TransferClosedNote ?? "", v => s.TransferClosedNote = v);
        if (p.ComingSoon != null)
        {
            if (p.ComingSoon.Any(k => !ServiceKeys.All.Contains(k))) throw new DomainException("خدمة غير معروفة");
            SetS("ComingSoon", string.Join(",", ServiceKeys.All.Where(p.ComingSoon.Contains)), () => s.ComingSoon, v => s.ComingSoon = v);
        }
        SetV("SignupOpen", p.SignupOpen, () => s.SignupOpen, v => s.SignupOpen = v);
        SetS("SupportEmail", Note(p.SupportEmail, 160), () => s.SupportEmail, v => s.SupportEmail = v);
        SetS("SupportPhone", Note(p.SupportPhone, 30), () => s.SupportPhone, v => s.SupportPhone = v);
        SetS("WhatsApp", Note(p.WhatsApp, 30), () => s.WhatsApp, v => s.WhatsApp = v);
        SetV("AnnouncementOn", p.AnnouncementOn, () => s.AnnouncementOn, v => s.AnnouncementOn = v);
        SetS("Announcement", Note(p.Announcement, 400), () => s.Announcement ?? "", v => s.Announcement = v);
        SetS("AnnouncementTone", p.AnnouncementTone, () => s.AnnouncementTone, v => s.AnnouncementTone = v);
        SetV("Maintenance", p.Maintenance, () => s.Maintenance, v => s.Maintenance = v);
        SetS("MaintenanceNote", Note(p.MaintenanceNote), () => s.MaintenanceNote ?? "", v => s.MaintenanceNote = v);
        SetV("PasswordMinLength", p.PasswordMinLength, () => s.PasswordMinLength, v => s.PasswordMinLength = v);
        SetV("MaxLoginAttempts", p.MaxLoginAttempts, () => s.MaxLoginAttempts, v => s.MaxLoginAttempts = v);
        SetV("LockoutMinutes", p.LockoutMinutes, () => s.LockoutMinutes, v => s.LockoutMinutes = v);
        SetV("StaffIdleMinutes", p.StaffIdleMinutes, () => s.StaffIdleMinutes, v => s.StaffIdleMinutes = v);
        SetV("PaymentDeadlineHours", p.PaymentDeadlineHours, () => s.PaymentDeadlineHours ?? 72, v => s.PaymentDeadlineHours = v);
        SetV("TransferFee", p.TransferFee is { } tf0 ? Math.Round(tf0, 2) : null, () => s.TransferFee ?? 0, v => s.TransferFee = v);
        SetV("DepositAmount", p.DepositAmount is { } d0 ? Math.Round(d0, 2) : null, () => s.DepositAmount ?? rules.Value.DepositAmount, v => s.DepositAmount = v);

        if (changes.Count > 0)
        {
            s.UpdatedAt = Now;
            s.UpdatedBy = me.FullName;
            db.SettingsChanges.AddRange(changes);
            await db.SaveChangesAsync(ct);
            store.Invalidate();
        }
        return Admin(s);
    }

    private static string? Show<T>(T v) => v switch
    {
        null => null,
        bool b => b ? "مفعّل" : "متوقف",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    public async Task<List<SettingsChangeDto>> Handle(GetSettingsHistoryQuery q, CancellationToken ct)
    {
        await access.RequireAsync(Permissions.AuditView, ct);
        return await db.SettingsChanges.AsNoTracking().OrderByDescending(c => c.Id).Take(Math.Clamp(q.Take, 1, 500))
            .Select(c => new SettingsChangeDto(c.Id, c.At, c.UserName, c.Field, c.OldValue, c.NewValue)).ToListAsync(ct);
    }

    public async Task<int> Handle(SignOutEveryoneCommand cmd, CancellationToken ct)
    {
        var me = await access.RequireAsync(Permissions.SecurityManage, ct);
        var users = await db.Users.Where(u => u.Id != me.Id).ToListAsync(ct);
        foreach (var u in users) u.SecurityStamp = Guid.NewGuid().ToString("N");
        var now = Now;
        await db.RefreshTokens.Where(t => t.UserId != me.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "admin"), ct);
        db.SettingsChanges.Add(new SettingsChange { At = now, UserId = me.Id, UserName = me.FullName, Field = "SignOutEveryone", NewValue = users.Count.ToString(CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct);
        return users.Count;
    }

    // ───── personal settings (owners and staff) ─────
    private static PreferencesDto Prefs(User u) => new(u.NotifyOutbid, u.NotifyReminders, u.NotifyResults, u.NotifyRequests);

    public async Task<PreferencesDto> Handle(GetMyPreferencesQuery q, CancellationToken ct) => Prefs(await access.MeAsync(ct));

    public async Task<PreferencesDto> Handle(UpdateMyPreferencesCommand cmd, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var p = cmd.Preferences;
        u.NotifyOutbid = p.NotifyOutbid;
        u.NotifyReminders = p.NotifyReminders;
        u.NotifyResults = p.NotifyResults;
        u.NotifyRequests = p.NotifyRequests;
        await db.SaveChangesAsync(ct);
        return Prefs(u);
    }

    private async Task<List<SessionDto>> SessionsOfAsync(Guid userId, CancellationToken ct)
    {
        var now = Now;
        var rows = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId && t.CreatedAt > now.AddDays(-60))
            .Select(t => new { t.FamilyId, t.CreatedAt, t.ExpiresAt, t.RevokedAt, t.RevokedReason, t.CreatedByIp, t.UserAgent }).ToListAsync(ct);
        return rows.GroupBy(r => r.FamilyId)
            // a family is live while its newest token is neither revoked (other than by rotation) nor expired
            .Select(g => (Family: g.Key, First: g.MinBy(x => x.CreatedAt)!, Last: g.MaxBy(x => x.CreatedAt)!))
            .Where(x => x.Last.RevokedAt == null && x.Last.ExpiresAt > now)
            .OrderByDescending(x => x.Last.CreatedAt)
            .Select(x => new SessionDto(x.Family, x.First.CreatedAt, x.Last.CreatedAt, x.Last.ExpiresAt, x.Last.CreatedByIp, Device(x.Last.UserAgent), x.Family == current.SessionId))
            .ToList();
    }

    /// <summary>"Chrome · Windows" from the user agent</summary>
    public static string? Device(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return null;
        var browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") ? "Opera" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Safari/") ? "Safari" : null;
        var os = ua.Contains("Windows") ? "Windows" : ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? "Android" : ua.Contains("Mac OS") ? "macOS" : ua.Contains("Linux") ? "Linux" : null;
        return browser == null && os == null ? ua[..Math.Min(ua.Length, 60)] : string.Join(" · ", new[] { browser, os }.Where(x => x != null));
    }

    public async Task<List<SessionDto>> Handle(GetMySessionsQuery q, CancellationToken ct) => await SessionsOfAsync((await access.MeAsync(ct)).Id, ct);

    public async Task<List<SessionDto>> Handle(RevokeMySessionCommand cmd, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (cmd.SessionId == current.SessionId) throw new DomainException("هذا جهازك الحالي، استخدم «تسجيل الخروج»");
        var now = Now;
        var n = await db.RefreshTokens.Where(t => t.UserId == u.Id && t.FamilyId == cmd.SessionId && t.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "user"), ct);
        if (n == 0 && !await db.RefreshTokens.AnyAsync(t => t.UserId == u.Id && t.FamilyId == cmd.SessionId, ct)) throw DomainException.NotFound("الجلسة غير موجودة");
        return await SessionsOfAsync(u.Id, ct);
    }

    public async Task<SecuritySummaryDto> Handle(GetMySecurityQuery q, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        return new SecuritySummaryDto(u.LastLoginAt, (await SessionsOfAsync(u.Id, ct)).Count, (await store.GetAsync(ct)).PasswordMinLength);
    }
}
