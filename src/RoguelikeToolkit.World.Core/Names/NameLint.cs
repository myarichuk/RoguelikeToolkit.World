using System.Text.RegularExpressions;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Mechanical checks for names that read as mistakes: stutters, repeated roots across
/// parts, broken spacing, Hebrew final letters mid-word. The generator is built to avoid
/// all of these; tests assert it over every style and script, and the name tester flags
/// any that slip through (usually from a hand-edited YAML table).
/// </summary>
public static class NameLint
{
    private static readonly Regex TripleLetter = new(@"(\p{L})\1\1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // "Gustgust", "Kindlekindler": a 4+ letter chunk twice in a row inside one word.
    private static readonly Regex DoubledChunk = new(@"(\p{L}{4,})\1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HebrewFinalMidWord = new("[םןףץך][א-ת]", RegexOptions.CultureInvariant);

    /// <summary>Human-readable problems with <paramref name="name"/>; empty when it reads cleanly.</summary>
    public static IReadOnlyList<string> Check(GeneratedName name)
    {
        var problems = new List<string>();
        var t = name.Text;
        if (string.IsNullOrWhiteSpace(t))
        {
            problems.Add("empty name");
            return problems;
        }
        if (t != t.Trim() || t.Contains("  ")) problems.Add("stray spaces");
        if (TripleLetter.IsMatch(t)) problems.Add("same letter three times");
        if (DoubledChunk.Match(t) is { Success: true } m) problems.Add($"stutter '{m.Value}'");
        if (HebrewFinalMidWord.IsMatch(t)) problems.Add("Hebrew final letter mid-word");

        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < words.Length; i++)
            if (words[i].Equals(words[i - 1], StringComparison.OrdinalIgnoreCase))
                problems.Add($"word '{words[i]}' twice");

        for (int i = 0; i < name.Parts.Count; i++)
            for (int j = i + 1; j < name.Parts.Count; j++)
                if (FantasyNameGenerator.SharesRoot(Root(name.Parts[i]), Root(name.Parts[j])))
                    problems.Add($"'{name.Parts[i].Form}' and '{name.Parts[j].Form}' repeat a root");
        return problems;
    }

    /// <summary>A part minus its prefix marker: "mac Bran" is compared as "Bran", so "Cormac mac Bran" is fine.</summary>
    private static string Root(NamePart p)
    {
        int sp = p.Form.IndexOf(' ');
        return sp > 0 && p.Role is NamePartRoles.Patronymic or NamePartRoles.Matronymic ? p.Form.Substring(sp + 1) : p.Form;
    }
}
