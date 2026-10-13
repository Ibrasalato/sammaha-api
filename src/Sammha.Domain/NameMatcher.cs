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
