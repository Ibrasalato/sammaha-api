using System.Text;
using System.Text.RegularExpressions;

namespace Sammha.Domain;

public record SimilarHit(string Name, string Type, string Source, double Score);
public record MatchSource(string Name, string Normalized, string Type, string Source);
public record NameCheck(string Name, string Normalized, string Verdict, string Message, List<SimilarHit> Hits);

/// <summary>Arabic name normalisation and similarity. Same rules as the front end's names.ts.</summary>
public static partial class NameMatcher
{
    [GeneratedRegex("[ً-ٰٟۖ-ۭـ]")] private static partial Regex Marks();
    [GeneratedRegex("[^ء-يa-zA-Z0-9]")] private static partial Regex NonLetters();
    [GeneratedRegex("^[؀-ۿ\\s]+$")] private static partial Regex ArabicOnly();

    public static string Normalize(string? input)
    {
        var s = (input ?? "").Trim();
        s = Marks().Replace(s, "");
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ٱ', 'ا')
             .Replace('ى', 'ي').Replace('ة', 'ه').Replace('ؤ', 'و').Replace('ئ', 'ي').Replace("ء", "");
        s = NonLetters().Replace(s, "");
        if (s.Length > 4 && s.StartsWith("ال")) s = s[2..];
        return s.ToLowerInvariant();
    }

    public static string? Validate(string? input)
    {
        var raw = (input ?? "").Trim();
        if (raw.Length == 0) return "اكتب الاسم";
        if (raw.Length < 2) return "الاسم قصير جدًا";
        if (raw.Length > 30) return "الاسم أطول من 30 حرفًا";
        if (!ArabicOnly().IsMatch(raw)) return "استخدم الحروف العربية فقط";
        return null;
    }

    public static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>0..1 on normalised names; a name contained in the other scores at least 0.8</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var score = 1 - (double)Levenshtein(a, b) / Math.Max(a.Length, b.Length);
        if (Math.Min(a.Length, b.Length) >= 3 && (a.Contains(b) || b.Contains(a))) score = Math.Max(score, 0.8);
        return score;
    }

    public static List<SimilarHit> FindMatches(string normalized, IEnumerable<MatchSource> pool, double threshold)
    {
        var hits = new List<SimilarHit>();
        foreach (var p in pool)
        {
            var score = Similarity(normalized, p.Normalized);
            if (score >= threshold) hits.Add(new SimilarHit(p.Name, p.Type, p.Source, Math.Round(score, 2)));
        }
        return hits.OrderByDescending(h => h.Score).Take(8).ToList();
    }

    public static string NamesCount(int n) => n switch { 1 => "اسم واحد", 2 => "اسمين", <= 10 => $"{n} أسماء", _ => $"{n} اسمًا" };

    /// <summary>"فهد عبدالله الدوسري" → "فهد الد…" (public bid history)</summary>
    public static string MaskName(string? full)
    {
        var parts = (full ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return $"{parts[0][..Math.Min(2, parts[0].Length)]}…";
        var last = parts[^1];
        return $"{parts[0]} {last[..Math.Min(2, last.Length)]}…";
    }

    /// <summary>Saudi mobile in canonical 05XXXXXXXX form (accepts Arabic digits, +966, 5XXXXXXXX)</summary>
    public static string NormalizePhone(string? p)
    {
        var sb = new StringBuilder();
        foreach (var c in p ?? "")
        {
            if (c is >= '٠' and <= '٩') sb.Append((char)('0' + (c - '٠')));
            else if (char.IsAsciiDigit(c)) sb.Append(c);
        }
        var d = sb.ToString();
        if (d.StartsWith("9665") && d.Length == 12) return "0" + d[3..];
        if (d.StartsWith('5') && d.Length == 9) return "0" + d;
        return d;
    }

    public static bool IsSaudiMobile(string phone) => phone.Length == 10 && phone.StartsWith("05") && phone.All(char.IsAsciiDigit);
}

/// <summary>
/// Mobile numbers. Saudi numbers keep the local form 05xxxxxxxx (as every existing account has);
/// other countries are stored as +{country code}{number}, e.g. +971501234567.
/// </summary>
public static class Phones
{
    /// <returns>the stored form, or null when the number is not valid for that country</returns>
    public static string? Normalize(string? countryCode, string? number)
    {
        var dial = NameMatcher.NormalizePhone(countryCode);
        var n = NameMatcher.NormalizePhone(number);
        if (n.Length == 0) return null;
        if (dial.Length == 0) return FromInput(number);
        if (dial == "966")
        {
            if (n.StartsWith("966")) n = n[3..];
            if (n.Length == 9 && n.StartsWith('5')) n = "0" + n;
            return NameMatcher.IsSaudiMobile(n) ? n : null;
        }
        if (dial.Length > 4) return null;
        if (n.StartsWith(dial) && n.Length > dial.Length + 6) n = n[dial.Length..];
        n = n.TrimStart('0');
        return n.Length is >= 6 and <= 12 ? $"+{dial}{n}" : null;
    }

    /// <summary>one field: 05… (Saudi), or +971… / 00971… (international)</summary>
    public static string? FromInput(string? raw)
    {
        var t = (raw ?? "").Trim();
        var n = NameMatcher.NormalizePhone(t);
        if (t.StartsWith('+') || n.StartsWith("00"))
        {
            n = n.TrimStart('0');
            if (n.StartsWith("966")) return Normalize("966", n[3..]);
            return n.Length is >= 8 and <= 15 ? "+" + n : null;
        }
        if (n.Length == 9 && n.StartsWith('5')) n = "0" + n;
        return NameMatcher.IsSaudiMobile(n) ? n : null;
    }

    /// <summary>where a code can be sent by SMS: 9665… for Saudi numbers, the digits for others</summary>
    public static string ForSms(string stored) => stored.StartsWith('+') ? stored[1..] : stored.StartsWith("05") ? "966" + stored[1..] : stored;
}

/// <summary>Identity numbers: Saudi national ID (1…), resident iqama (2…), or a passport for those living abroad.</summary>
public static class Identity
{
    public static string? Validate(string? id, string? nationality, string? residence)
    {
        var v = (id ?? "").Trim().ToUpperInvariant();
        if (v.Length == 0) return "أدخل رقم الهوية";
        if (residence == "SA" || nationality == "SA")
        {
            if (v.Length != 10 || !v.All(char.IsAsciiDigit)) return "رقم الهوية أو الإقامة 10 أرقام";
            if (nationality == "SA" && v[0] != '1') return "رقم الهوية الوطنية يبدأ بـ 1";
            if (nationality != "SA" && v[0] != '2') return "رقم الإقامة يبدأ بـ 2";
            return null;
        }
        return v.Length is >= 5 and <= 20 && v.All(char.IsAsciiLetterOrDigit) ? null : "رقم الهوية أو الجواز من 5 إلى 20 حرفًا ورقمًا";
    }
}
