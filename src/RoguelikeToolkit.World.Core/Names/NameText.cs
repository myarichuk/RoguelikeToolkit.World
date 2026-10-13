namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Pure string helpers for name assembly: stable hashing, fusing fragments, glosses, capitalisation and script rendering.</summary>
internal static class NameText
{

    /// <summary>Stable FNV-1a hash: string.GetHashCode is process-random and must not seed generation.</summary>
    internal static int StableHash(string s)
    {
        unchecked
        {
            // Case-folded: "Dwarf" and "dwarf" are the same race and must seed the same stream.
            uint h = 2166136261;
            foreach (var c in s.ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return (int)h;
        }
    }

    /// <summary>Form of a morpheme in the requested script (see Transliterator).</summary>
    internal static string Display(MorphemeYaml m, NameScript script)
        => Transliterator.Render(m, script);

    /// <summary>The fusable base of a morpheme (see <see cref="MorphemeYaml.Base"/>) in the requested script.</summary>
    internal static string DisplayBase(MorphemeYaml m, NameScript script)
        => string.IsNullOrWhiteSpace(m.Base)
            ? Display(m, script)
            : Display(new MorphemeYaml { Form = m.Base, Latin = m.BaseLatin }, script);

    /// <summary>Patronymic marker (son/daughter) in the requested script.</summary>
    internal static string Marker(CultureData table, bool feminine, NameScript script)
        => Transliterator.RenderToken(
            feminine ? table.PatronymicDaughter : table.PatronymicSon,
            feminine ? table.PatronymicDaughterLatin : table.PatronymicSonLatin,
            script);

    /// <summary>
    /// Joins two name fragments. With <paramref name="dedupe"/> a doubled boundary *vowel*
    /// is dropped ("Eli"+"iel" -> "Eliel"); a doubled consonant is kept, because dropping it
    /// changes the word ("Shan"+"ning" is "Shanning", not "Shaning"; "Fred"+"dochter" is not
    /// "Fredochter"). Runs of three or more of any letter are always cut to two.
    /// </summary>
    internal static string Fuse(string a, string b, bool dedupe)
    {
        if (string.IsNullOrEmpty(b)) return a;
        if (string.IsNullOrEmpty(a)) return b;
        if (dedupe && b.Length > 1 && IsVowel(a[^1]) &&
            char.ToLowerInvariant(a[^1]) == char.ToLowerInvariant(b[0]))
            b = b.Substring(1);
        return Transliterator.NormalizeInnerFinals(CollapseRuns(a + b));
    }

    internal static bool IsVowel(char c) => "aeiouyаеёиоуыэюя".IndexOf(char.ToLowerInvariant(c)) >= 0;

    /// <summary>
    /// Do two fragments share a root? Any word of one (3+ letters) inside a word of the other,
    /// or a common prefix of 4+ letters: "Ash"/"Ashbringer", "Gust"/"gustess", "Howling"/"howl".
    /// Short words ("of", "mac") and article particles ("al-", "ash-") never count, so "ad-Din"
    /// and "al-Misri" coexist.
    /// </summary>
    internal static bool SharesRoot(string? a, string? b)
    {
        foreach (var x in RootWords(a))
            foreach (var y in RootWords(b))
            {
                var (s, l) = x.Length <= y.Length ? (x, y) : (y, x);
                if (l.Contains(s, StringComparison.OrdinalIgnoreCase)) return true;
                int p = 0;
                while (p < s.Length && char.ToLowerInvariant(s[p]) == char.ToLowerInvariant(l[p])) p++;
                if (p >= 4) return true;
            }
        return false;
    }

    internal static IEnumerable<string> RootWords(string? s)
        => string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(w => w.Substring(LeadingParticle(w)).Split(new[] { '-', '\'' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(w => w.Length >= 3);

    /// <summary>"eagle"+"wolf" -> "eagle-wolf"; multi-word glosses get a clear seam ("sky-blue / veiled in light").</summary>
    internal static string JoinGloss(string a, string b)
        => a.Contains(' ') || a.Contains('-') || b.Contains(' ') || b.Contains('-') ? $"{a} / {b}" : $"{a}-{b}";

    /// <summary>"son of" + "of Rome" must read "son of Rome", not "son of of Rome".</summary>
    internal static string StripOf(string gloss)
        => gloss.StartsWith("of ", StringComparison.OrdinalIgnoreCase) ? gloss.Substring(3) : gloss;

    /// <summary>
    /// Gloss of stem + surname affix: relational affixes read before the stem ("of the house of eagle",
    /// "man of boar"), place-word affixes after it ("wolf homestead", "elf clearing").
    /// </summary>
    internal static string AffixGloss(string stemGloss, string affixGloss)
        => affixGloss.TrimEnd().EndsWith(" of", StringComparison.OrdinalIgnoreCase)
            ? $"{affixGloss.TrimEnd()} {StripOf(stemGloss)}"
            : $"{stemGloss} {affixGloss}";

    internal static string CollapseRuns(string s)
    {
        if (s.Length < 3) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        int run = 0;
        for (int i = 0; i < s.Length; i++)
        {
            run = i > 0 && char.ToLowerInvariant(s[i]) == char.ToLowerInvariant(s[i - 1]) ? run + 1 : 1;
            if (run <= 2) sb.Append(s[i]);
        }
        return sb.ToString();
    }

    internal static bool TripleAtSeam(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        char x = char.ToLowerInvariant(a[^1]);
        if (char.ToLowerInvariant(b[0]) != x) return false;
        return (a.Length > 1 && char.ToLowerInvariant(a[^2]) == x) || (b.Length > 1 && char.ToLowerInvariant(b[1]) == x);
    }

    /// <summary>
    /// Welsh soft mutation of <paramref name="word"/>'s first consonant: p→b, t→d, c→g, b→f, d→dd,
    /// m→f, ll→l, rh→r, and g is lost (gwyn → wyn, coch → goch). "ch", "th", "ph", "ff", "dd" and
    /// vowels never mutate. After a word ending in s, t and d resist ("nos da", "Dinas Du").
    /// Capitalization of the first letter is kept.
    /// </summary>
    internal static string SoftMutate(string word, string? previous = null)
    {
        if (word.Length < 2) return word;
        var lower = word.ToLowerInvariant();
        if (lower.StartsWith("ch") || lower.StartsWith("th") || lower.StartsWith("ph") ||
            lower.StartsWith("ff") || lower.StartsWith("dd")) return word;
        bool afterS = !string.IsNullOrEmpty(previous) && char.ToLowerInvariant(previous[^1]) == 's';
        (int drop, string add) = lower switch
        {
            _ when lower.StartsWith("ll") => (2, "l"),
            _ when lower.StartsWith("rh") => (2, "r"),
            _ when lower[0] is 't' or 'd' && afterS => (0, ""),
            _ => lower[0] switch
            {
                'p' => (1, "b"),
                't' => (1, "d"),
                'c' => (1, "g"),
                'b' => (1, "f"),
                'd' => (1, "dd"),
                'm' => (1, "f"),
                'g' => (1, ""),
                _ => (0, ""),
            },
        };
        if (drop == 0) return word;
        var mutated = add + word.Substring(drop);
        return char.IsUpper(word[0]) ? Cap(mutated) : mutated;
    }

    internal static string Cap(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// <summary>Capitalizes a word but leaves a lowercase particle ("al-Bahr", "ash-Shams", "аль-Бахр") alone.</summary>
    internal static string CapWord(string s)
        => LeadingParticle(s) > 0 ? s : Cap(s);

    /// <summary>
    /// Length of a hyphenated lowercase particle opening <paramref name="word"/> ("al-", "ash-",
    /// "аль-"), 0 when there is none. Particles are grammar, not roots: "ash-Shams" must not
    /// clash with "Rashid", nor "al-" with "Alder".
    /// </summary>
    internal static int LeadingParticle(string word)
    {
        int h = word.IndexOf('-');
        if (h <= 0 || h > 3 || h == word.Length - 1) return 0;
        for (int i = 0; i < h; i++)
            if (!char.IsLower(word[i])) return 0;
        return h + 1;
    }
}
