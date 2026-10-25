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
        else
            problems.Add($"{rel}: unrecognized location (want Folks/, Cultures/, Subsistence/ or Resources/*.yaml, or compat.yaml).");

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
