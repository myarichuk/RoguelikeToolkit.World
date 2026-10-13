using static RoguelikeToolkit.World.Core.Names.NameText;
using static RoguelikeToolkit.World.Core.Names.PersonNameBuilder;
using static RoguelikeToolkit.World.Core.Names.PlaceNameBuilder;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Deterministic fantasy/human name generators over YAML data tables.</summary>
public static class FantasyNameGenerator
{
    private static NameDataStore? _configured;

    public static void Configure(NameDataStore store) => _configured = store;
    public static NameDataStore Shared => _configured ??= NameDataStore.Load();

    /// <summary>
    /// Shipped race-to-table map. Anything absent here resolves by table Key
    /// (or alias), so a YAML-only drop like Races/troll.yaml works with no code.
    /// </summary>
    private static readonly Dictionary<string, string> RaceTables = new(StringComparer.OrdinalIgnoreCase)
    {
        [FantasyRaces.Dwarf] = "dwarven",
        [FantasyRaces.Halfling] = "smallfolk",
        [FantasyRaces.Gnome] = "gnomish",
        [FantasyRaces.Orc] = "orcish",
        [FantasyRaces.HalfElf] = "elf_high",
        [FantasyRaces.HalfOrc] = "orcish",
        [FantasyRaces.Demon] = "demonic",
        [FantasyRaces.Angel] = "angelic",
    };

    /// <summary>Point a race name at a table key at runtime (mods, tests).</summary>
    public static void RegisterRaceTable(string race, string tableKey) => RaceTables[race] = tableKey;

