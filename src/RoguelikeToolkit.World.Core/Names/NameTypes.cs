namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Fantasy ancestry keys. DarkElf is a generic folklore archetype, not Drow.
/// Plain strings on purpose: new folk (e.g. "troll") can be added with a
/// YAML-only drop, no code changes. See <see cref="Cultures"/> the same way.
/// </summary>
public static class FantasyRaces
{
    public const string Human = "human";
    public const string Dwarf = "dwarf";
    public const string Halfling = "halfling";
    public const string Gnome = "gnome";
    public const string ElfHigh = "elf_high";
    public const string ElfWood = "elf_wood";
    public const string ElfDark = "elf_dark";
    public const string Orc = "orc";
    public const string HalfElf = "half_elf";
    public const string HalfOrc = "half_orc";
    public const string Lizardfolk = "lizardfolk";
    public const string Dragonfolk = "dragonfolk";
    public const string Catfolk = "catfolk";
    public const string Dogfolk = "dogfolk";
    public const string Demon = "demon";
    public const string Angel = "angel";
    public const string ElementalFire = "elemental_fire";
    public const string ElementalWater = "elemental_water";
    public const string ElementalEarth = "elemental_earth";
    public const string ElementalAir = "elemental_air";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Human, Dwarf, Halfling, Gnome, ElfHigh, ElfWood, ElfDark, Orc,
        HalfElf, HalfOrc, Lizardfolk, Dragonfolk, Catfolk, Dogfolk,
        Demon, Angel, ElementalFire, ElementalWater, ElementalEarth, ElementalAir,
    };
}

/// <summary>Human-culture flavor keys (each is a YAML table key), plus FantasyCommon.</summary>
public static class Cultures
{
    public const string FantasyCommon = "fantasy_common";
    public const string EasternEuropean = "human_eastern";
    public const string WesternEuropean = "human_western";
    public const string Welsh = "human_welsh";
    public const string Irish = "human_irish";
    public const string Arabic = "human_arabic";
    public const string Chinese = "human_chinese";
    public const string Japanese = "human_japanese";
    public const string English = "human_english";
    public const string Russian = "human_russian";
    public const string Hebrew = "human_hebrew";

    public static readonly IReadOnlyList<string> All = new[]
    {
        FantasyCommon, EasternEuropean, WesternEuropean, Welsh, Irish, Arabic, Chinese, Japanese,
        English, Russian, Hebrew,
    };
}

/// <summary>
/// Which script a generated name renders in. <see cref="Native"/> and
/// <see cref="Latin"/> use the authored (or transliterated) table forms;
/// <see cref="Hebrew"/> and <see cref="Cyrillic"/> machine-transliterate any
/// Latin form via <see cref="Transliterator"/>, while morphemes already in the
/// target script (or carrying an explicit field) pass through untouched.
/// Glosses stay in English either way.
/// </summary>
public enum NameScript
{
    Native,
    Latin,
    Hebrew,
    Cyrillic,
}

/// <summary>Which parent's line the name follows. Caller-overridable per call.</summary>
public static class Lineages
{
    public const string Patrilineal = "patrilineal";
    public const string Matrilineal = "matrilineal";
    public const string Bilineal = "bilineal";
}

public static class PlaceKinds
{
    public const string Village = "village";
    public const string Town = "town";
    public const string City = "city";
    public const string River = "river";
    public const string Lake = "lake";
    public const string Mountain = "mountain";
    public const string Hill = "hill";
    public const string Valley = "valley";
    public const string Forest = "forest";
    public const string Hold = "hold";
    public const string Camp = "camp";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Village, Town, City, River, Lake, Mountain, Hill, Valley, Forest, Hold, Camp,
    };
}

public static class NamePartRoles
{
    public const string Given = "given";
    public const string Patronymic = "patronymic";
    public const string Matronymic = "matronymic";
    public const string Family = "family";
    public const string Clan = "clan";
    public const string Epithet = "epithet";
    public const string Descriptor = "descriptor";
    public const string Geography = "geography";
}

public sealed class NamePart
{
    public string Form { get; init; } = string.Empty;
    public string Gloss { get; init; } = string.Empty;
    public string Role { get; init; } = NamePartRoles.Given;
}

public sealed class NameProvenance
{
    public string Inspiration { get; init; } = "original";
    public string Note { get; init; } = string.Empty;
}

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

/// <summary>One entry in a UI "style" picker: a race+culture pair resolved to a single table.</summary>
public sealed record NameStyle(string Race, string Culture, string TableKey, string Label);

/// <summary>Rich name result: text plus full etymology metadata.</summary>
public sealed class GeneratedName
{
    public string Text { get; init; } = string.Empty;
    public string Race { get; init; } = FantasyRaces.Human;
    public string Culture { get; init; } = Cultures.FantasyCommon;
    public string Lineage { get; init; } = Lineages.Patrilineal;
    public NameProvenance Provenance { get; init; } = new();
    public IReadOnlyList<string> Inspirations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NamePart> Parts { get; init; } = Array.Empty<NamePart>();
    public int Seed { get; init; }
    public string Gloss => string.Join(" + ", Parts.Select(p => $"{p.Form} “{p.Gloss}”"));
    public override string ToString() => Text;
}

/// <summary>Map context a place name should reflect (water, woods, relief, resources).</summary>
public sealed class PlaceContext
{
    public bool NearWater { get; init; }
    public bool NearForest { get; init; }
    public bool DarkForest { get; init; }
    public bool Highland { get; init; }
    public bool Valley { get; init; }
    public bool HasDeposit { get; init; }
    public bool Dangerous { get; init; }
    public string Kind { get; init; } = PlaceKinds.Village;
}
