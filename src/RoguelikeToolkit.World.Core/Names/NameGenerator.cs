using RoguelikeToolkit.World.Core.Lineage;
using RoguelikeToolkit.World.Core.Peoples;
using static RoguelikeToolkit.World.Core.Names.NameText;
using static RoguelikeToolkit.World.Core.Names.PersonNameBuilder;
using static RoguelikeToolkit.World.Core.Names.PlaceNameBuilder;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Deterministic fantasy/human name generators over YAML data tables.</summary>
public static class FantasyNameGenerator
{
    private static NameDataStore? _configured;

    /// <summary>Sets the store that <see cref="Shared"/> returns, replacing any loaded one. Not synchronized; call it at startup before generating.</summary>
    public static void Configure(NameDataStore store) => _configured = store;
    /// <summary>The store used when a call passes no <c>overrideDir</c> or store: the one given to <see cref="Configure"/>, else the shipped tables loaded lazily on first access (not thread-safe).</summary>
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
            .ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
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

    /// <summary>
    /// How a realm of <paramref name="form"/> founded by this people is named: the place kinds its base name is drawn from,
    /// and its title templates (<c>{n}</c> = the base name). The people's own table wins per form, then
    /// <c>fantasy_common</c>; empty lists when neither has the form (callers fall back to a bare name).
    /// </summary>
    public static (IReadOnlyList<string> Kinds, IReadOnlyList<string> Titles) RealmNaming(string race, string culture, string form, NameDataStore? store = null)
    {
        store ??= Shared;
        var table = store.Get(TableKey(race, culture, store));
        store.Tables.TryGetValue(Cultures.FantasyCommon, out var common);
        static List<string>? Pick(CultureData? t, Dictionary<string, List<string>>? map, string key)
            => t != null && map != null && map.TryGetValue(key, out var list) && list.Count > 0 ? list : null;
        var kinds = Pick(table, table.RealmNameKinds, form) ?? Pick(common, common?.RealmNameKinds, form) ?? new List<string>();
        var titles = (Pick(table, table.RealmTitles, form) ?? Pick(common, common?.RealmTitles, form) ?? new List<string>())
            .Where(t => t.Contains("{n}", StringComparison.Ordinal)).ToList();
        return (kinds, titles);
    }

    /// <summary>
    /// Resolves a race/culture pair to the key of the table that generates its names. Humans use the culture table; other races use their race table (shipped mapping first, else the race key itself).
    /// Blank race means human and blank culture means <see cref="Cultures.FantasyCommon"/>.
    /// </summary>
    /// <exception cref="UnknownNameKeyException">The race or culture is unknown; the culture is validated even for non-humans, where it is otherwise ignored.</exception>
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

    /// <summary>The lineage the race/culture table follows by default, normalized to one of the <see cref="Lineages"/> values. Throws <see cref="UnknownNameKeyException"/> for unknown keys.</summary>
    public static string DefaultLineage(string race, string culture, NameDataStore? store = null)
        => NormalizeLineage((store ?? Shared).Get(TableKey(race, culture, store)).LineageDefault);

    /// <summary>Maps a lineage string (trimmed, case-insensitive) to a <see cref="Lineages"/> constant; null, blank or unrecognised input yields <see cref="Lineages.Patrilineal"/>.</summary>
    public static string NormalizeLineage(string? lineage) => lineage?.Trim().ToLowerInvariant() switch
    {
        Lineages.Matrilineal => Lineages.Matrilineal,
        Lineages.Bilineal => Lineages.Bilineal,
        _ => Lineages.Patrilineal,
    };

    /// <summary>
    /// Generates a person name; the same arguments always produce the same name for a given set of tables.
    /// </summary>
    /// <param name="race">Race key (see <see cref="FantasyRaces"/>); blank means human.</param>
    /// <param name="culture">Culture key (see <see cref="Cultures"/>); only used for humans and half-bloods, but must still be valid.</param>
    /// <param name="seed">Deterministic seed, echoed in <see cref="GeneratedName.Seed"/>.</param>
    /// <param name="lineage">Overrides the table's default lineage; null or blank uses the default.</param>
    /// <param name="feminine">Selects feminine given names, patronymic marker and surname forms.</param>
    /// <param name="salt">Extra number mixed into the seed to get a different name for the same seed.</param>
    /// <param name="overrideDir">Directory of YAML tables to load instead of <see cref="Shared"/>; reloaded on every call, so avoid it in hot paths.</param>
    /// <param name="inspiredBy">Theme words that bias which morphemes are picked.</param>
    /// <param name="script">Script to render the name in.</param>
    /// <exception cref="UnknownNameKeyException">The race or culture has no table.</exception>
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

