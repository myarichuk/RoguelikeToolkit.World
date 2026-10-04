using System.Text;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Pragmatic Latin-to-Hebrew and Latin-to-Cyrillic transliteration for name
/// rendering (a DM tool, not a linguistics paper). Works word by word, so
/// "al-Sahra" or "ben David" are treated as separate words; digraph-aware
/// ("sh" -&gt; "ш"/"ש"); follows how modern Hebrew spells foreign names
/// (short a/e are unwritten mid-word, o/u/i take matres, word-final a becomes ה,
/// final נ/מ/ף/ץ take sofit forms). Output is recognizable rather than perfect:
/// "Ben" becomes בן/бен, "Thrungar" becomes תרונגר/трунгар.
/// Hand-authored <see cref="MorphemeYaml.Latin"/> fields always win over this;
/// text already in the target script passes through untouched.
/// </summary>
public static class Transliterator
{
    private static readonly Dictionary<string, string> HebrewPairs = new(StringComparer.Ordinal)
    {
        ["sh"] = "ש", ["ch"] = "צ׳", ["th"] = "ת", ["ph"] = "פ", ["kh"] = "ח",
        ["gh"] = "ג", ["ck"] = "ק", ["qu"] = "קו", ["ts"] = "צ", ["tz"] = "צ", ["zh"] = "ז׳",
        ["ee"] = "י", ["oo"] = "ו", ["ou"] = "או", ["ow"] = "או",
        ["ai"] = "אי", ["ei"] = "אי", ["ay"] = "אי", ["ey"] = "אי",
        ["au"] = "או", ["aw"] = "או", ["ea"] = "י", ["ie"] = "י", ["ae"] = "אי",
    };

    private static readonly Dictionary<char, string> HebrewConsonants = new()
    {
        ['b'] = "ב", ['c'] = "ק", ['d'] = "ד", ['f'] = "פ", ['g'] = "ג", ['h'] = "ה",
        ['j'] = "ג׳", ['k'] = "ק", ['l'] = "ל", ['m'] = "מ", ['n'] = "נ", ['p'] = "פ",
        ['q'] = "ק", ['r'] = "ר", ['s'] = "ס", ['t'] = "ט", ['v'] = "ו", ['w'] = "ו",
        ['x'] = "קס", ['z'] = "ז",
    };

    private static readonly Dictionary<string, string> CyrillicPairs = new(StringComparer.Ordinal)
    {
        ["zh"] = "ж", ["sh"] = "ш", ["ch"] = "ч", ["th"] = "т", ["ph"] = "ф", ["ts"] = "ц", ["tz"] = "ц",
        ["kh"] = "х", ["qu"] = "кв", ["ck"] = "к", ["gh"] = "г", ["wh"] = "в",
        ["ee"] = "и", ["oo"] = "у", ["ou"] = "у", ["ow"] = "ау",
        ["ai"] = "ай", ["ei"] = "ей", ["ay"] = "ай", ["ey"] = "ей",
        ["au"] = "ау", ["aw"] = "ав", ["ea"] = "и", ["ie"] = "и", ["oh"] = "о", ["ae"] = "е",
        ["ya"] = "я", ["ye"] = "е", ["yi"] = "и", ["yo"] = "ё", ["yu"] = "ю",
    };

    private static readonly Dictionary<char, string> CyrillicSingles = new()
    {
        ['a'] = "а", ['b'] = "б", ['c'] = "к", ['d'] = "д", ['e'] = "е",
        ['f'] = "ф", ['g'] = "г", ['h'] = "х", ['i'] = "и", ['j'] = "дж",
        ['k'] = "к", ['l'] = "л", ['m'] = "м", ['n'] = "н", ['o'] = "о",
        ['p'] = "п", ['q'] = "к", ['r'] = "р", ['s'] = "с", ['t'] = "т",
        ['u'] = "у", ['v'] = "в", ['w'] = "в", ['x'] = "кс", ['z'] = "з",
    };

    private const string Vowels = "aeiouy";

    public static bool IsHebrew(string s) => s.Any(c => c is >= '֐' and <= '׿');
    public static bool IsCyrillic(string s) => s.Any(c => c is >= 'Ѐ' and <= 'ӿ');
    public static bool IsLatin(string s) => s.All(c => c < 128);

    public static string ToHebrew(string latin) => Words(latin, HebrewWord);

    public static string ToCyrillic(string latin) => Words(latin, CyrillicWord);

