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
    public static readonly IReadOnlyList<string> All = new[]
    {
        "blood", "eagle", "wolf", "raven", "bear", "stone", "iron", "fire", "ember",
        "night", "dawn", "star", "moon", "river", "brook", "lake", "sea", "forest",
        "oak", "willow", "thorn", "mountain", "hill", "valley", "cave", "crystal",
        "storm", "frost", "snow", "ash", "hammer", "anvil", "forge", "oath",
        "ale", "song", "memory", "shadow", "light", "dark", "swift", "brave",
        "spear", "shield", "crown", "harvest", "mill", "honey", "hawk", "owl",
    };

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
