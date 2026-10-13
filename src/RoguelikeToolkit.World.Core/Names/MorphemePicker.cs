using RoguelikeToolkit.World.Core.Primitives;
using static RoguelikeToolkit.World.Core.Names.NameText;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Weighted, gender-aware and repetition-avoiding morpheme selection, plus the <see cref="RootSet"/> stutter guard.</summary>
internal static class MorphemePicker
{
    /// <summary>+1 masculine, -1 feminine, 0 either. Tags win; the older "(fem)"/"(masc)" gloss marker still works.</summary>
    internal static int GenderOf(MorphemeYaml m)
    {
        foreach (var t in m.Tags)
        {
            if (t.Equals("fem", StringComparison.OrdinalIgnoreCase)) return -1;
            if (t.Equals("masc", StringComparison.OrdinalIgnoreCase)) return 1;
        }
        if (m.Gloss.Contains("(fem", StringComparison.OrdinalIgnoreCase)) return -1;
        if (m.Gloss.Contains("(masc", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    internal static bool HasTag(MorphemeYaml m, string tag)
        => m.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Entries usable for the requested gender; falls back to the whole list when that leaves nothing.</summary>
    internal static List<MorphemeYaml> ForGender(List<MorphemeYaml> list, bool feminine)
    {
        var pool = list.Where(m => GenderOf(m) != (feminine ? 1 : -1)).ToList();
        return pool.Count > 0 ? pool : list;
    }

    /// <summary>
    /// A compatible second stem: not a repeat of the first, and not sharing its meaning
    /// ("Eagle"+"eagle-eyed" or "Blood"+"blood-sworn" is a stutter, not a name).
    /// </summary>
    internal static MorphemeYaml? PickSecond(ref Rng rng, List<MorphemeYaml> list, HashSet<string> tokens, MorphemeYaml stem)
    {
        var stemWords = NameThemes.Words(stem.Gloss).ToHashSet();
        MorphemeYaml? last = null;
        for (int i = 0; i < 8; i++)
        {
            var cand = PickBiased(ref rng, list, tokens);
            if (cand is null) return null;
            last = cand;
            // "Kindle"+"kindler", "Storm"+"storm": the same root twice is a stutter.
            if (SharesRoot(cand.Form, stem.Form) || SharesRoot(cand.Latin, stem.Latin)) continue;
            if (NameThemes.Words(cand.Gloss).Any(stemWords.Contains)) continue;
            return cand;
        }
        // Every draw clashed: better a plain single stem than a stuttering compound.
        return list.Count <= 1 ? last : null;
    }

    /// <summary>
    /// Roots already in a name, in every spelling that can surface: the authored form, its Latin
    /// and fusable base ("Пётр" fuses as "Петр", so "Петрович" and "Петров" collide), and its
    /// Hebrew and Cyrillic renderings, where distinct forms can merge ("Peng" and "Feng" are both
    /// "פנג"). Every script is checked whatever the requested one, so a name makes the same picks
    /// in every script: the same character, rendered differently.
    /// </summary>
    internal sealed class RootSet
    {
        private readonly List<string> _roots = new();
        private readonly List<string> _markers = new();

        public void Add(MorphemeYaml m)
        {
            _roots.AddRange(Spellings(m));
            if (!string.IsNullOrWhiteSpace(m.Base))
                _roots.AddRange(Spellings(new MorphemeYaml { Form = m.Base, Latin = m.BaseLatin }));
        }

        /// <summary>
        /// A fused patronymic marker is a word of the name too ("-spark", "-gust"). Markers are
        /// suffixes rather than roots, so they only clash when equal or when a 4+ letter one is
        /// contained: "Gust"/"gustess" does, a short "ael" inside "Caeli" does not.
        /// </summary>
        public void AddMarker(CultureData table, bool feminine)
        {
            var native = feminine ? table.PatronymicDaughter : table.PatronymicSon;
            var latin = feminine ? table.PatronymicDaughterLatin : table.PatronymicSonLatin;
            foreach (var script in AllScripts)
            {
                var s = Transliterator.RenderToken(native, latin, script);
                if (!string.IsNullOrWhiteSpace(s)) _markers.Add(s.Trim());
            }
        }

        public bool Clashes(MorphemeYaml m)
        {
            foreach (var s in Spellings(m))
            {
                foreach (var r in _roots)
                    if (SharesRoot(s, r)) return true;
                foreach (var k in _markers)
                    if (s.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                        (k.Length >= 4 && s.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Length >= 4 && k.Contains(s, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Is <paramref name="m"/> ruled out as a given or parent stem because it repeats the table's
    /// fused patronymic marker for this gender ("Spark" beside "-spark")? Themes use this to stay honest.
    /// </summary>
    internal static bool BlockedByMarker(CultureData table, MorphemeYaml m, bool feminine)
    {
        if (!table.Patronymic || table.PatronymicPrefix) return false;
        var roots = new RootSet();
        roots.AddMarker(table, feminine);
        return roots.Clashes(m);
    }

    internal static readonly NameScript[] AllScripts = Enum.GetValues<NameScript>();

    internal static IEnumerable<string> Spellings(MorphemeYaml m)
    {
        foreach (var script in AllScripts)
            yield return Display(m, script);
    }

    /// <summary>Biased pick among entries that repeat nothing in the name; null when every entry would.</summary>
    internal static MorphemeYaml? PickAvoiding(ref Rng rng, List<MorphemeYaml> list, HashSet<string> tokens, RootSet used)
    {
        var fresh = list.Where(m => !used.Clashes(m)).ToList();
        return fresh.Count == 0 ? null : PickBiased(ref rng, fresh, tokens);
    }

    /// <summary>Adds an optional slot (epithet), skipped when every candidate repeats a root already in the name.</summary>
    internal static void AddPickBiased(ref Rng rng, List<NamePart> parts, List<MorphemeYaml> list, string role,
        HashSet<string> tokens, NameScript script, RootSet used)
    {
        var m = PickAvoiding(ref rng, list, tokens, used);
        if (m is null) return;
        used.Add(m);
        parts.Add(new NamePart { Form = Cap(Display(m, script)), Gloss = m.Gloss, Role = role });
    }

    internal static MorphemeYaml? Pick(ref Rng rng, List<MorphemeYaml> list)
        => list.Count == 0 ? null : list[(int)rng.NextUInt((uint)list.Count)];

    /// <summary>Uniform pick when no inspirations; otherwise weight toward matching morphemes.</summary>
    internal static MorphemeYaml? PickBiased(ref Rng rng, List<MorphemeYaml> list, HashSet<string> tokens)
        => tokens.Count == 0 ? Pick(ref rng, list) : WeightedPick(ref rng, list, m => 1 + 5 * CountMatches(m, tokens));

    internal static int CountMatches(MorphemeYaml m, HashSet<string> tokens)
    {
        int n = 0;
        foreach (var t in tokens)
            if (NameThemes.Matches(m, t)) n++;
        return n;
    }

    /// <summary>Requested inspiration tokens confirmed by the emitted parts (honest metadata).</summary>
    internal static string[] MatchedTokens(List<NamePart> parts, HashSet<string> tokens)
        => tokens.Where(t => parts.Any(p => NameThemes.Matches(p, t))).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    /// <summary>Weighted pick; weights of 0 are excluded unless every weight is 0 (then the pick is uniform).</summary>
    internal static MorphemeYaml? WeightedPick(ref Rng rng, List<MorphemeYaml> list, Func<MorphemeYaml, int> weight)
    {
        if (list.Count == 0) return null;
        var w = list.Select(m => Math.Max(0, weight(m))).ToArray();
        int total = w.Sum();
        if (total == 0) return list[(int)rng.NextUInt((uint)list.Count)];
        int roll = (int)rng.NextUInt((uint)total);
        for (int i = 0; i < list.Count; i++)
        {
            roll -= w[i];
            if (roll < 0) return list[i];
        }
        return list[^1];
    }
}