    public static string Transliterate(string text, NameScript script) => script switch
    {
        NameScript.Hebrew => IsHebrew(text) || !IsLatin(text) ? text : ToHebrew(text),
        NameScript.Cyrillic => IsCyrillic(text) || !IsLatin(text) ? text : ToCyrillic(text),
        _ => text,
    };

    /// <summary>
    /// Renders one morpheme in the requested script: authored forms first,
    /// machine transliteration as the fallback, anything else untouched.
    /// </summary>
    public static string Render(MorphemeYaml m, NameScript script) => script switch
    {
        NameScript.Latin => Spoken(!string.IsNullOrWhiteSpace(m.Latin) ? m.Latin : m.Form),
        NameScript.Hebrew => IsHebrew(m.Form) ? m.Form
            : !string.IsNullOrWhiteSpace(m.Latin) && IsLatin(m.Latin) ? HebrewWithArticles(m.Latin)
            : IsLatin(m.Form) ? HebrewWithArticles(m.Form) : m.Form,
        NameScript.Cyrillic => IsCyrillic(m.Form) ? m.Form
            : !string.IsNullOrWhiteSpace(m.Latin) && IsLatin(m.Latin) ? CyrillicWithArticles(m.Latin)
            : IsLatin(m.Form) ? CyrillicWithArticles(m.Form) : m.Form,
        _ => Spoken(m.Form),
    };

    // ---- the Arabic article ----------------------------------------------------
    //
    // Tables write the article as it is spelled, "al-", the way a dictionary does. Spoken
    // (and in every popular romanization) it assimilates to a following "sun letter":
    // "al-Din" is "ad-Din", "al-Shams" is "ash-Shams", "al-Nur" is "an-Nur"; before a
    // "moon letter" it stays ("al-Qamar", "al-Bahr"). Names are rendered as spoken, so a
    // table author writes the dictionary form and every script gets the right one:
    // Latin "ad-Din", Cyrillic "ад-Дин" ("аль-Бахр" for a moon letter, as Russian writes
    // it), Hebrew "א-דין" (the article reduced to its alef, "אל-בחר" otherwise).

