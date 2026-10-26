using System.Globalization;
using System.Text;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// English text helpers for the chronicle: slot modifiers and the final tidy (spacing, sentence capitals). All of them are
/// culture-invariant and allocation-light; none reads the current culture.
/// </summary>
internal static class ChronicleText
{
    /// <summary>
    /// Known modifiers: <c>cap</c> (capital first letter), <c>lower</c> (lower-case first letter), <c>pl</c> (plural of the last
    /// word), <c>a</c> (indefinite article), <c>poss</c> (possessive), <c>ord</c> (ordinal: 3 → third), <c>num</c> (number in
    /// words: 3 → three), <c>the</c> (drops a leading <c>the</c>, for a name used as an adjective).
    /// </summary>
    public static readonly IReadOnlyList<string> Modifiers = new[] { "cap", "lower", "pl", "a", "poss", "ord", "num", "the" };

    private static readonly string[] Small =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen",
        "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    };

    private static readonly string[] SmallOrd =
    {
        "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh",
        "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth",
    };

    private static readonly Dictionary<string, string> Irregular = new(StringComparer.OrdinalIgnoreCase)
    {
        ["man"] = "men", ["woman"] = "women", ["foot"] = "feet", ["tooth"] = "teeth", ["child"] = "children", ["ox"] = "oxen",
        ["mouse"] = "mice", ["dwarf"] = "dwarves", ["elf"] = "elves", ["wolf"] = "wolves", ["half"] = "halves", ["thief"] = "thieves",
        ["knife"] = "knives", ["life"] = "lives", ["wife"] = "wives", ["leaf"] = "leaves", ["sheaf"] = "sheaves", ["shelf"] = "shelves",
        ["staff"] = "staves", ["folk"] = "folk", ["kin"] = "kin", ["sheep"] = "sheep", ["deer"] = "deer", ["fish"] = "fish",
        ["kinsman"] = "kinsmen", ["swine"] = "swine", ["people"] = "peoples",
    };

