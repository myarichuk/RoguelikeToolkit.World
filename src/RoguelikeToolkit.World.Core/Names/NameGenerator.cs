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

    public static IEnumerable<string> AvailableRaces(NameDataStore? store = null)
        => (store ?? Shared).Tables.Keys.Concat(RaceTables.Keys)
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

    public static string TableKey(string race, string culture, NameDataStore? store = null)
    {
        store ??= Shared;
        if (race.Equals(FantasyRaces.Human, StringComparison.OrdinalIgnoreCase))
            return store.ResolveCulture(string.IsNullOrWhiteSpace(culture) ? Cultures.FantasyCommon : culture, Cultures.All).Key;
        // Non-human names come from the race table, but resolve the culture anyway
        // so a typo'd culture fails fast instead of being silently ignored.
        store.ResolveCulture(string.IsNullOrWhiteSpace(culture) ? Cultures.FantasyCommon : culture, Cultures.All);
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

    /// <summary>Patronymic marker (son/daughter) in the requested script.</summary>
    private static string Marker(CultureData table, bool feminine, NameScript script)
        => Transliterator.RenderToken(
            feminine ? table.PatronymicDaughter : table.PatronymicSon,
            feminine ? table.PatronymicDaughterLatin : table.PatronymicSonLatin,
            script);

    public static GeneratedName GeneratePerson(
        string race, string culture, int seed,
        string? lineage = null, bool feminine = false, int salt = 0,
        string? overrideDir = null, IEnumerable<string>? inspiredBy = null,
        NameScript script = NameScript.Native)
    {
        var store = overrideDir is null ? Shared : NameDataStore.Load(overrideDir);
        var table = store.Get(TableKey(race, culture, store));
        var tokens = NameThemes.Tokenize(inspiredBy);
        GeneratedName fallback = BuildPerson(store, race, culture, seed, lineage, feminine, salt, tokens, script);
        if (!HitsBanned(store, table, fallback.Text, script)) return fallback;
        for (int attempt = 1; attempt < 5; attempt++)
        {
            var name = BuildPerson(store, race, culture, seed, lineage, feminine, salt + attempt * 100003, tokens, script);
            if (!HitsBanned(store, table, name.Text, script)) return name;
        }
        return fallback;
    }

    public static GeneratedName GeneratePlace(
        string kind, PlaceContext ctx, string race, string culture,
        int seed, int salt = 0, string? overrideDir = null, IEnumerable<string>? inspiredBy = null,
        NameScript script = NameScript.Native)
    {
        var store = overrideDir is null ? Shared : NameDataStore.Load(overrideDir);
        var table = store.Get(TableKey(race, culture, store));
        var tokens = NameThemes.Tokenize(inspiredBy);
        GeneratedName fallback = BuildPlace(store, kind, ctx, race, culture, seed, salt, tokens, script);
        if (!HitsBanned(store, table, fallback.Text, script)) return fallback;
        for (int attempt = 1; attempt < 5; attempt++)
        {
            var name = BuildPlace(store, kind, ctx, race, culture, seed, salt + attempt * 100003, tokens, script);
            if (!HitsBanned(store, table, name.Text, script)) return name;
        }
        return fallback;
    }

    private static GeneratedName BuildPerson(
        NameDataStore store, string race, string culture,
        int seed, string? lineage, bool feminine, int salt, HashSet<string> tokens, NameScript script)
    {
        var table = store.Get(TableKey(race, culture, store));
        var rng = Rng.Create(seed, salt ^ (StableHash(race) * 7919) ^ (StableHash(culture) * 4789));
        var effLineage = NormalizeLineage(string.IsNullOrWhiteSpace(lineage) ? table.LineageDefault : lineage);
        var parts = new List<NamePart>();

        // Given name: stem (+ second stem for compound cultures), biased by inspirations.
        var stem = PickBiased(ref rng, table.GivenStems, tokens) ?? new MorphemeYaml { Form = "Ash", Gloss = "ash tree" };
        var second = table.SecondStems.Count > 0 && rng.NextUInt(100) < 65 ? PickBiased(ref rng, table.SecondStems, tokens) : null;
        var stemShown = Cap(Display(stem, script));
        string given;
        if (second is null)
        {
            given = stemShown;
        }
        else
        {
            var secondShown = Display(second, script);
            // Particles that are words of their own ("al-Din") join with a space,
            // plain stem endings ("gar", "mir") fuse ("Thrungar", "Vladimir").
            given = secondShown.Contains(' ') || secondShown.Contains('-')
                ? stemShown + " " + secondShown
                : stemShown + secondShown;
        }
        var givenGloss = second is null ? stem.Gloss : $"{stem.Gloss}-{second.Gloss}";
        parts.Add(new NamePart { Form = given, Gloss = givenGloss, Role = NamePartRoles.Given });

        // Parent stem for the patronymic/matronymic slot (re-drawn once when it
        // repeats the given stem, so "Vess Vessseth" stays a rarity, not the rule).
        var parent = PickBiased(ref rng, table.GivenStems, tokens) ?? stem;
        if (parent.Form.Equals(stem.Form, StringComparison.OrdinalIgnoreCase))
            parent = PickBiased(ref rng, table.GivenStems, tokens) ?? parent;
        bool matri = effLineage.Equals(Lineages.Matrilineal, StringComparison.OrdinalIgnoreCase);
        if (table.Patronymic)
        {
            var marker = Marker(table, feminine, script);
            var parentShown = Cap(Display(parent, script));
            var gloss = $"{(feminine ? "daughter of " : "son of ")}{parent.Gloss}";
            var role = matri ? NamePartRoles.Matronymic : NamePartRoles.Patronymic;
            if (table.PatronymicPrefix)
            {
                // "mac Bran", "ben David", "ibn Karim": the marker is its own word.
                parts.Add(new NamePart { Form = $"{marker} {parentShown}", Gloss = gloss, Role = role });
            }
            else
            {
                // "Bran"+"son", "Kaze"+"no suke": fuse, unless the marker is
                // itself spaced ("Kaze no suke").
                string sep = marker.Contains(' ') ? " " : "";
                parts.Add(new NamePart { Form = parentShown + sep + marker, Gloss = gloss, Role = role });
            }
        }

        // Clan / family / warband / workshop / epithet slots.
        // NOTE: race dispatch is case-insensitive throughout; a `switch` on the
        // raw string would misroute "Orc" vs "orc" into the default branch.
        bool human = race.Equals(FantasyRaces.Human, StringComparison.OrdinalIgnoreCase);
        if (race.Equals(FantasyRaces.Orc, StringComparison.OrdinalIgnoreCase))
        {
            AddClan(ref rng, parts, table, script);
            if (table.Epithets.Count > 0 && rng.NextUInt(100) < 60)
                AddPickBiased(ref rng, parts, table.Epithets, NamePartRoles.Epithet, tokens, script);
        }
        else if (race.Equals(FantasyRaces.Gnome, StringComparison.OrdinalIgnoreCase) ||
                 race.Equals(FantasyRaces.Dragonfolk, StringComparison.OrdinalIgnoreCase))
        {
            AddClan(ref rng, parts, table, script);
            if (table.Epithets.Count > 0 && rng.NextUInt(100) < 40)
                AddPickBiased(ref rng, parts, table.Epithets, NamePartRoles.Epithet, tokens, script);
        }
        else if (race.Equals(FantasyRaces.HalfElf, StringComparison.OrdinalIgnoreCase))
        {
            var humanTable = store.ResolveCulture(
                string.IsNullOrWhiteSpace(culture) ? Cultures.FantasyCommon : culture, Cultures.All);
            var fam = Pick(ref rng, humanTable.FamilyAffixes) ?? Pick(ref rng, humanTable.Clans)
                ?? Pick(ref rng, store.Get(Cultures.FantasyCommon).FamilyAffixes);
            if (fam is not null) parts.Add(new NamePart { Form = Display(fam, script), Gloss = fam.Gloss + " (human side)", Role = NamePartRoles.Family });
        }
        else if (race.Equals(FantasyRaces.HalfOrc, StringComparison.OrdinalIgnoreCase))
        {
            if (rng.NextUInt(100) < 50 && table.Epithets.Count > 0)
                AddPickBiased(ref rng, parts, table.Epithets, NamePartRoles.Epithet, tokens, script);
            else
            {
                var humanTable = store.ResolveCulture(
                    string.IsNullOrWhiteSpace(culture) ? Cultures.FantasyCommon : culture, Cultures.All);
                var fam = Pick(ref rng, humanTable.FamilyAffixes)
                    ?? Pick(ref rng, store.Get(Cultures.FantasyCommon).FamilyAffixes);
                if (fam is not null) parts.Add(new NamePart { Form = Display(fam, script), Gloss = fam.Gloss + " (human side)", Role = NamePartRoles.Family });
            }
        }
        else
        {
            var clan = Pick(ref rng, table.Clans);
            if (clan is not null && rng.NextUInt(100) < (human ? 45 : 80))
            {
                parts.Add(new NamePart { Form = Display(clan, script), Gloss = clan.Gloss, Role = human ? NamePartRoles.Family : NamePartRoles.Clan });
            }
            else
            {
                var fam = PickFiltered(ref rng, table.FamilyAffixes, feminine);
                if (fam is not null)
                {
                    if (table.FamilySuffix)
                    {
                        // True suffixes fuse onto the given stem ("Ald"+"bury", "Mil"+"ov").
                        parts.Add(new NamePart { Form = stemShown + Display(fam, script), Gloss = $"{stem.Gloss}, {fam.Gloss}", Role = NamePartRoles.Family });
                    }
                    else
                    {
                        // House / family / court names stand alone ("Starhaven", "Li").
                        parts.Add(new NamePart { Form = Display(fam, script), Gloss = fam.Gloss, Role = human ? NamePartRoles.Family : NamePartRoles.Clan });
                    }
                }
            }
        }

        string text = JoinPerson(table.Key, parts);
        return new GeneratedName
        {
            Text = text,
            Race = race,
            Culture = culture,
            Lineage = effLineage,
            Provenance = new NameProvenance { Inspiration = "original", Note = table.Provenance },
            Inspirations = MatchedTokens(parts, tokens),
            Parts = parts,
            Seed = seed,
        };
    }

    private static GeneratedName BuildPlace(
        NameDataStore store, string kind, PlaceContext ctx,
        string race, string culture, int seed, int salt, HashSet<string> tokens, NameScript script)
    {
        var table = store.Get(TableKey(race, culture, store));
        var common = store.Get(Cultures.FantasyCommon);
        var rng = Rng.Create(seed, salt ^ (StableHash(kind) * 311) ^ (StableHash(race) * 701));
        var tags = TagsFor(ctx, kind);

        var descs = table.Descriptors.Concat(common.Descriptors).ToList();
        var nouns = table.Nouns.Concat(common.Nouns).ToList();
        var kindTag = KindTag(kind);
        var fitNouns = nouns.Where(n => n.Tags.Any(t => t.Equals(kindTag, StringComparison.OrdinalIgnoreCase))).ToList();
        if (fitNouns.Count == 0) fitNouns = nouns;

        var desc = WeightedPick(ref rng, descs, d => 1 + 2 * d.Tags.Count(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)) + 5 * CountMatches(d, tokens));
        var noun = WeightedPick(ref rng, fitNouns, n => 1 + 3 * n.Tags.Count(t => t.Equals(kindTag, StringComparison.OrdinalIgnoreCase)) + 5 * CountMatches(n, tokens));
        desc ??= new MorphemeYaml { Form = "Ash", Gloss = "ash tree" };
        noun ??= new MorphemeYaml { Form = "ford", Gloss = "crossing" };

        var descShown = Cap(Display(desc, script));
        var nounShown = Display(noun, script);
        // Single-word compounds read "Clearford", not "ClearFord".
        string text = nounShown.Contains(' ')
            ? $"{descShown} {nounShown}"
            : $"{descShown}{nounShown.ToLowerInvariant()}";
        var parts = new List<NamePart>
        {
            new() { Form = descShown, Gloss = desc.Gloss, Role = NamePartRoles.Descriptor },
            new() { Form = Cap(nounShown), Gloss = noun.Gloss, Role = NamePartRoles.Geography },
        };
        return new GeneratedName
        {
            Text = text,
            Race = race,
            Culture = culture,
            Lineage = NormalizeLineage(table.LineageDefault),
            Provenance = new NameProvenance { Inspiration = "original", Note = $"place near {string.Join("/", tags.Count > 0 ? tags : new[] { "open land" })}; {table.Provenance}" },
            Inspirations = MatchedTokens(parts, tokens),
            Parts = parts,
            Seed = seed,
        };
    }

    private static HashSet<string> TagsFor(PlaceContext ctx, string kind)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { KindTag(kind) };
        if (ctx.NearWater) { tags.Add("water"); tags.Add("river"); tags.Add("lake"); }
        if (ctx.NearForest) tags.Add("forest");
        if (ctx.DarkForest) { tags.Add("dark"); tags.Add("forest"); }
        if (ctx.Highland) { tags.Add("high"); tags.Add("mountain"); tags.Add("hill"); }
        if (ctx.Valley) tags.Add("valley");
        if (ctx.HasDeposit) tags.Add("deposit");
        if (ctx.Dangerous) tags.Add("danger");
        return tags;
    }

    private static string KindTag(string kind) => kind.ToLowerInvariant() switch
    {
        PlaceKinds.River => "river",
        PlaceKinds.Lake => "lake",
        PlaceKinds.Mountain => "mountain",
        PlaceKinds.Hill => "hill",
        PlaceKinds.Valley => "valley",
        PlaceKinds.Forest => "forest",
        PlaceKinds.Town => "town",
        PlaceKinds.City => "city",
        PlaceKinds.Hold => "town",
        PlaceKinds.Camp => "hill",
        _ => "village",
    };

    private static void AddClan(ref Rng rng, List<NamePart> parts, CultureData table, NameScript script)
    {
        var clan = Pick(ref rng, table.Clans);
        if (clan is not null) parts.Add(new NamePart { Form = Display(clan, script), Gloss = clan.Gloss, Role = NamePartRoles.Clan });
    }

    private static void AddPickBiased(ref Rng rng, List<NamePart> parts, List<MorphemeYaml> list, string role, HashSet<string> tokens, NameScript script)
    {
        var m = PickBiased(ref rng, list, tokens);
        if (m is not null) parts.Add(new NamePart { Form = Cap(Display(m, script)), Gloss = m.Gloss, Role = role });
    }

    private static MorphemeYaml? Pick(ref Rng rng, List<MorphemeYaml> list)
        => list.Count == 0 ? null : list[(int)rng.NextUInt((uint)list.Count)];

    /// <summary>Uniform pick when no inspirations; otherwise weight toward matching morphemes.</summary>
    private static MorphemeYaml? PickBiased(ref Rng rng, List<MorphemeYaml> list, HashSet<string> tokens)
        => tokens.Count == 0 ? Pick(ref rng, list) : WeightedPick(ref rng, list, m => 1 + 5 * CountMatches(m, tokens));

    private static int CountMatches(MorphemeYaml m, HashSet<string> tokens)
    {
        int n = 0;
        foreach (var t in tokens)
        {
            if (m.Form.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(m.Latin) && m.Latin.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                m.Gloss.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                m.Tags.Any(tag => tag.Contains(t, StringComparison.OrdinalIgnoreCase) || t.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                n++;
        }
        return n;
    }

    /// <summary>Requested inspiration tokens confirmed by the emitted parts (honest metadata).</summary>
    private static string[] MatchedTokens(List<NamePart> parts, HashSet<string> tokens)
        => tokens.Where(t => parts.Any(p =>
            p.Form.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            p.Gloss.Contains(t, StringComparison.OrdinalIgnoreCase))).ToArray();

    private static MorphemeYaml? PickFiltered(ref Rng rng, List<MorphemeYaml> list, bool feminine)
    {
        if (list.Count == 0) return null;
        var gendered = list.Where(m => m.Gloss.Contains(feminine ? "(fem" : "(masc", StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = gendered.Count > 0 ? gendered : list;
        return pool[(int)rng.NextUInt((uint)pool.Count)];
    }

    private static MorphemeYaml? WeightedPick(ref Rng rng, List<MorphemeYaml> list, Func<MorphemeYaml, int> weight)
    {
        if (list.Count == 0) return null;
        int total = 0;
        foreach (var m in list) total += Math.Max(1, weight(m));
        int roll = (int)rng.NextUInt((uint)total);
        foreach (var m in list)
        {
            roll -= Math.Max(1, weight(m));
            if (roll < 0) return m;
        }
        return list[^1];
    }

    private static string JoinPerson(string tableKey, List<NamePart> parts)
    {
        if (tableKey is "human_chinese" or "human_japanese")
        {
            var fam = parts.FirstOrDefault(p => p.Role is NamePartRoles.Family or NamePartRoles.Clan);
            var rest = parts.Where(p => p != fam).ToList();
            return fam is null ? string.Join(" ", rest.Select(p => p.Form)) : $"{fam.Form} {string.Join(" ", rest.Select(p => p.Form))}".Trim();
        }
        return string.Join(" ", parts.Select(p => p.Form));
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

    private static string Cap(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
