namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Closed vocabulary for the "inspired by" bias (e.g. orc name inspired by
/// blood, eagle). Free text is also accepted: it is tokenized and matched
/// against morpheme forms, glosses and tags, so the list stays open-ended.
/// Unknown tokens simply match nothing and generation falls back gracefully.
/// </summary>
public static class NameThemes
{
    /// <summary>
    /// Generic starter vocabulary, kept for callers with no data store at hand.
    /// UIs should prefer <see cref="ForPeople"/>/<see cref="ForPlaces"/>, which
    /// derive the list from the loaded YAML so every entry is guaranteed to bias something.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "blood", "eagle", "wolf", "raven", "bear", "stone", "iron", "fire", "ember",
        "night", "dawn", "star", "moon", "river", "brook", "lake", "sea", "forest",
        "oak", "willow", "thorn", "mountain", "hill", "valley", "cave", "crystal",
        "storm", "frost", "snow", "ash", "hammer", "anvil", "forge", "oath",
        "ale", "song", "memory", "shadow", "light", "dark", "swift", "brave",
        "spear", "shield", "crown", "harvest", "mill", "honey", "hawk", "owl",
    };

    /// <summary>Tags with a structural meaning; never offered or matched as themes.</summary>
    internal static readonly HashSet<string> ReservedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "masc", "fem", "solo", "bound", "nopatron",
        "village", "town", "city", "hold", "camp", "danger", "deposit", "high",
    };

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "who", "whose", "that", "with", "from", "for", "his", "her", "its", "one",
        "son", "daughter", "masc", "fem", "clan", "house", "family", "warband", "tribe", "place",
        "kin", "born", "sworn", "eyed", "hard", "like", "side", "human", "full", "very", "has",
        "are", "was", "not", "all", "any", "out", "near", "land", "enclosure", "homestead",
        "bearer", "keeper", "caller", "herald", "court", "maker", "hearted", "haired", "lord", "blooded",
        "footed", "browed", "backed", "tailed", "eater", "child", "father", "mother", "man", "men", "ones", "fall",
    };

    /// <summary>Lowercase alphabetic words (length >= 3) of a gloss.</summary>
    internal static IEnumerable<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var sb = new System.Text.StringBuilder();
        foreach (var c in text.ToLowerInvariant() + " ")
        {
            if (char.IsLetter(c)) { sb.Append(c); continue; }
            if (sb.Length >= 3) yield return sb.ToString();
            sb.Clear();
        }
    }

    /// <summary>
    /// Word-aware match of a token against a morpheme: whole gloss words (with a
    /// light stem allowance: "wolf" ~ "wolves" is not attempted, "scar" ~ "scarred" is),
    /// the form itself, or a non-structural tag. Never a blind substring ("ash" no longer hits "splash").
    /// </summary>
    internal static bool Matches(MorphemeYaml m, string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        foreach (var w in Words(m.Gloss))
            if (WordMatches(w, token)) return true;
        if (FormMatches(m.Form, token) || FormMatches(m.Latin, token)) return true;
        foreach (var tag in m.Tags)
            if (!ReservedTags.Contains(tag) && tag.Equals(token, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static bool WordMatches(string word, string token)
        => word.Equals(token, StringComparison.OrdinalIgnoreCase) ||
           (token.Length >= 4 && word.Length >= 4 &&
            (word.StartsWith(token, StringComparison.OrdinalIgnoreCase) ||
             token.StartsWith(word, StringComparison.OrdinalIgnoreCase)));

    private static bool FormMatches(string? form, string token)
        => !string.IsNullOrWhiteSpace(form) &&
           (form.Equals(token, StringComparison.OrdinalIgnoreCase) ||
            (token.Length >= 4 && form.Contains(token, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Does an emitted part confirm the token? Used for honest "inspired by" metadata.</summary>
    internal static bool Matches(NamePart p, string token)
        => Words(p.Gloss).Any(w => WordMatches(w, token)) || FormMatches(p.Form, token);

    /// <summary>Themes available for person names of a table (stems, clans, epithets, family names).</summary>
    /// <param name="table">The name table whose vocabulary is listed.</param>
    /// <param name="feminine">When set, only themes reachable for that gender are listed.</param>
    /// <param name="borrowsHumanFamily">Half-bloods take a human surname instead of the table's clans and family names.</param>
    public static IReadOnlyList<string> ForPeople(CultureData table, bool? feminine = null, bool borrowsHumanFamily = false)
    {
        List<MorphemeYaml> Usable(List<MorphemeYaml> l) => feminine is null
            ? l
            : l.Where(m => !m.Tags.Contains(feminine.Value ? "masc" : "fem", StringComparer.OrdinalIgnoreCase)).ToList();
        // A stem that repeats the fused patronymic marker ("Spark" beside "-spark") is never picked.
        List<MorphemeYaml> Stems(List<MorphemeYaml> l) => Usable(l).Where(m => feminine is null
            ? !(FantasyNameGenerator.BlockedByMarker(table, m, false) && FantasyNameGenerator.BlockedByMarker(table, m, true))
            : !FantasyNameGenerator.BlockedByMarker(table, m, feminine.Value)).ToList();
        // Only offer a theme from a slot the table can actually emit (an epithet list with EpithetChance 0 is dead data).
        var lists = new List<List<MorphemeYaml>> { Stems(table.GivenStems) };
        if (table.CompoundChance > 0) lists.Add(Stems(table.SecondStems));
        if (borrowsHumanFamily) { if (table.EpithetChance > 0) lists.Add(table.Epithets); return Collect(lists, includeTags: false); }
        if (table.ClanChance != 0) lists.Add(table.Clans);
        if (table.EpithetChance > 0) lists.Add(table.Epithets);
        // A clan roll that always fires leaves no room for the family slot.
        if (table.FamilyChance > 0 && !(table.ClanChance == 100 && table.Clans.Count > 0)) lists.Add(Usable(table.FamilyAffixes));
        return Collect(lists, includeTags: false);
    }

    /// <summary>Themes available for place names; <paramref name="common"/> is merged when the table uses place fallback.</summary>
    public static IReadOnlyList<string> ForPlaces(CultureData table, CultureData? common = null)
    {
        var lists = new List<List<MorphemeYaml>> { table.Descriptors, table.Nouns };
        if (common is not null && table.PlaceFallback) { lists.Add(common.Descriptors); lists.Add(common.Nouns); }
        return Collect(lists, includeTags: true);
    }

    private static IReadOnlyList<string> Collect(IEnumerable<List<MorphemeYaml>> lists, bool includeTags)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in lists)
            foreach (var m in list)
            {
                foreach (var w in Words(m.Gloss))
                    if (!Stopwords.Contains(w)) set.Add(w);
                if (includeTags)
                    foreach (var t in m.Tags)
                        if (!ReservedTags.Contains(t) && t.Length >= 3) set.Add(t.ToLowerInvariant());
            }
        return set.ToList();
    }

    /// <summary>Lowercase tokens from free text, split on common separators.</summary>
    public static HashSet<string> Tokenize(IEnumerable<string>? inspirations)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inspirations is null) return tokens;
        foreach (var raw in inspirations)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            foreach (var t in raw.Split(new[] { ' ', ',', ';', '/', '_', '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = t.Trim().ToLowerInvariant();
                // Fragments shorter than 3 chars match almost every gloss; drop them.
                if (clean.Length >= 3) tokens.Add(clean);
            }
        }
        return tokens;
    }
}