    /// <summary>True for a known modifier name.</summary>
    public static bool IsModifier(string name)
    {
        foreach (var m in Modifiers)
            if (string.Equals(m, name, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Applies <paramref name="modifier"/> to <paramref name="text"/>.</summary>
    public static string Apply(string text, string modifier) => modifier switch
    {
        "cap" => Capitalize(text),
        "lower" => text.Length > 0 ? char.ToLowerInvariant(text[0]) + text.Substring(1) : text,
        "pl" => Plural(text),
        "a" => Article(text),
        "poss" => Possessive(text),
        "ord" => Ordinal(text),
        "num" => Number(text),
        "the" => text.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? text.Substring(4) : text,
        _ => text,
    };

    /// <summary>Upper-cases the first letter.</summary>
    public static string Capitalize(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsLetter(text[i])) continue;
            if (char.IsUpper(text[i])) return text;
            return text.Substring(0, i) + char.ToUpperInvariant(text[i]) + text.Substring(i + 1);
        }
        return text;
    }

    /// <summary>Plural of the last word (irregulars, then the usual English endings).</summary>
    public static string Plural(string text)
    {
        if (text.Length == 0) return text;
        int sp = text.LastIndexOf(' ');
        string head = sp < 0 ? string.Empty : text.Substring(0, sp + 1);
        string word = sp < 0 ? text : text.Substring(sp + 1);
        if (Irregular.TryGetValue(word, out var irr)) return head + MatchCase(word, irr);
        string lower = word.ToLowerInvariant();
        if (lower.EndsWith("man", StringComparison.Ordinal)) return head + word.Substring(0, word.Length - 3) + MatchCase(word.Substring(word.Length - 3), "men");
        if (lower.EndsWith("s", StringComparison.Ordinal) || lower.EndsWith("x", StringComparison.Ordinal) || lower.EndsWith("z", StringComparison.Ordinal)
            || lower.EndsWith("ch", StringComparison.Ordinal) || lower.EndsWith("sh", StringComparison.Ordinal))
            return head + word + "es";
        if (lower.Length > 1 && lower.EndsWith("y", StringComparison.Ordinal) && !IsVowel(lower[lower.Length - 2]))
            return head + word.Substring(0, word.Length - 1) + "ies";
        return head + word + "s";
    }

    /// <summary>Prefixes <c>a</c> or <c>an</c>.</summary>
    public static string Article(string text)
    {
        string t = text.TrimStart();
        if (t.Length == 0) return text;
        string lower = t.ToLowerInvariant();
        bool an = IsVowel(lower[0]);
        if (lower.StartsWith("uni", StringComparison.Ordinal) || lower.StartsWith("use", StringComparison.Ordinal)
            || lower.StartsWith("eu", StringComparison.Ordinal) || lower.StartsWith("one", StringComparison.Ordinal))
            an = false;
        if (lower.StartsWith("hour", StringComparison.Ordinal) || lower.StartsWith("honour", StringComparison.Ordinal)
            || lower.StartsWith("honest", StringComparison.Ordinal) || lower.StartsWith("heir", StringComparison.Ordinal))
            an = true;
        return (an ? "an " : "a ") + t;
    }

    /// <summary>Possessive: <c>'s</c>, or a bare apostrophe after a final s.</summary>
    public static string Possessive(string text)
    {
        if (text.Length == 0) return text;
        char last = text[text.Length - 1];
        return last == 's' || last == 'S' ? text + "'" : text + "'s";
    }

    /// <summary>An ordinal: words up to twentieth, then digits with a suffix.</summary>
    public static string Ordinal(string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0) return text;
        if (n < SmallOrd.Length) return SmallOrd[n];
        int tens = n % 100;
        string suffix = tens is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>A number in words up to twenty, then digits.</summary>
    public static string Number(string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0) return text;
        return n < Small.Length ? Small[n] : n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The final tidy: collapses runs of spaces, removes spaces before punctuation, doubles of punctuation left by an empty
    /// optional part, and capitalizes the first letter of every sentence.
    /// </summary>
    public static string Tidy(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char raw in text)
        {
            char c = raw == '\n' || raw == '\t' || raw == '\r' ? ' ' : raw;
            if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' ')) continue;
            if ((c == '.' || c == ',' || c == ';' || c == ':' || c == '!' || c == '?') && sb.Length > 0 && sb[sb.Length - 1] == ' ')
                sb.Length--;
            if (c == '.' && sb.Length > 0 && (sb[sb.Length - 1] == '.' || sb[sb.Length - 1] == '!' || sb[sb.Length - 1] == '?'))
                continue;
            if (c == ',' && sb.Length > 0 && sb[sb.Length - 1] == ',') continue;
            sb.Append(c);
        }
        while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
        bool start = true;
        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];
            if (start && char.IsLetter(c))
            {
                sb[i] = char.ToUpperInvariant(c);
                start = false;
            }
            else if (start && char.IsDigit(c)) start = false;
            if (c == '.' || c == '!' || c == '?')
                start = i + 1 >= sb.Length || sb[i + 1] == ' ';
        }
        return sb.ToString();
    }

    /// <summary>Splits rendered text into sentences (on <c>.</c>, <c>!</c> and <c>?</c> followed by a space or the end).</summary>
    public static IEnumerable<string> Sentences(string text)
    {
        int from = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '.' && c != '!' && c != '?') continue;
            int end = i + 1;
            while (end < text.Length && (text[end] == '"' || text[end] == '\'')) end++;
            if (end < text.Length && text[end] != ' ') continue;
            string s = text.Substring(from, end - from).Trim();
            if (s.Length > 0) yield return s;
            from = end;
            i = end - 1;
        }
        if (from < text.Length)
        {
            string tail = text.Substring(from).Trim();
            if (tail.Length > 0) yield return tail;
        }
    }

    /// <summary>Joins names as an English list (<c>A</c>, <c>A and B</c>, <c>A, B and C</c>).</summary>
    public static string JoinList(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return string.Empty;
        if (items.Count == 1) return items[0];
        var sb = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(i == items.Count - 1 ? " and " : ", ");
            sb.Append(items[i]);
        }
        return sb.ToString();
    }

    /// <summary><c>forest_ward</c> → <c>forest ward</c>.</summary>
    public static string Humanize(string key) => key.Replace('_', ' ').Trim();

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static string MatchCase(string like, string word)
        => like.Length > 0 && char.IsUpper(like[0]) ? Capitalize(word) : word;
}
