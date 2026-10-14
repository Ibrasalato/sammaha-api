using System.Numerics;

namespace Sammha.Domain;

public record IbanCheck(bool Valid, string Iban, string? Country, string? BankCode, string? BankName, string Message);

/// <summary>IBAN validation (ISO 13616: length per country and the mod-97 check digits) and the Saudi bank from its code.</summary>
public static class Iban
{
    /// <summary>IBAN length per country for the countries Sammha owners come from; others use the general 15–34 rule</summary>
    private static readonly Dictionary<string, int> Lengths = new()
    {
        ["SA"] = 24, ["AE"] = 23, ["KW"] = 30, ["QA"] = 29, ["BH"] = 22, ["OM"] = 23, ["JO"] = 30, ["EG"] = 29, ["LB"] = 28, ["IQ"] = 23,
        ["PS"] = 29, ["TN"] = 24, ["DZ"] = 26, ["MA"] = 28, ["MR"] = 27, ["LY"] = 25, ["SD"] = 18, ["YE"] = 30, ["TR"] = 26, ["PK"] = 24,
        ["GB"] = 22, ["FR"] = 27, ["DE"] = 22,
    };

    /// <summary>Saudi bank codes (the two digits after «SA» and the check digits)</summary>
    public static readonly Dictionary<string, string> SaudiBanks = new()
    {
        ["05"] = "مصرف الإنماء", ["10"] = "البنك الأهلي السعودي", ["15"] = "بنك البلاد", ["20"] = "بنك الرياض", ["30"] = "البنك العربي الوطني",
        ["45"] = "البنك السعودي الأول (ساب)", ["55"] = "البنك السعودي الفرنسي", ["60"] = "بنك الجزيرة", ["65"] = "البنك السعودي للاستثمار",
        ["80"] = "مصرف الراجحي", ["90"] = "بنك الخليج الدولي", ["95"] = "بنك الإمارات دبي الوطني",
    };

    public static string Clean(string? raw)
    {
        var s = new string((raw ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        // Arabic-Indic digits typed on a phone
        return new string(s.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c).ToArray());
    }

    public static IbanCheck Check(string? raw)
    {
        var s = Clean(raw);
        if (s.Length < 4) return new(false, s, null, null, null, "اكتب رقم الآيبان");
        if (!s.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(s[0]) || !char.IsAsciiLetter(s[1]) || !char.IsAsciiDigit(s[2]) || !char.IsAsciiDigit(s[3]))
            return new(false, s, null, null, null, "الآيبان يبدأ بحرفي الدولة ثم رقمين، مثل SA03…");
        var country = s[..2];
        if (Lengths.TryGetValue(country, out var len) ? s.Length != len : s.Length is < 15 or > 34)
            return new(false, s, country, null, null, len > 0 ? $"آيبان {country} يتكون من {len} خانة، المُدخل {s.Length}" : "طول الآيبان غير صحيح");
        if (country == "SA" && !s[4..].All(char.IsAsciiDigit)) return new(false, s, country, null, null, "الآيبان السعودي أرقام بعد SA");
        // ISO 7064 mod 97-10
        var moved = s[4..] + s[..4];
        var digits = string.Concat(moved.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()));
        if (BigInteger.Parse(digits) % 97 != 1) return new(false, s, country, null, null, "رقم الآيبان غير صحيح (خانات التحقق لا تطابق)، راجعه من شهادة الآيبان");
        string? code = null, bank = null;
        if (country == "SA") { code = s.Substring(4, 2); bank = SaudiBanks.GetValueOrDefault(code); }
        return new(true, s, country, code, bank, bank != null ? $"آيبان صحيح · {bank}" : "آيبان صحيح");
    }

    /// <summary>SA03 •••• •••• 1234</summary>
    public static string Mask(string iban) => iban.Length < 8 ? iban : $"{iban[..4]} •••• •••• {iban[^4..]}";

    /// <summary>a short, readable form: SA03 8000 0000 6080 1016 7519</summary>
    public static string Pretty(string iban) => string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));
}

public static class BankAccountStatuses
{
    public const string Pending = "pending", Verified = "verified", Rejected = "rejected";
}

/// <summary>the owner's bank account for refunds, with the IBAN certificate they uploaded; finance verifies it before any transfer</summary>
public class BankAccount
{
    public Guid UserId { get; set; }
    public string Iban { get; set; } = "";
    public string BankName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string DocName { get; set; } = "";
    public string DocType { get; set; } = "";
    public byte[] DocBytes { get; set; } = [];
    public string Status { get; set; } = BankAccountStatuses.Pending;
    public string? RejectReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