    /// <summary>
    /// Generates a mixed-ancestry person name from lineage shares and the owning
    /// culture's rule: a share at or above the culture's hypodescent threshold
    /// names after that folk alone (dominant strategy always does); otherwise the
    /// given name comes from the steepest share and the family/clan slot from the
    /// next one, with no <c>half_X</c> key. The same arguments always produce the
    /// same name for a given set of tables.
    /// </summary>
    /// <param name="ancestry">Lineage shares; the steepest share is dominant, ties keep declaration order.</param>
    /// <param name="culture">Owning culture key (see <see cref="PeoplesStore.Cultures"/>): supplies the hypodescent threshold, the mixed-naming strategy and the lineage default. Blank means human_common.</param>
    /// <param name="peoples">Folk/culture tables the owning culture is read from.</param>
    /// <param name="seed">Deterministic seed, echoed in <see cref="GeneratedName.Seed"/>.</param>
    /// <param name="lineage">Overrides the owning culture's default lineage; null or blank uses it.</param>
    /// <param name="feminine">Selects feminine given names, patronymic marker and surname forms.</param>
    /// <param name="salt">Extra number mixed into the seed to get a different name for the same seed.</param>
    /// <param name="overrideDir">Directory of YAML tables to load instead of <see cref="Shared"/>; reloaded on every call, so avoid it in hot paths.</param>
    /// <param name="inspiredBy">Theme words that bias which morphemes are picked.</param>
    /// <param name="script">Script to render the name in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ancestry"/> or <paramref name="peoples"/> is null.</exception>
    /// <exception cref="UnknownPeoplesKeyException">The culture is unknown.</exception>
    /// <exception cref="UnknownNameKeyException">A folk has no name table.</exception>
    public static GeneratedName GenerateMixedPerson(
        Ancestry ancestry, string culture, PeoplesStore peoples, int seed,
        string? lineage = null, bool feminine = false, int salt = 0,
        string? overrideDir = null, IEnumerable<string>? inspiredBy = null,
        NameScript script = NameScript.Native)
    {
        if (ancestry is null) throw new ArgumentNullException(nameof(ancestry));
        if (peoples is null) throw new ArgumentNullException(nameof(peoples));
        if (string.IsNullOrWhiteSpace(culture)) culture = "human_common";
        var owning = peoples.GetCulture(culture);
        var store = overrideDir is null ? Shared : NameDataStore.Load(overrideDir);
        // The owning culture's naming table voices single-folk renders; a clan
        // key like dwarven is itself a table, otherwise fall back to fantasy_common.
        var nameCulture = store.Tables.ContainsKey(owning.NamingTable) ? owning.NamingTable : Cultures.FantasyCommon;
        var effLineage = string.IsNullOrWhiteSpace(lineage) ? owning.LineageDefault : lineage;
        var dominant = ancestry.Dominant;
        var implied = ancestry.ResolveImpliedStatus(owning.DominantShareThreshold);
        if (!implied.Equals(Ancestry.Mixed, StringComparison.Ordinal)
            || owning.MixedNaming.Equals("dominant", StringComparison.OrdinalIgnoreCase))
            return GeneratePerson(dominant, nameCulture, seed,
                lineage: effLineage, feminine: feminine, salt: salt,
                overrideDir: overrideDir, inspiredBy: inspiredBy, script: script);
        var second = ancestry.Shares.Count > 1 ? ancestry.Shares[1].Folk : dominant;
        var dominantTable = store.Get(TableKey(dominant, nameCulture, store));
        var secondTable = store.Get(TableKey(second, nameCulture, store));
        var tokens = NameThemes.Tokenize(inspiredBy);
        return FirstGood(store, dominantTable, script,
            attempt => BuildMixed(store, dominantTable, secondTable,
                second.Equals(FantasyRaces.Human, StringComparison.OrdinalIgnoreCase),
                owning.LineageDefault, $"{dominant}+{second}", owning.Key,
                seed, lineage, feminine, salt + attempt * 100003, tokens, script));
    }