    private static readonly System.Text.RegularExpressions.Regex Article =
        new(@"(?<![A-Za-z])al-(?=[A-Za-z])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // Romanized sun letters: t th d dh r z s sh (and the emphatics written as t d s z) l n.
    private static readonly string[] SunLetters = { "th", "dh", "sh", "t", "d", "r", "z", "s", "l", "n" };

    /// <summary>
    /// The consonant an Arabic article assimilates to before <paramref name="word"/>
    /// ("sh" for "Shams", "d" for "Din"), or null before a moon letter ("Qamar").
    /// </summary>
    public static string? SunLetter(string word)
    {
        foreach (var s in SunLetters)
            if (word.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }

    /// <summary>Latin text with every "al-" article as spoken: "Nasr al-Din" -> "Nasr ad-Din".</summary>
    public static string Spoken(string text)
        => !text.Contains("al-", StringComparison.Ordinal) ? text
            : WithArticles(text, w => w, sun => "a" + sun + "-", "al-");

    private static string HebrewWithArticles(string latin)
        => WithArticles(latin, ToHebrew, _ => "א-", "אל-");

    private static string CyrillicWithArticles(string latin)
        => WithArticles(latin, ToCyrillic, sun => "а" + ToCyrillic(sun) + "-", "аль-");

    /// <summary>Transliterates the text between articles with <paramref name="word"/> and renders each article itself.</summary>
    private static string WithArticles(string latin, Func<string, string> word, Func<string, string> sun, string moon)
    {
        var sb = new StringBuilder(latin.Length * 2);
        int last = 0;
        foreach (System.Text.RegularExpressions.Match m in Article.Matches(latin))
        {
            sb.Append(word(latin.Substring(last, m.Index - last)));
            last = m.Index + m.Length;
            var letter = SunLetter(latin.Substring(last));
            sb.Append(letter is null ? moon : sun(letter.ToLowerInvariant()));
        }
        sb.Append(word(latin.Substring(last)));
        return sb.ToString();
    }

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

    /// <summary>
    /// Morphemes are transliterated one at a time, so a fused compound can carry a final-form
    /// letter mid-word ("Storm"+"bury"); this turns those back into their regular forms.
    /// </summary>
    public static string NormalizeInnerFinals(string s)
    {
        if (!IsHebrew(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool inner = i + 1 < s.Length && s[i + 1] is >= '\u05D0' and <= '\u05EA';
            sb.Append(inner ? c switch { 'ם' => 'מ', 'ן' => 'נ', 'ף' => 'פ', 'ץ' => 'צ', 'ך' => 'כ', _ => c } : c);
        }
        return sb.ToString();
    }

    /// <summary>Runs <paramref name="word"/> over each ASCII-letter run, keeping separators and word capitalization.</summary>
    private static string Words(string text, Func<string, string> word)
    {
        var sb = new StringBuilder(text.Length * 2);
        int i = 0;
        while (i < text.Length)
        {
            if (!IsAsciiLetter(text[i])) { sb.Append(text[i++]); continue; }
            int j = i;
            while (j < text.Length && IsAsciiLetter(text[j])) j++;
            var src = text.Substring(i, j - i);
            var outp = word(src.ToLowerInvariant());
            if (char.IsUpper(src[0]) && outp.Length > 0) outp = char.ToUpperInvariant(outp[0]) + outp.Substring(1);
            sb.Append(outp);
            i = j;
        }
        return sb.ToString();
    }

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    private static string HebrewWord(string w)
    {
        var sb = new StringBuilder();
        char prevConsonant = '\0';
        int i = 0;
        while (i < w.Length)
        {
            bool first = i == 0;
            if (i + 2 <= w.Length && HebrewPairs.TryGetValue(w.Substring(i, 2), out var pair))
            {
                // A lone yod pair ("ea", "ee", "ie") at the start of a word still needs its alef.
                sb.Append(first && pair == "י" ? "אי" : pair);
                prevConsonant = '\0';
                i += 2;
                continue;
            }
            char c = w[i];
            char next = i + 1 < w.Length ? w[i + 1] : '\0';
            bool last = i == w.Length - 1;
            switch (c)
            {
                // Modern Hebrew leaves short a/e unwritten inside a name ("Karim" -> קרים);
                // initially they need an alef, and a final "a" is spelled with he ("Dana" -> דנה).
                case 'a': sb.Append(first ? "א" : last ? "ה" : ""); prevConsonant = '\0'; break;
                case 'e': sb.Append(first ? "א" : ""); prevConsonant = '\0'; break;
                case 'i': sb.Append(first ? "אי" : "י"); prevConsonant = '\0'; break;
                case 'o': sb.Append(first ? "או" : "ו"); prevConsonant = '\0'; break;
                case 'u': sb.Append(first ? "או" : "ו"); prevConsonant = '\0'; break;
                case 'y':
                    sb.Append(first && next != '\0' && !IsVowel(next) ? "אי" : "י");
                    prevConsonant = '\0';
                    break;
                case 'c':
                    if (prevConsonant == 'c') break;
                    sb.Append(next is 'e' or 'i' or 'y' ? "ס" : "ק");
                    prevConsonant = 'c';
                    break;
                case 'v' when last && i > 0:
                    sb.Append("ב"); prevConsonant = '\0'; break;   // Slavic-style final v: Yaroslav -> ירוסלב
                case 'v' when first || IsVowel(next):
                case 'w' when first || IsVowel(next):
                    sb.Append("ו"); prevConsonant = '\0'; break;
                case 'h' when last && i > 0 && IsVowel(w[i - 1]):
                    sb.Append("ה"); prevConsonant = 'h'; break;
                default:
                    if (HebrewConsonants.TryGetValue(c, out var s))
                    {
                        // Doubled consonants ("ll", "ss") are written once.
                        if (prevConsonant != c)
                        {
                            if (last) s = c switch { 'n' => "ן", 'm' => "ם", 'f' => "ף", _ => s };
                            sb.Append(s);
                        }
                        prevConsonant = c;
                    }
                    else sb.Append(c);
                    break;
            }
            i++;
        }
        return sb.ToString();
    }

    private static string CyrillicWord(string w)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < w.Length)
        {
            if (i + 2 <= w.Length && CyrillicPairs.TryGetValue(w.Substring(i, 2), out var pair))
            {
                sb.Append(pair);
                i += 2;
                continue;
            }
            char c = w[i];
            char next = i + 1 < w.Length ? w[i + 1] : '\0';
            bool prevVowel = i > 0 && IsVowel(w[i - 1]);
            if (c == 'c') sb.Append(next is 'e' or 'i' or 'y' ? "с" : "к");
            else if (c == 'y') sb.Append(prevVowel ? "й" : "и");
            else if (CyrillicSingles.TryGetValue(c, out var s)) sb.Append(s);
            else sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
