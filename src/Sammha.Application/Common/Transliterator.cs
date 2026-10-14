using System.Text;
using System.Text.RegularExpressions;

namespace Sammha.Application.Common;

/// <summary>
/// Arabic camel names → Latin letters, for the English site, search and certificates. Same rules as the web app (src/lib/translit.ts):
/// names carry no vowel marks, so it follows the common patterns of camel names (فاعل → Sarim, فعيل → Kabir, فيعل → Hayla') plus well-known spellings.
/// Registry staff can override any spelling (ReservedName.LatinName).
/// </summary>
public static partial class Transliterator
{
    private static readonly Dictionary<string, string> Known = new()
    {
        ["ريم"] = "Reem", ["نوره"] = "Noura", ["نورة"] = "Noura", ["سهيل"] = "Suhail", ["مزنه"] = "Muznah", ["مزنة"] = "Muznah", ["مزن"] = "Muzn", ["فهد"] = "Fahd",
        ["محمد"] = "Mohammed", ["عبدالله"] = "Abdullah", ["عبدالرحمن"] = "Abdulrahman", ["سعد"] = "Saad", ["ناصر"] = "Nasser", ["فيصل"] = "Faisal", ["سلطان"] = "Sultan",
        ["خالد"] = "Khalid", ["سالم"] = "Salem", ["نايف"] = "Nayef", ["عمر"] = "Omar", ["علي"] = "Ali", ["حمد"] = "Hamad", ["ماجد"] = "Majed", ["سعيد"] = "Saeed",
        ["العتيبي"] = "Al-Otaibi", ["القحطاني"] = "Al-Qahtani", ["الدوسري"] = "Al-Dosari", ["المري"] = "Al-Marri", ["الشهري"] = "Al-Shehri", ["الهاجري"] = "Al-Hajri",
        ["شعلان"] = "Sha'lan", ["مجاهيم"] = "Majaheem", ["مغاتير"] = "Maghateer", ["وضح"] = "Wadh", ["صفر"] = "Sufr", ["شعل"] = "Sha'l", ["حمر"] = "Humr", ["زرق"] = "Zurq",
        ["بن"] = "bin", ["بنت"] = "bint", ["ابو"] = "Abu", ["أبو"] = "Abu", ["ام"] = "Umm", ["أم"] = "Umm",
    };
    private static readonly Dictionary<char, string> Cons = new()
    {
        ['ب'] = "b", ['ت'] = "t", ['ث'] = "th", ['ج'] = "j", ['ح'] = "h", ['خ'] = "kh", ['د'] = "d", ['ذ'] = "dh", ['ر'] = "r", ['ز'] = "z", ['س'] = "s", ['ش'] = "sh", ['ص'] = "s", ['ض'] = "d",
        ['ط'] = "t", ['ظ'] = "dh", ['ع'] = "'", ['غ'] = "gh", ['ف'] = "f", ['ق'] = "q", ['ك'] = "k", ['ل'] = "l", ['م'] = "m", ['ن'] = "n", ['ه'] = "h", ['ء'] = "'", ['ئ'] = "'", ['ؤ'] = "'", ['ة'] = "h",
    };

    [GeneratedRegex("[ً-ْٰـ]")] private static partial Regex Marks();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex("[؀-ۿ]")] private static partial Regex ArabicRx();
    [GeneratedRegex("aa+")] private static partial Regex LongA();
    [GeneratedRegex("''+")] private static partial Regex Quotes();

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"الهيلع" → "Al-Hayla'", "بنت الشمال" → "Bint Al-Shamal"</summary>
    public static string ToLatin(string? name)
    {
        var s = Spaces().Replace(Marks().Replace(name ?? "", ""), " ").Trim();
        if (s.Length == 0) return "";
        if (!ArabicRx().IsMatch(s)) return s;
        return Cap(string.Join(' ', s.Split(' ').Select(Word)));
    }

    private static string Word(string w)
    {
        if (Known.TryGetValue(w, out var k)) return k;
        if (w.StartsWith("ال") && w.Length > 3)
        {
            var rest = w[2..];
            return "Al-" + Cap(Known.TryGetValue(rest, out var kr) ? kr : Stem(rest));
        }
        return Cap(Stem(w));
    }

    private enum Kind { C, Long, End }
    private record Tok(string V, Kind K);

    private static string Stem(string w)
    {
        var ch = w.ToCharArray();
        var toks = new List<Tok>();
        for (var i = 0; i < ch.Length; i++)
        {
            var c = ch[i];
            bool first = i == 0, last = i == ch.Length - 1;
            var prev = toks.Count > 0 ? toks[^1] : null;
            var afterCons = prev is { K: Kind.C };
            var nextIsVowelLetter = i + 1 < ch.Length && "اوي".Contains(ch[i + 1]);
            if (c is 'ا' or 'أ' or 'إ' or 'آ' or 'ى')
            {
                if (first) toks.Add(new(c == 'إ' ? "i" : c == 'آ' ? "aa" : "a", Kind.Long));
                else if (c is 'أ' or 'إ') toks.Add(new("'", Kind.C));
                else toks.Add(new("a", Kind.Long));
                continue;
            }
            if (c == 'و')
            {
                toks.Add(first || !afterCons || nextIsVowelLetter ? new("w", Kind.C) : new("u", Kind.Long));
                continue;
            }
            if (c == 'ي')
            {
                if (first || !afterCons || nextIsVowelLetter) toks.Add(new("y", Kind.C));
                else if (i == ch.Length - 2 && "ةه".Contains(ch[i + 1])) toks.Add(new("iy", Kind.Long));
                else if (toks.Count == 1 && !last) toks.Add(new("ay", Kind.Long));
                else toks.Add(new("i", Kind.Long));
                continue;
            }
            if (c is 'ة' or 'ه' && last && toks.Count > 1 && (afterCons || prev?.V == "iy")) { toks.Add(new("ah", Kind.End)); continue; }
            if (c == 'ع' && first) { toks.Add(new("a", Kind.Long)); continue; }
            toks.Add(new(Cons.GetValueOrDefault(c, ""), Kind.C));
        }
        var consOnly = toks.All(t => t.K == Kind.C);
        var sb = new StringBuilder();
        for (var i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            if (i > 0 && toks[i - 1].K == Kind.C && t.K == Kind.C)
            {
                if (!(consOnly && toks.Count == 3 && i == 2))
                {
                    var longBefore = i >= 2 && toks[i - 2] is { K: Kind.Long, V: "a" };
                    sb.Append(longBefore && i == toks.Count - 1 ? "i" : "a");
                }
            }
            sb.Append(t.V);
        }
        var o = sb.ToString();
        if (o.StartsWith('\'')) o = o[1..];
        return Quotes().Replace(LongA().Replace(o, "aa"), "'");
    }

    /// <summary>true when a search query is written in Latin letters</summary>
    public static bool IsLatin(string q) => q.Any(char.IsAsciiLetter) && !ArabicRx().IsMatch(q);

    [GeneratedRegex(@"^al[-\s]?")] private static partial Regex AlPrefix();
    [GeneratedRegex(@"[-'’`\s]")] private static partial Regex Punct();
    [GeneratedRegex(@"(.)\1+")] private static partial Regex Doubles();

    /// <summary>loose key for Latin search: case, "Al-", hyphens, apostrophes and doubled letters are ignored</summary>
    public static string Key(string s)
    {
        var k = Punct().Replace(AlPrefix().Replace(s.ToLowerInvariant(), ""), "").Replace("ee", "i").Replace("oo", "u");
        return Doubles().Replace(k, "$1");
    }
}
