namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Pragmatic Latin-to-Hebrew and Latin-to-Cyrillic transliteration for name
/// rendering (a DM tool, not a linguistics paper). Rules are deterministic and
/// digraph-aware ("sh" -&gt; "ш"/"ש", "th" -&gt; "т"/"ת"), vowels use matres,
/// word-final נ/ם/ף get their sofit forms. Output is recognizable rather than
/// perfect: "Ben" becomes בן/бен, "Vladimir" becomes ולאדימיר/владимир.
/// Hand-authored <see cref="MorphemeYaml.Latin"/> fields always win over this;
/// text already in the target script passes through untouched.
/// </summary>
public static class Transliterator
{
    private static readonly Dictionary<string, string> HebrewPairs = new(StringComparer.Ordinal)
    {
        ["sh"] = "ש", ["ch"] = "צ", ["th"] = "ת", ["ph"] = "פ", ["kh"] = "ח",
        ["gh"] = "ג", ["ck"] = "ק", ["qu"] = "קו",
        ["ee"] = "י", ["oo"] = "ו", ["ou"] = "או", ["ow"] = "או",
        ["ai"] = "אי", ["ei"] = "אי", ["ay"] = "אי", ["ey"] = "אי",
        ["au"] = "או", ["aw"] = "או", ["ea"] = "אי", ["ie"] = "י",
    };

    private static readonly Dictionary<char, string> HebrewSingles = new()
    {
        ['a'] = "א", ['b'] = "ב", ['c'] = "כ", ['d'] = "ד", ['f'] = "פ",
        ['g'] = "ג", ['h'] = "ה", ['i'] = "י", ['j'] = "ג", ['k'] = "ק",
        ['l'] = "ל", ['m'] = "מ", ['n'] = "נ", ['o'] = "ו", ['p'] = "פ",
        ['q'] = "ק", ['r'] = "ר", ['s'] = "ס", ['t'] = "ט", ['u'] = "ו",
        ['v'] = "ו", ['w'] = "ו", ['x'] = "קס", ['y'] = "י", ['z'] = "ז",
    };

    private static readonly Dictionary<string, string> CyrillicPairs = new(StringComparer.Ordinal)
    {
        ["zh"] = "ж", ["sh"] = "ш", ["ch"] = "ч", ["th"] = "т", ["ph"] = "ф",
        ["kh"] = "х", ["shch"] = "щ", ["qu"] = "кв", ["ck"] = "к", ["gh"] = "г",
        ["ee"] = "и", ["oo"] = "у", ["ou"] = "у", ["ow"] = "ау",
        ["ai"] = "ай", ["ei"] = "ей", ["ay"] = "ай", ["ey"] = "ей",
        ["au"] = "ау", ["aw"] = "ав", ["ea"] = "и", ["ie"] = "и", ["oh"] = "о",
        ["ya"] = "я", ["ye"] = "е", ["yi"] = "и", ["yo"] = "ё", ["yu"] = "ю",
    };

    private static readonly Dictionary<char, string> CyrillicSingles = new()
    {
        ['a'] = "а", ['b'] = "б", ['c'] = "к", ['d'] = "д", ['e'] = "е",
        ['f'] = "ф", ['g'] = "г", ['h'] = "х", ['i'] = "и", ['j'] = "дж",
        ['k'] = "к", ['l'] = "л", ['m'] = "м", ['n'] = "н", ['o'] = "о",
        ['p'] = "п", ['q'] = "к", ['r'] = "р", ['s'] = "с", ['t'] = "т",
        ['u'] = "у", ['v'] = "в", ['w'] = "в", ['x'] = "кс", ['y'] = "й",
        ['z'] = "з",
    };

    public static bool IsHebrew(string s) => s.Any(c => c is >= '\u0590' and <= '\u05FF');
    public static bool IsCyrillic(string s) => s.Any(c => c is >= '\u0400' and <= '\u04FF');
    public static bool IsLatin(string s) => s.All(c => c < 128);

    public static string ToHebrew(string latin) => Transliterate(latin, HebrewPairs, HebrewSingles, ToHebrewChar);

    public static string ToCyrillic(string latin) => Transliterate(latin, CyrillicPairs, CyrillicSingles, ToCyrillicChar);