    /// <summary>
    /// Generates a place name of the given kind; the same arguments always produce the same name for a given set of tables.
    /// </summary>
    /// <param name="kind">One of <see cref="PlaceKinds"/>; any other value is treated as a village. This argument, not <see cref="PlaceContext.Kind"/>, selects the kind.</param>
    /// <param name="ctx">Surroundings the name should reflect.</param>
    /// <param name="race">Race key whose vocabulary is used; blank means human.</param>
    /// <param name="culture">Culture key; only used for humans, but must still be valid.</param>
    /// <param name="seed">Deterministic seed, echoed in <see cref="GeneratedName.Seed"/>.</param>
    /// <param name="salt">Extra number mixed into the seed to get a different name for the same seed.</param>
    /// <param name="overrideDir">Directory of YAML tables to load instead of <see cref="Shared"/>; reloaded on every call.</param>
    /// <param name="inspiredBy">Theme words that bias which morphemes are picked.</param>
    /// <param name="script">Script to render the name in.</param>
    /// <exception cref="UnknownNameKeyException">The race or culture has no table.</exception>
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
    /// Generates a hybrid place name for a conquest or settled-nomad city: the
    /// descriptor comes from the superstrate (ruling) table, the noun from the
    /// substrate (settled) table. The same arguments always produce the same name
    /// for a given set of tables.
    /// </summary>
    /// <param name="kind">One of <see cref="PlaceKinds"/>; any other value is treated as a village. This argument, not <see cref="PlaceContext.Kind"/>, selects the kind.</param>
    /// <param name="ctx">Surroundings the name should reflect.</param>
    /// <param name="superstrateRace">Race key whose descriptors name the place; blank means human.</param>
    /// <param name="superstrateCulture">Culture key for the superstrate; must still be valid for non-humans.</param>
    /// <param name="substrateRace">Race key whose nouns ground the place; blank means human.</param>
    /// <param name="substrateCulture">Culture key for the substrate; must still be valid for non-humans.</param>
    /// <param name="seed">Deterministic seed, echoed in <see cref="GeneratedName.Seed"/>.</param>
    /// <param name="salt">Extra number mixed into the seed to get a different name for the same seed.</param>
    /// <param name="overrideDir">Directory of YAML tables to load instead of <see cref="Shared"/>; reloaded on every call.</param>
    /// <param name="inspiredBy">Theme words that bias which morphemes are picked.</param>
    /// <param name="script">Script to render the name in.</param>
    /// <exception cref="UnknownNameKeyException">A race or culture has no table.</exception>
    public static GeneratedName GenerateHybridPlace(
        string kind, PlaceContext ctx, string superstrateRace, string superstrateCulture,
        string substrateRace, string substrateCulture,
        int seed, int salt = 0, string? overrideDir = null, IEnumerable<string>? inspiredBy = null,
        NameScript script = NameScript.Native)
    {
        if (string.IsNullOrWhiteSpace(superstrateRace)) superstrateRace = FantasyRaces.Human;
        if (string.IsNullOrWhiteSpace(superstrateCulture)) superstrateCulture = Cultures.FantasyCommon;
        if (string.IsNullOrWhiteSpace(substrateRace)) substrateRace = FantasyRaces.Human;
        if (string.IsNullOrWhiteSpace(substrateCulture)) substrateCulture = Cultures.FantasyCommon;
        var store = overrideDir is null ? Shared : NameDataStore.Load(overrideDir);
        var superstrate = store.Get(TableKey(superstrateRace, superstrateCulture, store));
        var substrate = store.Get(TableKey(substrateRace, substrateCulture, store));
        var tokens = NameThemes.Tokenize(inspiredBy);
        return FirstGood(store, superstrate, script,
            attempt => BuildHybridPlace(store, kind, ctx, superstrate, substrate,
                superstrateRace, superstrateCulture, seed, salt + attempt * 100003, tokens, script));
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