    /// <summary>
    /// Every race key a caller may pass: the coded list, mapped races, and any
    /// YAML-added non-culture table (so a drop-in troll.yaml shows up).
    /// </summary>
    public static IEnumerable<string> AvailableRaces(NameDataStore? store = null)
        => FantasyRaces.All.Concat(RaceTables.Keys)
            .Concat((store ?? Shared).Tables.Keys.Where(k => !IsCultureKey(k)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Human culture keys: the coded list plus any YAML-added human_*/fantasy_common
    /// tables, so a YAML-only culture drop shows up in UIs with no code change.
    /// </summary>
    public static IEnumerable<string> AvailableCultures(NameDataStore? store = null)
        => Cultures.All.Concat((store ?? Shared).Tables.Keys.Where(IsCultureKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    private static bool IsCultureKey(string key)
        => key.Equals(Cultures.FantasyCommon, StringComparison.OrdinalIgnoreCase) ||
           key.StartsWith("human_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One flat, UI-ready list of what the generator can produce: each human
    /// culture plus each non-human table, with the race/culture pair to pass in.
    /// A picker over this list replaces separate race and culture pickers (culture
    /// only ever mattered for humans and half-bloods).
    /// </summary>
    public static IReadOnlyList<NameStyle> Styles(NameDataStore? store = null)
    {
        store ??= Shared;
        var styles = new List<NameStyle>();
        foreach (var (key, table) in store.Tables)
        {
            if (IsCultureKey(key))
            {
                styles.Add(new NameStyle(FantasyRaces.Human, key, key, LabelFor(table)));
                continue;
            }
            // Reverse-map shipped aliases (dwarven -> dwarf); unmapped keys are their own race.
            var race = RaceTables.FirstOrDefault(kv =>
                kv.Value.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                !kv.Key.StartsWith("half_", StringComparison.OrdinalIgnoreCase)).Key ?? key;
            styles.Add(new NameStyle(race, Cultures.FantasyCommon, key, LabelFor(table)));
        }
        foreach (var half in new[] { FantasyRaces.HalfElf, FantasyRaces.HalfOrc })
        {
            if (RaceTables.TryGetValue(half, out var t) && store.Tables.ContainsKey(t))
                styles.Add(new NameStyle(half, Cultures.FantasyCommon, t, Title(half)));
        }
        return styles
            .OrderBy(s => s.Race.Equals(FantasyRaces.Human, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => s.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string LabelFor(CultureData t)
        => string.IsNullOrWhiteSpace(t.DisplayName) ? Title(t.Key) : t.DisplayName;

    private static string Title(string key)
        => string.Join(" ", key.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(Cap));

    /// <summary>Inspiration themes that actually bias this table's person names.</summary>
    public static IReadOnlyList<string> PersonThemes(
        string race, string culture, NameDataStore? store = null, bool? feminine = null)
    {
        store ??= Shared;
        bool half = race.Equals(FantasyRaces.HalfElf, StringComparison.OrdinalIgnoreCase) ||
                    race.Equals(FantasyRaces.HalfOrc, StringComparison.OrdinalIgnoreCase);
        return NameThemes.ForPeople(store.Get(TableKey(race, culture, store)), feminine, borrowsHumanFamily: half);
    }

    /// <summary>Inspiration themes that actually bias this table's place names.</summary>
    public static IReadOnlyList<string> PlaceThemes(string race, string culture, NameDataStore? store = null)
    {
        store ??= Shared;
        return NameThemes.ForPlaces(store.Get(TableKey(race, culture, store)), store.Get(Cultures.FantasyCommon));
    }

    public static string TableKey(string race, string culture, NameDataStore? store = null)
    {
        store ??= Shared;
        if (string.IsNullOrWhiteSpace(race)) race = FantasyRaces.Human;
        if (string.IsNullOrWhiteSpace(culture)) culture = Cultures.FantasyCommon;
        if (race.Equals(FantasyRaces.Human, StringComparison.OrdinalIgnoreCase))
            return store.ResolveCulture(culture, Cultures.All).Key;
        // Non-human names come from the race table, but resolve the culture anyway
        // so a typo'd culture fails fast instead of being silently ignored.
        store.ResolveCulture(culture, Cultures.All);
        if (RaceTables.TryGetValue(race, out var mapped))
            return store.ResolveRace(mapped, RaceTables.Keys).Key;
        return store.ResolveRace(race, RaceTables.Keys).Key;
    }

    public static string DefaultLineage(string race, string culture, NameDataStore? store = null)
        => NormalizeLineage((store ?? Shared).Get(TableKey(race, culture, store)).LineageDefault);

    public static string NormalizeLineage(string? lineage) => lineage?.Trim().ToLowerInvariant() switch
    {
        Lineages.Matrilineal => Lineages.Matrilineal,
        Lineages.Bilineal => Lineages.Bilineal,
        _ => Lineages.Patrilineal,
    };

    public static GeneratedName GeneratePerson(
        string race, string culture, int seed,
        string? lineage = null, bool feminine = false, int salt = 0,
        string? overrideDir = null, IEnumerable<string>? inspiredBy = null,
        NameScript script = NameScript.Native)
    {
        var store = overrideDir is null ? Shared : NameDataStore.Load(overrideDir);
        var table = store.Get(TableKey(race, culture, store));
        var tokens = NameThemes.Tokenize(inspiredBy);
        return FirstGood(store, table, script,
            attempt => BuildPerson(store, race, culture, seed, lineage, feminine, salt + attempt * 100003, tokens, script));
    }

    public static GeneratedName GeneratePlace(
        string kind, PlaceContext ctx, string race, string culture,
        int seed, int salt = 0, string? overrideDir = null, IEnumerable<string>? inspiredBy = null,
        NameScript script = NameScript.Native)
    {
        var store = overrideDir is null ? Shared : NameDataStore.Load(overrideDir);
        var table = store.Get(TableKey(race, culture, store));
        var tokens = NameThemes.Tokenize(inspiredBy);
        return FirstGood(store, table, script,
            attempt => BuildPlace(store, kind, ctx, race, culture, seed, salt + attempt * 100003, tokens, script));
    }

    /// <summary>
    /// Rerolls (deterministically, by salt) until a name is free of banned words and reads cleanly
    /// per <see cref="NameLint"/>. Most stutters are prevented while picking; this catches the rest,
    /// which only appear once fragments are fused and rendered ("Twisk"+"whisker" in Cyrillic is
    /// "Твисквискер"). If nothing passes, a ban-free name beats a clean banned one. Picks are
    /// script-independent; only these rare rerolls (like a ban that bites in one script) differ.
    /// </summary>
    private static GeneratedName FirstGood(NameDataStore store, CultureData table, NameScript script, Func<int, GeneratedName> build)
    {
        GeneratedName? first = null, firstAllowed = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var name = build(attempt);
            first ??= name;
            if (HitsBanned(store, table, name.Text, script)) continue;
            firstAllowed ??= name;
            if (NameLint.Check(name).Count == 0) return name;
        }
        return firstAllowed ?? first!;
    }

    private static bool HitsBanned(NameDataStore store, CultureData table, string text, NameScript script)
    {
        // Only the generating table's bans plus fantasy_common's apply: a ban
        // like angelic/gabriel must not veto a legitimate Hebrew "Gabriel".
        // In transliterated scripts the bans are transliterated too, so a ban
        // still catches its own rendering ("gimli" -> "גימלי").
        var lower = text.ToLowerInvariant();
        var bans = table.Banned.Concat(store.Get(Cultures.FantasyCommon).Banned)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (script is NameScript.Hebrew or NameScript.Cyrillic)
            bans = bans.SelectMany(b => new[] { b, Transliterator.Transliterate(b, script) });
        return bans.Any(b => lower.Contains(b.ToLowerInvariant()));
    }
}