    public static string Transliterate(string text, NameScript script) => script switch
    {
        NameScript.Hebrew => IsHebrew(text) || !IsLatin(text) ? text : ToHebrew(text),
        NameScript.Cyrillic => IsCyrillic(text) || !IsLatin(text) ? text : ToCyrillic(text),
        _ => text,
    };

    /// <summary>
    /// Renders one morpheme in the requested script: authored forms first,
    /// machine transliteration as the fallback, anything else untouched.
    /// A capitalized source stays capitalized ("Starhaven" -> "Стархавен",
    /// while "al-Sahra" keeps its lowercase particle).
    /// </summary>
    public static string Render(MorphemeYaml m, NameScript script) => script switch
    {
        NameScript.Latin => !string.IsNullOrWhiteSpace(m.Latin) ? m.Latin : m.Form,
        NameScript.Hebrew => IsHebrew(m.Form) ? m.Form
            : !string.IsNullOrWhiteSpace(m.Latin) && IsLatin(m.Latin) ? MatchCase(ToHebrew(m.Latin), m.Latin)
            : IsLatin(m.Form) ? MatchCase(ToHebrew(m.Form), m.Form) : m.Form,
        NameScript.Cyrillic => IsCyrillic(m.Form) ? m.Form
            : !string.IsNullOrWhiteSpace(m.Latin) && IsLatin(m.Latin) ? MatchCase(ToCyrillic(m.Latin), m.Latin)
            : IsLatin(m.Form) ? MatchCase(ToCyrillic(m.Form), m.Form) : m.Form,
        _ => m.Form,
    };

    private static string MatchCase(string rendered, string source)
        => !string.IsNullOrEmpty(source) && char.IsUpper(source[0]) && !string.IsNullOrEmpty(rendered)
            ? char.ToUpperInvariant(rendered[0]) + rendered.Substring(1)
            : rendered;

    /// <summary>Same fallback chain for plain-string markers (patronymics).</summary>
    public static string RenderToken(string native, string latin, NameScript script) => script switch
    {
        NameScript.Latin => !string.IsNullOrWhiteSpace(latin) ? latin : native,
        // Prefer the Latin transliteration as the machine source; Transliterate
        // passes text already in the target script straight through.
        NameScript.Hebrew or NameScript.Cyrillic => Transliterate(
            !string.IsNullOrWhiteSpace(latin) ? latin : native, script),
        _ => native,
    };

    private static string Transliterate(
        string text,
        Dictionary<string, string> pairs,
        Dictionary<char, string> singles,
        Func<char, char, bool, bool, string> single)
    {
        var lower = text.ToLowerInvariant();
        var out_ = new System.Text.StringBuilder(lower.Length * 2);
        int i = 0;
        while (i < lower.Length)
        {
            if (i + 4 <= lower.Length && pairs.TryGetValue(lower.Substring(i, 4), out var quad))
            {
                out_.Append(quad);
                i += 4;
                continue;
            }
            if (i + 2 <= lower.Length && pairs.TryGetValue(lower.Substring(i, 2), out var pair))
            {
                out_.Append(pair);
                i += 2;
                continue;
            }
            char c = lower[i];
            char next = i + 1 < lower.Length ? lower[i + 1] : '\0';
            out_.Append(single(c, next, i == 0, i == lower.Length - 1));
            i++;
        }
        return out_.ToString();
    }

    private static string ToHebrewChar(char c, char next, bool first, bool last)
    {
        // Word-initial vowels get their mater ("Or" -> אור, initial "E" -> א);
        // inner "e" is silent-ish and drops ("Medved" -> מדבד, "Ben" -> בן).
        // "c" softens before e/i/y ("Cian" -> סיאן, "Crag" -> כרג).
        if (c == 'e') return first ? "א" : "";
        if (c == 'o' && first) return "או";
        if (c == 'c' && (next == 'e' || next == 'i' || next == 'y')) return "ס";
        if (!HebrewSingles.TryGetValue(c, out var s)) return c.ToString();
        if (last) s = c switch { 'n' => "ן", 'm' => "ם", 'p' => "ף", 'f' => "ף", _ => s };
        return s;
    }

    private static string ToCyrillicChar(char c, char next, bool first, bool last)
    {
        if (c == 'c' && (next == 'e' || next == 'i' || next == 'y')) return "с";
        if (!CyrillicSingles.TryGetValue(c, out var s)) return c.ToString();
        return s;
    }
}
