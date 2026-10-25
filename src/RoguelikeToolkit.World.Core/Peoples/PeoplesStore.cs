using System.Reflection;
using RoguelikeToolkit.World.Core.Lineage;
using YamlDotNet.Serialization;

namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// Data-oriented folk and culture tables. Convention: <c>Folks/*.yaml</c> hold one
/// <see cref="FolkDef"/> each and <c>Cultures/*.yaml</c> hold one <see cref="CultureDef"/>
/// each, <c>Subsistence/*.yaml</c> one <see cref="SubsistenceDef"/> and <c>Resources/*.yaml</c> one
/// <see cref="ResourceDef"/>, with the Key matching the filename; <c>compat.yaml</c> holds the pairwise
/// offspring rules. YAML ships as embedded resources; files under an override
/// directory (same layout) replace shipped entries with the same key.
/// </summary>
public sealed class PeoplesStore
{
    /// <summary>Loaded folk definitions keyed by <see cref="FolkDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, FolkDef> Folks { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Loaded culture definitions keyed by <see cref="CultureDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, CultureDef> Cultures { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Loaded subsistence profiles keyed by <see cref="SubsistenceDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, SubsistenceDef> Subsistence { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Loaded resources keyed by <see cref="ResourceDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, ResourceDef> Resources { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Loaded territory step-cost profiles keyed by <see cref="TerritoryDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, TerritoryDef> Territories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loaded court profiles keyed by <see cref="CourtDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, CourtDef> Courts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loaded epithet tables keyed by <see cref="TraitTableDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, TraitTableDef> TraitTables { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Loaded tactics keyed by <see cref="TacticDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, TacticDef> Tactics { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loaded ways of war keyed by <see cref="WarfareDef.Key"/>: a culture key, or <c>default</c> (case-insensitive).</summary>
    public Dictionary<string, WarfareDef> Warfare { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loaded battle models keyed by <see cref="BattleDef.Key"/> (case-insensitive).</summary>
    public Dictionary<string, BattleDef> Battles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pairwise offspring rules from <c>compat.yaml</c>; unlisted pairs blend.</summary>
    public CompatibilityMatrix Compatibility { get; } = new();

    private readonly List<(string Where, string Folk)> _compatRefs = new();

    /// <summary>
    /// The generated folk index: every shipped folk key, sorted. This replaces the
    /// hand-maintained <c>FantasyRaces.All</c> list as the folk registry (which stays as a shim).
    /// </summary>
    public IReadOnlyList<string> FolkKeys => Folks.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Strict folk lookup: throws <see cref="UnknownPeoplesKeyException"/> naming available keys.</summary>
    public FolkDef GetFolk(string key)
    {
        if (key is not null && Folks.TryGetValue(key, out var d)) return d;
        throw new UnknownPeoplesKeyException("folk", key ?? string.Empty, Folks.Keys);
    }

    /// <summary>Strict culture lookup: throws <see cref="UnknownPeoplesKeyException"/> naming available keys.</summary>
    public CultureDef GetCulture(string key)
    {
        if (key is not null && Cultures.TryGetValue(key, out var d)) return d;
        throw new UnknownPeoplesKeyException("culture", key ?? string.Empty, Cultures.Keys);
    }

    /// <summary>Strict subsistence lookup: throws <see cref="UnknownPeoplesKeyException"/> naming available keys.</summary>
    public SubsistenceDef GetSubsistence(string key)
    {
        if (key is not null && Subsistence.TryGetValue(key, out var d)) return d;
        throw new UnknownPeoplesKeyException("subsistence", key ?? string.Empty, Subsistence.Keys);
    }

    /// <summary>Strict resource lookup: throws <see cref="UnknownPeoplesKeyException"/> naming available keys.</summary>
    public ResourceDef GetResource(string key)
    {
        if (key is not null && Resources.TryGetValue(key, out var d)) return d;
        throw new UnknownPeoplesKeyException("resource", key ?? string.Empty, Resources.Keys);
    }

    /// <summary>
    /// The era gate: the exploits of profile <paramref name="subsistenceKey"/> whose resource needs no more than
    /// <paramref name="maxEra"/>, in file order. An exploit naming a missing resource (possible only after
    /// <see cref="LoadLenient"/>) is skipped.
    /// </summary>
    /// <exception cref="UnknownPeoplesKeyException">The profile does not exist.</exception>
    public IReadOnlyList<SubsistenceExploit> ExploitsOf(string subsistenceKey, ResourceEra maxEra)
    {
        var list = new List<SubsistenceExploit>();
        foreach (var e in GetSubsistence(subsistenceKey).Exploits)
            if (Resources.TryGetValue(e.Resource, out var r) && r.EraLevel <= maxEra) list.Add(e);
        return list;
    }

    /// <summary>True when folk <paramref name="folkKey"/> may live by <paramref name="subsistenceKey"/> (an empty allow-list allows any).</summary>
    /// <exception cref="UnknownPeoplesKeyException">The folk does not exist.</exception>
    public bool AllowsSubsistence(string folkKey, string subsistenceKey)
    {
        var allowed = GetFolk(folkKey).Subsistence;
        return allowed.Count == 0 || allowed.Any(k => string.Equals(k?.Trim(), subsistenceKey?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Why the (folk, culture) pair cannot be seeded, or null when it can: both exist and the culture's default
    /// subsistence is in the folk's allow-list. Cultures do not name a folk, so this is checked per pair, not at load.
    /// </summary>
    public string? PairingProblem(string folkKey, string cultureKey)
    {
        if (folkKey is null || !Folks.ContainsKey(folkKey)) return $"unknown folk '{folkKey}'.";
        if (cultureKey is null || !Cultures.TryGetValue(cultureKey, out var culture)) return $"unknown culture '{cultureKey}'.";
        if (AllowsSubsistence(folkKey, culture.Subsistence)) return null;
        return $"culture '{cultureKey}' lives by '{culture.Subsistence}', which folk '{folkKey}' does not allow (allowed: {string.Join(", ", Folks[folkKey].Subsistence)}).";
    }

    /// <summary>
    /// Loads the shipped folk/culture tables and, when <paramref name="overrideDir"/> exists,
    /// every <c>*.yaml</c> under its <c>Folks/</c>, <c>Cultures/</c>, <c>Subsistence/</c> and <c>Resources/</c> folders plus a top-level
    /// <c>compat.yaml</c>; an on-disk entry replaces the shipped one with the same key.
    /// </summary>
    /// <exception cref="PeoplesDataException">Any file is unreadable, malformed or invalid, or two files claim the same key; all problems are reported together.</exception>
    public static PeoplesStore Load(string? overrideDir = null)
    {
        var store = LoadCore(overrideDir, out var problems);
        if (problems.Count > 0) throw new PeoplesDataException(problems);
        return store;
    }

    /// <summary>
    /// Like <see cref="Load"/>, but never throws for bad tables: every file that validates is
    /// used, a broken on-disk file leaves the shipped entry of the same key in place, and the
    /// problems are reported instead.
    /// </summary>
    public static PeoplesStore LoadLenient(string? overrideDir, out IReadOnlyList<string> problems)
    {
        var store = LoadCore(overrideDir, out var list);
        problems = list;
        return store;
    }

    private static PeoplesStore LoadCore(string? overrideDir, out List<string> problems)
    {
        var store = new PeoplesStore();
        problems = new List<string>();
        var des = AotYamlObjectFactory.CreateDeserializer();

        var asm = typeof(PeoplesStore).Assembly;
        string prefix = asm.GetName().Name + ".Peoples.Data.";
        var embeddedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool embeddedCompat = false;
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                              && n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            var rel = ResourceToRelative(name.Substring(prefix.Length));
            LoadOne(store, problems, des, rel, ReadEmbedded(asm, name), fromDisk: false,
                embeddedKeys, diskKeys: null, ref embeddedCompat);
        }

        if (overrideDir is not null && Directory.Exists(overrideDir))
        {
            var diskKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool diskCompat = false;
            foreach (var file in Directory.GetFiles(overrideDir, "*.yaml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(overrideDir, file).Replace('\\', '/');
                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (Exception ex)
                {
                    problems.Add($"{rel}: cannot read file: {ex.Message}");
                    continue;
                }
                LoadOne(store, problems, des, rel, content, fromDisk: true,
                    embeddedKeys, diskKeys, ref diskCompat);
            }
        }
        foreach (var (where, folk) in store._compatRefs)
            if (!store.Folks.ContainsKey(folk)) problems.Add($"{where}: unknown folk '{folk}' (a typo here would silently leave the pair blending).");
        CheckReferences(store, problems);
        return store;
    }

    private static void LoadOne(
        PeoplesStore store, List<string> problems, IDeserializer des,
        string rel, string? content, bool fromDisk,
        HashSet<string> embeddedKeys, HashSet<string>? diskKeys, ref bool compatSeen)
    {
        if (content is null)
        {
            problems.Add($"{rel}: cannot read embedded resource.");
            return;
        }
        if (rel.Equals("compat.yaml", StringComparison.OrdinalIgnoreCase))
        {
            if (compatSeen)
            {
                problems.Add($"{rel}: duplicate compat file.");
                return;
            }
            compatSeen = true;
            CompatData? compat;
            try
            {
                compat = des.Deserialize<CompatData>(content);
            }
            catch (Exception ex)
            {
                problems.Add($"{rel}: invalid YAML: {ex.Message}");
                return;
            }
            ValidateCompat(rel, compat, store.Compatibility, problems, fromDisk ? store._compatRefs : null);
            return;
        }
        var fileBase = Path.GetFileNameWithoutExtension(rel.Split('/').Last());
        if (rel.StartsWith("Folks/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Folks, "folk", d => d.Key, ValidateFolk);
        else if (rel.StartsWith("Cultures/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Cultures, "culture", d => d.Key, ValidateCulture);
        else if (rel.StartsWith("Subsistence/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Subsistence, "subsistence", d => d.Key, ValidateSubsistence);
        else if (rel.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Resources, "resource", d => d.Key, ValidateResource);
        else if (rel.StartsWith("Territory/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Territories, "territory", d => d.Key, ValidateTerritory);
        else if (rel.StartsWith("Court/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Courts, "court", d => d.Key, ValidateCourt);
        else if (rel.StartsWith("Traits/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.TraitTables, "traits", d => d.Key, ValidateTraits);
        else if (rel.StartsWith("Tactics/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Tactics, "tactic", d => d.Key, ValidateTactic);
        else if (rel.StartsWith("Warfare/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Warfare, "warfare", d => d.Key, ValidateWarfare);
        else if (rel.StartsWith("Battle/", StringComparison.OrdinalIgnoreCase))
            LoadKeyed(store.Battles, "battle", d => d.Key, ValidateBattle);
        else
            problems.Add($"{rel}: unrecognized location (want Folks/, Cultures/, Subsistence/, Resources/, Territory/, Court/, Traits/, Tactics/, Warfare/ or Battle/*.yaml, or compat.yaml).");

        void LoadKeyed<T>(Dictionary<string, T> into, string kind, Func<T, string> key, Action<string, T, List<string>> validate)
        {
            T? data;
            try
            {
                data = des.Deserialize<T>(content!);
            }
            catch (Exception ex)
            {
                problems.Add($"{rel}: invalid YAML: {ex.Message}");
                return;
            }
            if (data is null || string.IsNullOrWhiteSpace(key(data)))
            {
                problems.Add($"{rel}: missing Key (expected Key matching the filename).");
                return;
            }
            string k = key(data);
            if (!k.Equals(fileBase, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{rel}: Key '{k}' does not match filename '{fileBase}'.");
                return;
            }
            bool duplicate = fromDisk ? !diskKeys!.Add(kind + ":" + k) : !embeddedKeys.Add(kind + ":" + k);
            if (duplicate)
            {
                problems.Add($"{rel}: duplicate {kind} Key '{k}'.");
                return;
            }
            int before = problems.Count;
            validate(rel, data, problems);
            if (problems.Count > before) return;
            into[k] = data;
        }
    }

    private static void ValidateFolk(string rel, FolkDef data, List<string> problems)
    {
        if (!string.Equals(data.Sapience, "full", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(data.Sapience, "instinct", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(data.Sapience, "setting", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown Sapience '{data.Sapience}' (want Full, Instinct or Setting).");
        if (string.IsNullOrWhiteSpace(data.Embodiment)) problems.Add($"{rel}: Embodiment is blank.");
        if (string.IsNullOrWhiteSpace(data.HabitatAffinity)) problems.Add($"{rel}: HabitatAffinity is blank.");
        if (data.LifespanYears < 1 || data.LifespanYears > 100000)
            problems.Add($"{rel}: LifespanYears {data.LifespanYears} is out of range (1-100000).");
        if (data.MaturationYears < 1 || data.MaturationYears > 100000)
            problems.Add($"{rel}: MaturationYears {data.MaturationYears} is out of range (1-100000).");
        else if (data.MaturationYears > data.LifespanYears)
            problems.Add($"{rel}: MaturationYears {data.MaturationYears} exceeds LifespanYears {data.LifespanYears}.");
        if (double.IsNaN(data.GrowthR) || data.GrowthR < 0 || data.GrowthR > 10)
            problems.Add($"{rel}: GrowthR {data.GrowthR} is out of range (0-10).");
        if (string.IsNullOrWhiteSpace(data.CompatGroup)) problems.Add($"{rel}: CompatGroup is blank.");
        CheckPrior(rel, nameof(FolkDef.OpennessPrior), data.OpennessPrior, problems);
        CheckPrior(rel, nameof(FolkDef.ConscientiousnessPrior), data.ConscientiousnessPrior, problems);
        CheckPrior(rel, nameof(FolkDef.ExtraversionPrior), data.ExtraversionPrior, problems);
        CheckPrior(rel, nameof(FolkDef.AgreeablenessPrior), data.AgreeablenessPrior, problems);
        CheckPrior(rel, nameof(FolkDef.NeuroticismPrior), data.NeuroticismPrior, problems);
    }

    private static void CheckPrior(string rel, string field, string? value, List<string> problems)
    {
        if (!string.Equals(value, "low", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "balanced", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "high", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown {field} '{value}' (want low, balanced or high).");
    }

    private static void ValidateCulture(string rel, CultureDef data, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(data.NamingTable)) problems.Add($"{rel}: NamingTable is blank.");
        if (string.IsNullOrWhiteSpace(data.Subsistence)) problems.Add($"{rel}: Subsistence is blank.");
        foreach (var kv in data.TerrainAffinity)
            if (!(kv.Value > 0) || double.IsInfinity(kv.Value)) problems.Add($"{rel}: TerrainAffinity.{kv.Key} must be a positive number.");
        if (!string.Equals(data.LineageDefault, "patrilineal", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(data.LineageDefault, "matrilineal", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(data.LineageDefault, "bilineal", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown LineageDefault '{data.LineageDefault}' (want Patrilineal, Matrilineal or Bilineal).");
        foreach (var (field, v) in new[]
                 {
                     ("Xenophobia", data.Xenophobia), ("HierarchyClaim", data.HierarchyClaim),
                     ("Collectivism", data.Collectivism), ("Honor", data.Honor),
                 })
            if (v < 0 || v > 100) problems.Add($"{rel}: {field} {v} is out of range (0-100).");
        if (data.FormAffinity is null) problems.Add($"{rel}: FormAffinity is null (use an empty map for none).");
        else
        {
            foreach (var (form, weight) in data.FormAffinity)
                if (string.IsNullOrWhiteSpace(form) || weight < 0 || weight > 100)
                    problems.Add($"{rel}: FormAffinity '{form}' weight {weight} is out of range (0-100).");
            if (data.FormAffinity.Count > 0 && data.FormAffinity.Values.Sum() <= 0) problems.Add($"{rel}: FormAffinity weights are all zero.");
        }
        if (string.IsNullOrWhiteSpace(data.FaithForm)) problems.Add($"{rel}: FaithForm is blank.");
        if (!string.Equals(data.MixedNaming, "blend", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(data.MixedNaming, "dominant", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(data.MixedNaming, "hybrid", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown MixedNaming '{data.MixedNaming}' (want Blend, Dominant or Hybrid).");
        if (double.IsNaN(data.DominantShareThreshold) || data.DominantShareThreshold < 0.5 || data.DominantShareThreshold > 1)
            problems.Add($"{rel}: DominantShareThreshold {data.DominantShareThreshold} is out of range (0.5-1).");
        foreach (var (field, v) in new[]
                 {
                     ("OpennessOffset", data.OpennessOffset), ("ConscientiousnessOffset", data.ConscientiousnessOffset),
                     ("ExtraversionOffset", data.ExtraversionOffset), ("AgreeablenessOffset", data.AgreeablenessOffset),
                     ("NeuroticismOffset", data.NeuroticismOffset),
                 })
            if (v < -100 || v > 100) problems.Add($"{rel}: {field} {v} is out of range (-100-100).");
    }

    private static void ValidateTerritory(string rel, TerritoryDef data, List<string> problems)
    {
        if (!(data.Step > 0) || double.IsInfinity(data.Step)) problems.Add($"{rel}: Step must be a positive number.");
        foreach (var (field, v) in new[] { ("Climb", data.Climb), ("High", data.High), ("River", data.River) })
            if (!(v >= 0) || double.IsInfinity(v)) problems.Add($"{rel}: {field} must be a non-negative number.");
        if (double.IsNaN(data.HighFrom) || double.IsInfinity(data.HighFrom)) problems.Add($"{rel}: HighFrom must be a number.");
        foreach (var kv in data.Biomes)
            if (!(kv.Value > 0) || double.IsInfinity(kv.Value)) problems.Add($"{rel}: Biomes.{kv.Key} must be a positive number.");
    }

    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    private static void ValidateCourt(string rel, CourtDef data, List<string> problems)
    {
        if (!(data.HazardBase >= 0) || !Finite(data.HazardBase)) problems.Add($"{rel}: HazardBase must be a non-negative number.");
        if (!(data.HazardGrowth >= 0) || !Finite(data.HazardGrowth)) problems.Add($"{rel}: HazardGrowth must be a non-negative number.");
        if (!(data.HardCap >= 1) || !Finite(data.HardCap)) problems.Add($"{rel}: HardCap must be at least 1.");
        foreach (var (field, lo, hi) in new[]
                 {
                     ("RulerDeathAge", data.RulerDeathAgeMin, data.RulerDeathAgeMax), ("OfficeAge", data.OfficeAgeMin, data.OfficeAgeMax),
                 })
            if (!(lo > 0) || !(hi >= lo) || !(hi <= data.HardCap)) problems.Add($"{rel}: {field}Min/Max must satisfy 0 < min <= max <= HardCap.");
        if (!(data.OldAge > 0) || !Finite(data.OldAge)) problems.Add($"{rel}: OldAge must be positive.");
        if (data.UntimelyDeathCauses is null || data.UntimelyDeathCauses.Count == 0 || data.UntimelyDeathCauses.Any(string.IsNullOrWhiteSpace))
            problems.Add($"{rel}: UntimelyDeathCauses needs at least one non-blank cause.");
        if (data.FeminineDeathCauses is null || data.FeminineDeathCauses.Any(string.IsNullOrWhiteSpace))
            problems.Add($"{rel}: FeminineDeathCauses must be a list of non-blank causes (may be empty).");
        if (data.Candidates < 1) problems.Add($"{rel}: Candidates must be at least 1.");
        if (data.MaxEpithets < 0) problems.Add($"{rel}: MaxEpithets cannot be negative.");
        if (!(data.FeminineShare >= 0 && data.FeminineShare <= 1)) problems.Add($"{rel}: FeminineShare must be in 0-1.");
        foreach (var (field, v) in new[]
                 {
                     ("FavouriteBelowAgreeableness", data.FavouriteBelowAgreeableness), ("RivalAboveExtraversion", data.RivalAboveExtraversion),
                     ("RivalBelowAgreeableness", data.RivalBelowAgreeableness),
                 })
            if (!(v >= 0 && v <= 100)) problems.Add($"{rel}: {field} must be in 0-100.");
        if (data.Skills is null) problems.Add($"{rel}: Skills is null.");
        else
            foreach (var skill in data.Skills)
                foreach (var w in skill.Value ?? new Dictionary<string, double>())
                    if (!Finite(w.Value)) problems.Add($"{rel}: Skills.{skill.Key}.{w.Key} must be a number.");
        if (data.Forms is null) problems.Add($"{rel}: Forms is null.");
        else
            foreach (var form in data.Forms)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var office in form.Value ?? new List<CourtOfficeDef>())
                {
                    if (office is null || string.IsNullOrWhiteSpace(office.Office) || string.IsNullOrWhiteSpace(office.Kind) || string.IsNullOrWhiteSpace(office.Skill))
                        problems.Add($"{rel}: Forms.{form.Key} has an office without Office, Kind or Skill.");
                    else if (!seen.Add(office.Office)) problems.Add($"{rel}: Forms.{form.Key} lists office '{office.Office}' twice.");
                    else if (data.Skills != null && !data.Skills.ContainsKey(office.Skill))
                        problems.Add($"{rel}: Forms.{form.Key}.{office.Office} judges by unknown skill '{office.Skill}'.");
                }
            }
    }

    private static void ValidateTraits(string rel, TraitTableDef data, List<string> problems)
    {
        if (data.Epithets is null) { problems.Add($"{rel}: Epithets is null (use an empty list)."); return; }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in data.Epithets)
        {
            if (e is null || string.IsNullOrWhiteSpace(e.Key)) { problems.Add($"{rel}: an epithet has no Key."); continue; }
            if (!seen.Add(e.Key)) problems.Add($"{rel}: epithet '{e.Key}' is listed twice.");
            foreach (var bound in (e.Min ?? new()).Concat(e.Max ?? new()))
                if (!(bound.Value >= 0 && bound.Value <= 100)) problems.Add($"{rel}: epithet '{e.Key}' bound {bound.Key} must be in 0-100.");
        }
    }

    private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

    private static readonly string[] BattleKindNames = { "Field", "Ambush", "Siege", "Relief", "Naval" };

    private static readonly string[] OutcomeNames =
    {
        "AttackerDecisive", "AttackerVictory", "AttackerPyrrhic", "Stalemate", "DefenderVictory", "DefenderDecisive", "Averted",
    };

    private static readonly string[] AftermathActions = { "capture", "raze", "sack", "grudge", "none" };

    private static void CheckSeasons(string rel, string field, Dictionary<string, double>? seasons, List<string> problems)
    {
        if (seasons is null) { problems.Add($"{rel}: {field} is null (use an empty map)."); return; }
        foreach (var kv in seasons)
        {
            if (!SeasonNames.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                problems.Add($"{rel}: {field}.{kv.Key} is not a season (want Spring, Summer, Autumn or Winter).");
            if (!(kv.Value >= 0) || !Finite(kv.Value)) problems.Add($"{rel}: {field}.{kv.Key} must be a non-negative number.");
        }
    }

    private static void CheckFactors(string rel, string field, Dictionary<string, double>? factors, List<string> problems)
    {
        if (factors is null) { problems.Add($"{rel}: {field} is null (use an empty map)."); return; }
        foreach (var kv in factors)
            if (!(kv.Value > 0) || !Finite(kv.Value)) problems.Add($"{rel}: {field}.{kv.Key} must be a positive number.");
    }

    private static void ValidateTactic(string rel, TacticDef data, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(data.Arm)) problems.Add($"{rel}: Arm is blank.");
        if (data.Kinds is null) problems.Add($"{rel}: Kinds is null (use an empty list for field and relief battles).");
        else
            foreach (var kind in data.Kinds)
                if (!BattleKindNames.Contains(kind, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{rel}: unknown battle kind '{kind}' (want Field, Ambush, Siege, Relief or Naval).");
        if (!(data.Surprise > 0) || !Finite(data.Surprise)) problems.Add($"{rel}: Surprise must be a positive number.");
        if (!(data.Cover >= 0 && data.Cover <= 1)) problems.Add($"{rel}: Cover must be in 0-1.");
        CheckFactors(rel, "TerrainFit", data.TerrainFit, problems);
        CheckFactors(rel, "Counters", data.Counters, problems);
        CheckSeasons(rel, "Seasons", data.Seasons, problems);
        if (data.Beats is null || data.Beats.Any(string.IsNullOrWhiteSpace)) problems.Add($"{rel}: Beats must be a list of non-blank names (may be empty).");
    }

    private static void ValidateWarfare(string rel, WarfareDef data, List<string> problems)
    {
        if (data.Doctrine is null || data.Doctrine.Count == 0) problems.Add($"{rel}: Doctrine needs at least one tactic.");
        else
        {
            foreach (var kv in data.Doctrine)
                if (!(kv.Value >= 0) || !Finite(kv.Value)) problems.Add($"{rel}: Doctrine.{kv.Key} must be a non-negative number.");
            if (data.Doctrine.Values.Sum() <= 0) problems.Add($"{rel}: Doctrine weights are all zero.");
        }
        if (data.Units is null) problems.Add($"{rel}: Units is null (use an empty list).");
        else
            foreach (var u in data.Units)
                if (u is null || string.IsNullOrWhiteSpace(u.Key) || string.IsNullOrWhiteSpace(u.Arm) || !(u.Weight > 0) || !Finite(u.Weight))
                    problems.Add($"{rel}: every unit needs a Key, an Arm and a positive Weight.");
        foreach (var (field, v) in new[]
                 {
                     ("Mobility", data.Mobility), ("Discipline", data.Discipline), ("SiegeSkill", data.SiegeSkill),
                     ("Forage", data.Forage), ("ColdTolerance", data.ColdTolerance),
                 })
            if (!(v >= 0 && v <= 100)) problems.Add($"{rel}: {field} {v} is out of range (0-100).");
        if (!(data.Levy > 0) || !Finite(data.Levy)) problems.Add($"{rel}: Levy must be a positive number.");
        CheckSeasons(rel, "CampaignSeasons", data.CampaignSeasons, problems);
        if (data.CampaignSeasons != null && data.CampaignSeasons.Count > 0 && !(data.CampaignSeasons.Values.Sum() > 0))
            problems.Add($"{rel}: CampaignSeasons weights are all zero.");
        if (data.Customs is null || data.Customs.Any(string.IsNullOrWhiteSpace)) problems.Add($"{rel}: Customs must be a list of non-blank names (may be empty).");
    }

    private static void ValidateBattle(string rel, BattleDef data, List<string> problems)
    {
        if (data.FormLevy is null) problems.Add($"{rel}: FormLevy is null.");
        else
            foreach (var kv in data.FormLevy)
                if (!(kv.Value >= 0 && kv.Value <= 1)) problems.Add($"{rel}: FormLevy.{kv.Key} must be in 0-1.");
        foreach (var (field, v) in new[]
                 {
                     ("DefenderReadiness", data.DefenderReadiness), ("MusterFloor", data.MusterFloor), ("SupplyPower", data.SupplyPower), ("DeclineBelow", data.DeclineBelow), ("WarnedBase", data.WarnedBase), ("SupplyMin", data.SupplyMin),
                     ("AvertedBelow", data.AvertedBelow), ("RiverBypass", data.RiverBypass), ("WinterDefenderShare", data.WinterDefenderShare),
                     ("LoserLossMin", data.LoserLossMin), ("LoserLossMax", data.LoserLossMax), ("WinnerLossMin", data.WinnerLossMin),
                     ("WinnerLossMax", data.WinnerLossMax), ("GeneralDeathBase", data.GeneralDeathBase), ("SackCasualty", data.SackCasualty),
                     ("SackDisplacement", data.SackDisplacement), ("RazeCasualty", data.RazeCasualty), ("CasualtyCap", data.CasualtyCap),
                 })
            if (!(v >= 0 && v <= 1)) problems.Add($"{rel}: {field} must be in 0-1.");
        foreach (var (field, v) in new[]
                 {
                     ("ReachDegrees", data.ReachDegrees), ("MusterDegrees", data.MusterDegrees), ("RenownScale", data.RenownScale), ("WinterRange", data.WinterRange),
                     ("MarginScale", data.MarginScale), ("MarginSpread", data.MarginSpread), ("MarginSpreadMin", data.MarginSpreadMin),
                     ("PursuitDiscipline", data.PursuitDiscipline), ("FieldSharpness", data.FieldSharpness),
                 })
            if (!(v > 0) || !Finite(v)) problems.Add($"{rel}: {field} must be a positive number.");
        foreach (var (field, v) in new[]
                 {
                     ("CollectivismLevy", data.CollectivismLevy), ("WarnedPerIntrigue", data.WarnedPerIntrigue), ("MoraleBase", data.MoraleBase),
                     ("MoraleHonor", data.MoraleHonor), ("MoraleHome", data.MoraleHome), ("MoraleHoly", data.MoraleHoly),
                     ("MoraleRenown", data.MoraleRenown), ("MoralePressure", data.MoralePressure), ("PressureFrom", data.PressureFrom),
                     ("MoraleHunger", data.MoraleHunger), ("HillsFrom", data.HillsFrom), ("RiverDefence", data.RiverDefence),
                     ("InitiativeMobility", data.InitiativeMobility), ("InitiativeMartial", data.InitiativeMartial),
                     ("InitiativeIntrigue", data.InitiativeIntrigue), ("InitiativeNoise", data.InitiativeNoise), ("MartialPower", data.MartialPower),
                     ("WinterPenalty", data.WinterPenalty), ("WinterAttrition", data.WinterAttrition), ("LoserLossBase", data.LoserLossBase),
                     ("LoserLossPerMargin", data.LoserLossPerMargin), ("WinnerLossBase", data.WinnerLossBase), ("PursuitLoss", data.PursuitLoss),
                     ("GeneralDeathLost", data.GeneralDeathLost), ("GeneralDeathRouted", data.GeneralDeathRouted),
                 })
            if (!(v >= 0) || !Finite(v)) problems.Add($"{rel}: {field} must be a non-negative number.");
        if (!(data.MoraleMin > 0 && data.MoraleMax >= data.MoraleMin) || !Finite(data.MoraleMax)) problems.Add($"{rel}: MoraleMin/Max must satisfy 0 < min <= max.");
        if (!(data.StalemateMargin <= data.VictoryMargin && data.VictoryMargin <= data.DecisiveMargin && data.StalemateMargin >= 0) || !Finite(data.DecisiveMargin))
            problems.Add($"{rel}: margins must satisfy 0 <= StalemateMargin <= VictoryMargin <= DecisiveMargin.");
        if (data.LoserLossMin > data.LoserLossMax || data.WinnerLossMin > data.WinnerLossMax) problems.Add($"{rel}: a loss Min exceeds its Max.");
        if (data.FieldCandidates < 1) problems.Add($"{rel}: FieldCandidates must be at least 1.");
        if (data.NemesisMeetings < 1) problems.Add($"{rel}: NemesisMeetings must be at least 1.");
        if (string.IsNullOrWhiteSpace(data.GeneralDeathCause)) problems.Add($"{rel}: GeneralDeathCause is blank.");
        if (data.TerrainDefence is null || data.TerrainDefence.Count == 0) problems.Add($"{rel}: TerrainDefence needs at least one terrain class.");
        else
        {
            foreach (var kv in data.TerrainDefence)
                if (!(kv.Value >= 0) || !Finite(kv.Value)) problems.Add($"{rel}: TerrainDefence.{kv.Key} must be a non-negative number.");
            foreach (var name in new[] { data.DefaultTerrain, data.HillsTerrain })
                if (string.IsNullOrWhiteSpace(name) || !data.TerrainDefence.ContainsKey(name))
                    problems.Add($"{rel}: terrain class '{name}' is not a TerrainDefence key.");
            foreach (var kv in data.BiomeTerrain ?? new Dictionary<string, string>())
                if (kv.Value is null || !data.TerrainDefence.ContainsKey(kv.Value))
                    problems.Add($"{rel}: BiomeTerrain.{kv.Key} names terrain class '{kv.Value}', which is not a TerrainDefence key.");
        }
        CheckSeasons(rel, "SeasonSupply", data.SeasonSupply, problems);
        CheckFactors(rel, "ForageChannels", data.ForageChannels, problems);
        if (data.Beats is null) problems.Add($"{rel}: Beats is null (use an empty list).");
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in data.Beats)
            {
                if (b is null || string.IsNullOrWhiteSpace(b.Key)) { problems.Add($"{rel}: a beat has no Key."); continue; }
                if (!seen.Add(b.Key)) problems.Add($"{rel}: beat '{b.Key}' is listed twice.");
                if (!(b.Weight > 0) || !Finite(b.Weight)) problems.Add($"{rel}: beat '{b.Key}' needs a positive Weight.");
                if (b.When is null || b.When.Any(string.IsNullOrWhiteSpace)) problems.Add($"{rel}: beat '{b.Key}' has a blank condition.");
            }
        }
        foreach (var kv in data.Epithets ?? new Dictionary<string, BattleEpithetDef>())
        {
            if (kv.Value is null) { problems.Add($"{rel}: Epithets.{kv.Key} is empty."); continue; }
            if (!Finite(kv.Value.Spread) || !Finite(kv.Value.Death) || !Finite(kv.Value.Initiative)) problems.Add($"{rel}: Epithets.{kv.Key} terms must be numbers.");
            CheckFactors(rel, $"Epithets.{kv.Key}.Tactics", kv.Value.Tactics, problems);
        }
        foreach (var kv in data.Customs ?? new Dictionary<string, BattleCustomDef>())
        {
            if (kv.Value is null) continue; // an empty map is a custom with no model terms (it may still unlock beats)
            if (!(kv.Value.Pursuit >= 0) || !Finite(kv.Value.Pursuit) || !(kv.Value.Casualties >= 0) || !Finite(kv.Value.Casualties)
                || !(kv.Value.Capture >= 0 && kv.Value.Capture <= 1) || !Finite(kv.Value.Morale))
                problems.Add($"{rel}: Customs.{kv.Key} needs Pursuit, Casualties >= 0, Capture in 0-1 and a numeric Morale.");
        }
        foreach (var kv in data.Aftermath ?? new Dictionary<string, string>())
        {
            if (!OutcomeNames.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) problems.Add($"{rel}: Aftermath.{kv.Key} is not a battle outcome.");
            if (!AftermathActions.Contains(kv.Value, StringComparer.OrdinalIgnoreCase))
                problems.Add($"{rel}: Aftermath.{kv.Key} '{kv.Value}' is not an action (want capture, raze, sack, grudge or none).");
        }
        if (!(data.SackDisplacement < 1)) problems.Add($"{rel}: SackDisplacement must be below 1.");
        if (data.AftermathGrudge < 0) problems.Add($"{rel}: AftermathGrudge cannot be negative.");
        if (string.IsNullOrWhiteSpace(data.NemesisRelation)) problems.Add($"{rel}: NemesisRelation is blank.");
    }

    private static void ValidateSubsistence(string rel, SubsistenceDef data, List<string> problems)
    {
        foreach (var (field, v) in new[]
                 {
                     ("Arable", data.Arable), ("Pasture", data.Pasture), ("Game", data.Game),
                     ("Timber", data.Timber), ("Fish", data.Fish),
                 })
            if (!(v >= 0 && v <= 10)) problems.Add($"{rel}: {field} {v} is out of range (0-10).");
        foreach (var (field, v) in new[]
                 {
                     ("Defensibility", data.Defensibility), ("Coast", data.Coast), ("River", data.River), ("Road", data.Road),
                 })
            if (!(v >= -10 && v <= 10)) problems.Add($"{rel}: {field} {v} is out of range (-10-10).");
        if (data.Exploits is null)
        {
            problems.Add($"{rel}: Exploits is null (use an empty list).");
            return;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < data.Exploits.Count; i++)
        {
            var e = data.Exploits[i];
            if (e is null || string.IsNullOrWhiteSpace(e.Resource))
            {
                problems.Add($"{rel}: Exploits[{i}] needs a Resource.");
                continue;
            }
            e.Resource = e.Resource.Trim();
            if (!seen.Add(e.Resource)) problems.Add($"{rel}: Exploits[{i}] repeats resource '{e.Resource}'.");
            if (!(e.Value >= 0 && e.Value <= 10)) problems.Add($"{rel}: Exploits[{i}] Value {e.Value} is out of range (0-10).");
        }
    }

    private static readonly string[] YieldChannels = { "arable", "pasture", "game", "timber", "fish" };

    private static void ValidateResource(string rel, ResourceDef data, List<string> problems)
    {
        bool deposit = !string.IsNullOrWhiteSpace(data.Deposit);
        bool yield = !string.IsNullOrWhiteSpace(data.Yield);
        if (yield && !YieldChannels.Contains(data.Yield.Trim(), StringComparer.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown Yield '{data.Yield}' (want arable, pasture, game, timber, fish, or blank).");
        if (deposit && yield) problems.Add($"{rel}: a resource is a Deposit or a Yield, not both.");
        if (!ResourceDef.TryParseEra(data.Era, out _))
            problems.Add($"{rel}: unknown Era '{data.Era}' (want Ancient, Medieval or Industrial).");
        if (!(data.Value >= 0 && data.Value <= 100)) problems.Add($"{rel}: Value {data.Value} is out of range (0-100).");
        if (data.LifetimeTurns < 0 || data.LifetimeTurns > 100000)
            problems.Add($"{rel}: LifetimeTurns {data.LifetimeTurns} is out of range (0-100000).");
        else if (data.LifetimeTurns > 0 && !deposit)
            problems.Add($"{rel}: LifetimeTurns needs a Deposit (only deposits run out).");
    }

    /// <summary>Cross-file checks once every file has loaded (shipped and override alike).</summary>
    private static void CheckReferences(PeoplesStore store, List<string> problems)
    {
        foreach (var c in store.Cultures.Values.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
            if (!string.IsNullOrWhiteSpace(c.Subsistence) && !store.Subsistence.ContainsKey(c.Subsistence.Trim()))
                problems.Add($"Cultures/{c.Key}.yaml: unknown Subsistence '{c.Subsistence}'.");
        foreach (var f in store.Folks.Values.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (f.Subsistence is null)
            {
                problems.Add($"Folks/{f.Key}.yaml: Subsistence is null (use an empty list for any).");
                continue;
            }
            foreach (var k in f.Subsistence)
                if (string.IsNullOrWhiteSpace(k) || !store.Subsistence.ContainsKey(k.Trim()))
                    problems.Add($"Folks/{f.Key}.yaml: unknown Subsistence '{k}'.");
        }
        foreach (var sub in store.Subsistence.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            foreach (var e in sub.Exploits)
                if (!store.Resources.ContainsKey(e.Resource))
                    problems.Add($"Subsistence/{sub.Key}.yaml: unknown resource '{e.Resource}'.");
        foreach (var w in store.Warfare.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!w.Key.Equals("default", StringComparison.OrdinalIgnoreCase) && !store.Cultures.ContainsKey(w.Key))
                problems.Add($"Warfare/{w.Key}.yaml: '{w.Key}' is neither a culture nor default.");
            foreach (var t in w.Doctrine.Keys)
                if (!store.Tactics.ContainsKey(t)) problems.Add($"Warfare/{w.Key}.yaml: unknown tactic '{t}' in Doctrine.");
            if (store.Battles.TryGetValue("default", out var battle))
                foreach (var c in w.Customs)
                    if (!battle.Customs.ContainsKey(c)) problems.Add($"Warfare/{w.Key}.yaml: unknown custom '{c}' (want a key of Battle/default.yaml Customs).");
        }
        foreach (var t in store.Tactics.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            foreach (var c in t.Counters.Keys)
                if (!store.Tactics.ContainsKey(c)) problems.Add($"Tactics/{t.Key}.yaml: Counters names unknown tactic '{c}'.");
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in store.Resources.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(r.Deposit)) continue;
            string d = r.Deposit.Trim();
            if (!claimed.TryAdd(d, r.Key)) problems.Add($"Resources/{r.Key}.yaml: deposit {d} is already claimed by '{claimed[d]}'.");
        }
    }

    private static void ValidateCompat(string rel, CompatData? data, CompatibilityMatrix matrix, List<string> problems, List<(string Where, string Folk)>? refs)
    {
        if (data is null || data.Rules is null)
        {
            problems.Add($"{rel}: missing Rules list.");
            return;
        }
        var seen = new HashSet<(string, string)>();
        for (int i = 0; i < data.Rules.Count; i++)
        {
            var row = data.Rules[i];
            string where = $"{rel}: Rules[{i}]";
            if (row is null || string.IsNullOrWhiteSpace(row.FolkA) || string.IsNullOrWhiteSpace(row.FolkB))
            {
                problems.Add($"{where} needs both FolkA and FolkB.");
                continue;
            }
            if (row.FolkA.Equals(row.FolkB, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{where}: FolkA and FolkB are the same folk (self-pairs always blend).");
                continue;
            }
            CompatibilityRule rule;
            if (string.Equals(row.Rule, "blend", StringComparison.OrdinalIgnoreCase)) rule = CompatibilityRule.Blend;
            else if (string.Equals(row.Rule, "dominant", StringComparison.OrdinalIgnoreCase)) rule = CompatibilityRule.Dominant;
            else if (string.Equals(row.Rule, "sterile", StringComparison.OrdinalIgnoreCase)) rule = CompatibilityRule.Sterile;
            else
            {
                problems.Add($"{where}: unknown Rule '{row.Rule}' (want Blend, Dominant or Sterile).");
                continue;
            }
            var a = row.FolkA.ToLowerInvariant();
            var b = row.FolkB.ToLowerInvariant();
            var key = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
            if (!seen.Add(key))
            {
                problems.Add($"{where}: duplicate rule for '{row.FolkA}' x '{row.FolkB}'.");
                continue;
            }
            matrix.With(row.FolkA, row.FolkB, rule);
            // Only user-supplied rows are checked: the shipped table deliberately rules on folks that have no file yet (demon, angel).
            refs?.Add((where, row.FolkA.Trim()));
            refs?.Add((where, row.FolkB.Trim()));
        }
    }

    private static string? ReadEmbedded(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Maps a dotted resource tail (Folks.human.yaml) back to a relative path.</summary>
    internal static string ResourceToRelative(string tail)
    {
        var segs = tail.Split('.');
        if (segs.Length < 2) return tail;
        var file = segs[^2] + "." + segs[^1];
        var dirs = segs.Take(segs.Length - 2);
        return dirs.Any() ? string.Join("/", dirs) + "/" + file : file;
    }
}
