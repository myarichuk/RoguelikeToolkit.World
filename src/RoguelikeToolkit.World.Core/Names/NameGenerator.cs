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
    private static string DisplayBase(MorphemeYaml m, NameScript script)
        => string.IsNullOrWhiteSpace(m.Base)
            ? Display(m, script)
            : Display(new MorphemeYaml { Form = m.Base, Latin = m.BaseLatin }, script);

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

    // ---- gender ---------------------------------------------------------------

    /// <summary>+1 masculine, -1 feminine, 0 either. Tags win; the older "(fem)"/"(masc)" gloss marker still works.</summary>
    private static int GenderOf(MorphemeYaml m)
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

    private static bool HasTag(MorphemeYaml m, string tag)
        => m.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Entries usable for the requested gender; falls back to the whole list when that leaves nothing.</summary>
    private static List<MorphemeYaml> ForGender(List<MorphemeYaml> list, bool feminine)
    {
        var pool = list.Where(m => GenderOf(m) != (feminine ? 1 : -1)).ToList();
        return pool.Count > 0 ? pool : list;
    }

    // ---- people ---------------------------------------------------------------

    private static GeneratedName BuildPerson(
        NameDataStore store, string race, string culture,
        int seed, string? lineage, bool feminine, int salt, HashSet<string> tokens, NameScript script)
    {
        if (string.IsNullOrWhiteSpace(race)) race = FantasyRaces.Human;
        if (string.IsNullOrWhiteSpace(culture)) culture = Cultures.FantasyCommon;
        var table = store.Get(TableKey(race, culture, store));
        var rng = Rng.Create(seed, salt ^ (StableHash(race) * 7919) ^ (StableHash(culture) * 4789));
        var effLineage = NormalizeLineage(string.IsNullOrWhiteSpace(lineage) ? table.LineageDefault : lineage);
        var parts = new List<NamePart>();

        // Given name: stem (+ second stem for compound cultures), biased by inspirations.
        var givenPool = ForGender(table.GivenStems, feminine);
        var stem = PickBiased(ref rng, givenPool, tokens) ?? new MorphemeYaml { Form = "Ash", Gloss = "ash tree" };
        MorphemeYaml? second = null;
        if (table.SecondStems.Count > 0 && !HasTag(stem, "solo") && rng.NextUInt(100) < (uint)table.CompoundChance)
        {
            // Strict, unlike given stems: a feminine name must never take a masculine ending ("Layla al-Din").
            var secondPool = table.SecondStems.Where(m => GenderOf(m) != (feminine ? 1 : -1)).ToList();
            second = PickSecond(ref rng, secondPool, tokens, stem);
        }
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
                : Fuse(stemShown, secondShown, dedupe: true);
        }
        var givenGloss = second is null ? stem.Gloss : $"{stem.Gloss}-{second.Gloss}";
        parts.Add(new NamePart { Form = given, Gloss = givenGloss, Role = NamePartRoles.Given });

        // Parent for the patronymic/matronymic slot: the right gender for the line,
        // never the child's own stem ("Vess Vessseth" is not a lineage, it is a typo).
        bool matri = effLineage.Equals(Lineages.Matrilineal, StringComparison.OrdinalIgnoreCase);
        string? parentForm = null;
        if (table.Patronymic)
        {
            var marker = Marker(table, feminine, script);
            var parentPool = table.GivenStems.Where(m => !HasTag(m, "nopatron")
                    && GenderOf(m) != (matri ? 1 : -1)
                    && !m.Form.Equals(stem.Form, StringComparison.OrdinalIgnoreCase)
                    // "Gust"+"gust": a parent that already ends in the marker reads as a stutter.
                    && (table.PatronymicPrefix || !m.Form.EndsWith(marker, StringComparison.OrdinalIgnoreCase))).ToList();
            if (parentPool.Count == 0) parentPool = table.GivenStems;
            var parent = PickBiased(ref rng, parentPool, tokens) ?? stem;
            parentForm = parent.Form;
            var gloss = $"{(feminine ? "daughter of " : "son of ")}{parent.Gloss}";
            var role = matri ? NamePartRoles.Matronymic : NamePartRoles.Patronymic;
            if (table.PatronymicPrefix)
            {
                // "mac Bran", "ben David", "ibn Karim": the marker is its own word.
                parts.Add(new NamePart { Form = $"{marker} {Cap(Display(parent, script))}", Gloss = gloss, Role = role });
            }
            else
            {
                // "Bran"+"son", "Petr"+"ovich": fuse onto the base, unless the marker is itself spaced.
                string sep = marker.Contains(' ') ? " " : "";
                var baseShown = Cap(DisplayBase(parent, script));
                parts.Add(new NamePart
                {
                    Form = sep.Length > 0 ? baseShown + sep + marker : Fuse(baseShown, marker, dedupe: true),
                    Gloss = gloss, Role = role,
                });
            }
        }

        // Clan / family / warband / epithet slots. Race-specific behaviour lives in
        // the YAML (ClanChance, EpithetChance, FamilyChance); only the half-bloods,
        // which borrow a human family name, are special-cased by race.
        bool human = race.Equals(FantasyRaces.Human, StringComparison.OrdinalIgnoreCase);
        if (race.Equals(FantasyRaces.HalfElf, StringComparison.OrdinalIgnoreCase))
        {
            AddHumanFamily(ref rng, store, culture, parts, script, feminine);
        }
        else if (race.Equals(FantasyRaces.HalfOrc, StringComparison.OrdinalIgnoreCase))
        {
            if (rng.NextUInt(100) < 50 && table.Epithets.Count > 0)
                AddPickBiased(ref rng, parts, table.Epithets, NamePartRoles.Epithet, tokens, script);
            else
                AddHumanFamily(ref rng, store, culture, parts, script, feminine);
        }
        else
        {
            uint clanChance = (uint)(table.ClanChance >= 0 ? table.ClanChance : human ? 45 : 80);
            bool clanned = table.Clans.Count > 0 && rng.NextUInt(100) < clanChance;
            if (clanned)
            {
                var clan = PickBiased(ref rng, table.Clans, tokens)!;
                parts.Add(new NamePart
                {
                    Form = Display(clan, script), Gloss = clan.Gloss,
                    Role = human ? NamePartRoles.Family : NamePartRoles.Clan,
                });
            }
            else if (table.FamilyAffixes.Count > 0 && rng.NextUInt(100) < (uint)table.FamilyChance)
            {
                var fam = PickFiltered(ref rng, table.FamilyAffixes, feminine, tokens)!;
                if (table.FamilySuffix)
                {
                    // True suffixes fuse onto a *different* stem ("Ald"+"bury", "Mil"+"ov"):
                    // a surname built from the person's own given name reads as a stutter,
                    // and one built from a feminine given name ("Olgaova") is simply wrong.
                    var famPool = table.GivenStems.Where(m => GenderOf(m) != -1
                        && !m.Form.Equals(stem.Form, StringComparison.OrdinalIgnoreCase)
                        && (parentForm is null || !m.Form.Equals(parentForm, StringComparison.OrdinalIgnoreCase))).ToList();
                    var fb = PickBiased(ref rng, famPool.Count > 0 ? famPool : table.GivenStems, tokens) ?? stem;
                    parts.Add(new NamePart
                    {
                        Form = Fuse(Cap(DisplayBase(fb, script)), Display(fam, script), dedupe: true),
                        Gloss = $"{fb.Gloss}, {fam.Gloss}",
                        Role = NamePartRoles.Family,
                    });
                }
                else
                {
                    // House / family / court names stand alone ("Starhaven", "Li").
                    parts.Add(new NamePart
                    {
                        Form = Display(fam, script), Gloss = fam.Gloss,
                        Role = human ? NamePartRoles.Family : NamePartRoles.Clan,
                    });
                }
            }
            if (table.Epithets.Count > 0 && rng.NextUInt(100) < (uint)table.EpithetChance)
                AddPickBiased(ref rng, parts, table.Epithets, NamePartRoles.Epithet, tokens, script);
        }

        string text = JoinPerson(table, parts);
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

    private static void AddHumanFamily(
        ref Rng rng, NameDataStore store, string culture, List<NamePart> parts, NameScript script, bool feminine)
    {
        var humanTable = store.ResolveCulture(
            string.IsNullOrWhiteSpace(culture) ? Cultures.FantasyCommon : culture, Cultures.All);
        var common = store.Get(Cultures.FantasyCommon);
        var fam = Pick(ref rng, humanTable.Clans.Concat(humanTable.FamilyAffixes).Where(m => GenderOf(m) != (feminine ? 1 : -1)).ToList())
            ?? Pick(ref rng, common.FamilyAffixes);
        if (fam is null) return;
        // Suffix-only tables ("bury", "ov") have no standalone surname, so borrow a stem to carry them.
        string form = Display(fam, script);
        string gloss = fam.Gloss;
        if (humanTable.FamilySuffix && humanTable.FamilyAffixes.Contains(fam) && humanTable.GivenStems.Count > 0)
        {
            var b = Pick(ref rng, humanTable.GivenStems.Where(m => GenderOf(m) != -1).ToList()) ?? humanTable.GivenStems[0];
            form = Fuse(Cap(DisplayBase(b, script)), form, dedupe: true);
            gloss = $"{b.Gloss}, {fam.Gloss}";
        }
        parts.Add(new NamePart { Form = form, Gloss = gloss + " (human side)", Role = NamePartRoles.Family });
    }

    /// <summary>
    /// A compatible second stem: not a repeat of the first, and not sharing its meaning
    /// ("Eagle"+"eagle-eyed" or "Blood"+"blood-sworn" is a stutter, not a name).
    /// </summary>
    private static MorphemeYaml? PickSecond(ref Rng rng, List<MorphemeYaml> list, HashSet<string> tokens, MorphemeYaml stem)
    {
        var stemWords = NameThemes.Words(stem.Gloss).ToHashSet();
        MorphemeYaml? last = null;
        for (int i = 0; i < 8; i++)
        {
            var cand = PickBiased(ref rng, list, tokens);
            if (cand is null) return null;
            last = cand;
            if (cand.Form.Equals(stem.Form, StringComparison.OrdinalIgnoreCase)) continue;
            if (stem.Form.EndsWith(cand.Form, StringComparison.OrdinalIgnoreCase)) continue;
            if (NameThemes.Words(cand.Gloss).Any(stemWords.Contains)) continue;
            return cand;
        }
        // Every draw clashed: better a plain single stem than a stuttering compound.
        return list.Count <= 1 ? last : null;
    }

    /// <summary>
    /// Joins two name fragments. With <paramref name="dedupe"/> a doubled boundary letter
    /// is dropped ("Thorn"+"nic" -> "Thornic"); runs of three or more of any letter are always cut to two.
    /// </summary>
    internal static string Fuse(string a, string b, bool dedupe)
    {
        if (string.IsNullOrEmpty(b)) return a;
        if (string.IsNullOrEmpty(a)) return b;
        if (dedupe && b.Length > 1 && char.IsLetter(a[^1]) &&
            char.ToLowerInvariant(a[^1]) == char.ToLowerInvariant(b[0]))
            b = b.Substring(1);
        return Transliterator.NormalizeInnerFinals(CollapseRuns(a + b));
    }

    private static string CollapseRuns(string s)
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

    // ---- places ---------------------------------------------------------------

    private static GeneratedName BuildPlace(
        NameDataStore store, string kind, PlaceContext ctx,
        string race, string culture, int seed, int salt, HashSet<string> tokens, NameScript script)
    {
        if (string.IsNullOrWhiteSpace(race)) race = FantasyRaces.Human;
        if (string.IsNullOrWhiteSpace(culture)) culture = Cultures.FantasyCommon;
        var table = store.Get(TableKey(race, culture, store));
        var common = store.Get(Cultures.FantasyCommon);
        var rng = Rng.Create(seed, salt ^ (StableHash(kind) * 311) ^ (StableHash(race) * 701));
        var tags = TagsFor(ctx, kind);
        var kindTags = KindTags(kind);

        // Vocabulary: the table's own, topped up from fantasy_common only for
        // tables that opt in (a Japanese village must never be "Clearwood").
        var descs = table.PlaceFallback ? table.Descriptors.Concat(common.Descriptors).ToList() : table.Descriptors.ToList();
        var nouns = table.PlaceFallback ? table.Nouns.Concat(common.Nouns).ToList() : table.Nouns.ToList();
        if (descs.Count == 0) descs = common.Descriptors.ToList();
        if (nouns.Count == 0) nouns = common.Nouns.ToList();

        // Nouns must fit the kind of place (a river is not a "-hold"); fall back to the
        // whole list only when nothing in the table can name that kind of place.
        var fitNouns = nouns.Where(n => kindTags.Any(k => HasTag(n, k))).ToList();
        if (fitNouns.Count == 0) fitNouns = nouns;

        int DescWeight(MorphemeYaml d)
        {
            // Untagged descriptors are generic ("Great"); tagged ones must fit the kind or terrain.
            int overlap = d.Tags.Count(t => tags.Contains(t));
            int w = d.Tags.Count == 0 ? 1 : overlap > 0 ? 1 + 2 * overlap : 0;
            return w + 5 * CountMatches(d, tokens);
        }
        int NounWeight(MorphemeYaml n)
        {
            int w = 1;
            for (int i = 0; i < kindTags.Length; i++)
                if (HasTag(n, kindTags[i])) w += i == 0 ? 3 : 1;
            w += n.Tags.Count(t => tags.Contains(t) && !kindTags.Contains(t, StringComparer.OrdinalIgnoreCase));
            return w + 5 * CountMatches(n, tokens);
        }

        MorphemeYaml? desc = null, noun = null;
        for (int i = 0; i < 6; i++)
        {
            desc = WeightedPick(ref rng, descs, DescWeight);
            noun = WeightedPick(ref rng, fitNouns, NounWeight);
            if (desc is null || noun is null) break;
            // "Stonestone" / "Fordford": same word or same meaning twice.
            if (desc.Form.Equals(noun.Form, StringComparison.OrdinalIgnoreCase)) continue;
            if (NameThemes.Words(desc.Gloss).Intersect(NameThemes.Words(noun.Gloss)).Any()) continue;
            break;
        }
        desc ??= new MorphemeYaml { Form = "Ash", Gloss = "ash tree" };
        noun ??= new MorphemeYaml { Form = "ford", Gloss = "crossing" };

        bool nounFirst = table.PlaceOrder.Equals("NounFirst", StringComparison.OrdinalIgnoreCase);
        var descShown = Display(desc, script);
        var nounShown = Display(noun, script);
        var first = CapWord(nounFirst ? nounShown : descShown);
        var secondRaw = nounFirst ? descShown : nounShown;
        string join = table.PlaceJoin.ToLowerInvariant();
        bool spaced = join == "space" || secondRaw.Contains(' ') || secondRaw.Contains('-') || first.Contains(' ');
        string text = join == "hyphen" ? first + "-" + secondRaw
            : spaced ? first + " " + CapWord(secondRaw)
            // Single-word compounds read "Clearford", not "ClearFord".
            : Fuse(first, secondRaw.ToLowerInvariant(), dedupe: false);

        var descPart = new NamePart { Form = CapWord(descShown), Gloss = desc.Gloss, Role = NamePartRoles.Descriptor };
        var nounPart = new NamePart { Form = CapWord(nounShown), Gloss = noun.Gloss, Role = NamePartRoles.Geography };
        var parts = nounFirst ? new List<NamePart> { nounPart, descPart } : new List<NamePart> { descPart, nounPart };
        var near = tags.Where(t => !kindTags.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        return new GeneratedName
        {
            Text = text,
            Race = race,
            Culture = culture,
            Lineage = NormalizeLineage(table.LineageDefault),
            Provenance = new NameProvenance { Inspiration = "original", Note = $"{kind} near {string.Join("/", near.Count > 0 ? near : new List<string> { "open land" })}; {table.Provenance}" },
            // Confirm against the chosen morphemes (not just the text) so terrain tags like "dark" count.
            Inspirations = tokens.Where(t => NameThemes.Matches(desc, t) || NameThemes.Matches(noun, t))
                .OrderBy(t => t, StringComparer.Ordinal).ToArray(),
            Parts = parts,
            Seed = seed,
        };
    }

    private static HashSet<string> TagsFor(PlaceContext ctx, string kind)
    {
        var tags = new HashSet<string>(KindTags(kind), StringComparer.OrdinalIgnoreCase);
        if (ctx.NearWater) { tags.Add("water"); tags.Add("river"); tags.Add("lake"); }
        if (ctx.NearForest) tags.Add("forest");
        if (ctx.DarkForest) { tags.Add("dark"); tags.Add("forest"); }
        if (ctx.Highland) { tags.Add("high"); tags.Add("mountain"); tags.Add("hill"); }
        if (ctx.Valley) tags.Add("valley");
        if (ctx.HasDeposit) tags.Add("deposit");
        if (ctx.Dangerous) tags.Add("danger");
        return tags;
    }

    /// <summary>Noun tags that fit a place kind, best first (a hold prefers "hold", then any "town" word).</summary>
    private static string[] KindTags(string kind) => kind.ToLowerInvariant() switch
    {
        PlaceKinds.River => new[] { "river" },
        PlaceKinds.Lake => new[] { "lake" },
        PlaceKinds.Mountain => new[] { "mountain" },
        PlaceKinds.Hill => new[] { "hill" },
        PlaceKinds.Valley => new[] { "valley" },
        PlaceKinds.Forest => new[] { "forest" },
        PlaceKinds.Town => new[] { "town" },
        PlaceKinds.City => new[] { "city", "town" },
        PlaceKinds.Hold => new[] { "hold", "town" },
        PlaceKinds.Camp => new[] { "camp", "hill" },
        _ => new[] { "village" },
    };

    // ---- picking --------------------------------------------------------------

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
            if (NameThemes.Matches(m, t)) n++;
        return n;
    }

    /// <summary>Requested inspiration tokens confirmed by the emitted parts (honest metadata).</summary>
    private static string[] MatchedTokens(List<NamePart> parts, HashSet<string> tokens)
        => tokens.Where(t => parts.Any(p => NameThemes.Matches(p, t))).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    /// <summary>Gender-appropriate pick (Ivanov/Ivanova); an unmarked list is a plain biased pick.</summary>
    private static MorphemeYaml? PickFiltered(ref Rng rng, List<MorphemeYaml> list, bool feminine, HashSet<string> tokens)
        => list.Count == 0 ? null : PickBiased(ref rng, ForGender(list, feminine), tokens);

    /// <summary>Weighted pick; weights of 0 are excluded unless every weight is 0 (then the pick is uniform).</summary>
    private static MorphemeYaml? WeightedPick(ref Rng rng, List<MorphemeYaml> list, Func<MorphemeYaml, int> weight)
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

    private static string JoinPerson(CultureData table, List<NamePart> parts)
    {
        if (table.FamilyFirst)
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

    /// <summary>Capitalizes a word but leaves lowercase particles ("al-Bahr") alone.</summary>
    private static string CapWord(string s)
        => s.StartsWith("al-", StringComparison.Ordinal) ? s : Cap(s);
}
