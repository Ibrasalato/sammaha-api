using Microsoft.EntityFrameworkCore;
using Sammha.Domain;

namespace Sammha.Application.Common;

/// <summary>
/// Reads the platform settings row (created with defaults on first use). Cached for a few seconds per process,
/// so every request can check the switches without a database round trip; a change made here clears the cache at once,
/// and other API instances pick it up within the cache window.
/// </summary>
public class SettingsStore(IAppDbContext db, TimeProvider clock, SettingsCache cache)
{
    public async Task<PlatformSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGet(out var hit)) return hit;
        var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct) ?? new PlatformSettings();
        cache.Set(s);
        return s;
    }

    /// <summary>the mark request guarantee in force: the admin's value, or the server default</summary>
    public async Task<decimal> DepositAmountAsync(decimal fallback, CancellationToken ct = default) => (await GetAsync(ct)).DepositAmount ?? fallback;

    /// <summary>hours an auction winner has to pay before the win is cancelled</summary>
    public async Task<decimal> TransferFeeAsync(CancellationToken ct = default) => (await GetAsync(ct)).TransferFee ?? 0;
    public async Task<decimal> InvestFeeAsync(decimal fallback, CancellationToken ct = default) => (await GetAsync(ct)).InvestFee ?? fallback;
    public async Task<decimal> CompoundFeeAsync(CancellationToken ct = default) => (await GetAsync(ct)).CompoundFee ?? 0;
    public async Task<decimal> ResaleCommissionAsync(decimal fallback, CancellationToken ct = default) => (await GetAsync(ct)).ResaleCommission ?? fallback;
    public async Task<decimal> InstantCommissionAsync(decimal fallback, CancellationToken ct = default) => (await GetAsync(ct)).InstantCommission ?? fallback;

    public async Task<int> PaymentDeadlineHoursAsync(CancellationToken ct = default) => (await GetAsync(ct)).PaymentDeadlineHours ?? 72;

    /// <summary>the tracked row for editing (inserted with defaults if missing)</summary>
    public async Task<PlatformSettings> ForUpdateAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (s == null) { s = new PlatformSettings { UpdatedAt = clock.GetUtcNow() }; db.Settings.Add(s); }
        return s;
    }

    public void Invalidate() => cache.Clear();

    /// <summary>throws when the admin has closed this intake</summary>
    public async Task EnsureOpenAsync(string intake, CancellationToken ct = default)
    {
        var s = await GetAsync(ct);
        var soonKey = intake switch { Intakes.ResaleAuction => ServiceKeys.Resale, Intakes.InstantSale => ServiceKeys.Instant, _ => intake };
        var (open, note, label) = intake switch
        {
            Intakes.Requests => (s.RequestsOpen, s.RequestsClosedNote, "استقبال طلبات الشعارات"),
            Intakes.Compound => (s.CompoundOpen, s.CompoundClosedNote, "استقبال طلبات الأسماء المركبة"),
            Intakes.ResaleAuction => (s.ResaleAuctionOpen, s.SaleClosedNote, "عرض الأسماء في مزاد إعادة البيع"),
            Intakes.InstantSale => (s.InstantSaleOpen, s.SaleClosedNote, "البيع الفوري للأسماء"),
            Intakes.Investment => (s.InvestmentOpen, s.InvestmentClosedNote, "استقبال طلبات استثمار الاسم"),
            Intakes.Transfer => (s.TransferOpen, s.TransferClosedNote, "نقل ملكية الأسماء"),
            _ => (true, null, ""),
        };
        if (ServiceKeys.Parse(s.ComingSoon).Contains(soonKey)) throw new DomainException($"{label}: الخدمة قريبًا، تابعنا لمعرفة موعد الإطلاق", 403);
        if (!open) throw new DomainException(string.IsNullOrWhiteSpace(note) ? $"{label} متوقف مؤقتًا، تابعنا لمعرفة موعد الفتح" : $"{label} متوقف مؤقتًا: {note}", 403);
    }

    public async Task EnsurePasswordAsync(string? password, CancellationToken ct = default)
    {
        var min = (await GetAsync(ct)).PasswordMinLength;
        if ((password ?? "").Length < min) throw new DomainException($"كلمة المرور {min} أحرف على الأقل");
        if ((password ?? "").Length > 128) throw new DomainException("كلمة المرور طويلة جدًا");
    }
}

/// <summary>Per-process cache of the settings row (a singleton), refreshed every few seconds.</summary>
public class SettingsCache
{
    private const long TtlMs = 10_000;
    private PlatformSettings? _value;
    private long _at;

    public bool TryGet(out PlatformSettings value)
    {
        var v = _value;
        value = v!;
        return v != null && Environment.TickCount64 - Volatile.Read(ref _at) < TtlMs;
    }

    public void Set(PlatformSettings s) { _value = s; Volatile.Write(ref _at, Environment.TickCount64); }
    public void Clear() => _value = null;
}

public static class Intakes
{
    public const string Requests = "requests";
    public const string Compound = "compound";
    public const string ResaleAuction = "resale";
    public const string InstantSale = "instant";
    public const string Investment = "investment";
    public const string Transfer = "transfer";
}

/// <summary>the e-services the admin can open, close or mark «قريبًا»</summary>
public static class ServiceKeys
{
    public const string Requests = "requests", Compound = "compound", Resale = "resale", Instant = "instant", Investment = "investment", Transfer = "transfer";
    public static readonly string[] All = [Requests, Compound, Resale, Instant, Investment, Transfer];
    public static List<string> Parse(string? csv) => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(All.Contains).Distinct().ToList();
}

/// <summary>the admin's own wording for a service card (empty = the built-in text)</summary>
/// <summary>the admin's own wording for a service card and its terms (empty = the built-in text)</summary>
public record ServiceText(string? Ar, string? En, string? TermsAr = null, string? TermsEn = null);

public static class ServiceTexts
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) };
    public static Dictionary<string, ServiceText> Parse(string? json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ServiceText>>(json ?? "{}", Json) ?? []; } catch { return []; }
    }
    public static string Write(Dictionary<string, ServiceText> d) => System.Text.Json.JsonSerializer.Serialize(d, Json);
}
