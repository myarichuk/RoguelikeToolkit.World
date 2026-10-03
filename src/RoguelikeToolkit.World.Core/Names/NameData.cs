using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoguelikeToolkit.World.Core.Names;

public sealed class MorphemeYaml
{
    public string Form { get; set; } = string.Empty;
    /// <summary>
    /// Optional Latin transliteration of <see cref="Form"/> (Russian, Hebrew
    /// tables). Used only when generating with <see cref="NameScript.Latin"/>.
    /// </summary>
    public string Latin { get; set; } = string.Empty;
    public string Gloss { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

public sealed class CultureData
{
    public string Key { get; set; } = string.Empty;
    /// <summary>Extra race names that resolve to this table (YAML-only extension).</summary>
    public List<string> Aliases { get; set; } = new();
    /// <summary>Whether person names get a patronymic/matronymic slot. Orcs, gnomes etc. set false.</summary>
    public bool Patronymic { get; set; } = true;
    /// <summary>
    /// When true the patronymic marker goes before the parent name ("mac Bran",
    /// "ben David", "ibn Karim") instead of fusing after it ("Bran"+"son").
    /// For prefix particles like Welsh map/ferch, Irish mac/nic, Arabic ibn/bint,
    /// Hebrew ben/bat.
    /// </summary>
    public bool PatronymicPrefix { get; set; } = false;
    /// <summary>
    /// When true, family affixes fuse onto the given stem as a suffix
    /// ("Ald"+"bury", "Mil"+"ov"). When false (default) they stand alone as
    /// their own name part ("Starhaven", "Li", "al-Sahra"). Suffix tables
    /// (dwarven, fantasy_common, human_eastern, human_western) set this true.
    /// </summary>
    public bool FamilySuffix { get; set; } = false;
    public string LineageDefault { get; set; } = "Patrilineal";
    public string Provenance { get; set; } = "original";
    public List<MorphemeYaml> GivenStems { get; set; } = new();
    public List<MorphemeYaml> SecondStems { get; set; } = new();
    public List<MorphemeYaml> FamilyAffixes { get; set; } = new();
    public List<MorphemeYaml> Clans { get; set; } = new();
    public List<MorphemeYaml> Epithets { get; set; } = new();
    public List<MorphemeYaml> Descriptors { get; set; } = new();
    public List<MorphemeYaml> Nouns { get; set; } = new();
    public string PatronymicSon { get; set; } = "son";
    public string PatronymicDaughter { get; set; } = "dottir";
    /// <summary>Latin transliterations of the patronymic markers (Russian ovich/ovna); blank falls back to the markers above.</summary>
    public string PatronymicSonLatin { get; set; } = string.Empty;
    public string PatronymicDaughterLatin { get; set; } = string.Empty;
    public List<string> Banned { get; set; } = new();
}

/// <summary>Thrown when name tables fail startup validation (missing/duplicate/mismatched keys, bad YAML).</summary>
public sealed class NameDataException : Exception
{
    public IReadOnlyList<string> Problems { get; }
    public NameDataException(IEnumerable<string> problems)
        : base("Name data failed validation:\n- " + string.Join("\n- ", problems))
        => Problems = problems.ToArray();
}

/// <summary>Thrown when a race/culture key has no table. Carries the available keys plus a closest match.</summary>
public sealed class UnknownNameKeyException : Exception
{
    public string Key { get; }
    public string Kind { get; }
    public IReadOnlyList<string> Available { get; }
    public string? Suggestion { get; }

    public UnknownNameKeyException(string kind, string key, IEnumerable<string> available)
        : base(BuildMessage(kind, key, available, out var suggestion, out var avail))
    {
        Key = key;
        Kind = kind;
        Available = avail;
        Suggestion = suggestion;
    }

    private static string BuildMessage(string kind, string key, IEnumerable<string> available,
        out string? suggestion, out string[] avail)
    {
        avail = available.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray();
        suggestion = ClosestMatch(key, avail);
        var msg = $"Unknown {kind} '{key}'. Available {kind}s: {string.Join(", ", avail)}.";
        if (suggestion is not null) msg += $" Did you mean '{suggestion}'?";
        return msg;
    }

    internal static string? ClosestMatch(string key, string[] candidates)
    {
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var c in candidates)
        {
            int d = Levenshtein(key.ToLowerInvariant(), c.ToLowerInvariant());
            if (d < bestDist) { bestDist = d; best = c; }
        }
        int threshold = Math.Max(2, key.Length / 3);
        return bestDist <= threshold ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return dp[a.Length, b.Length];
    }
}

/// <summary>
/// Data-oriented name tables. Convention: one file per folk/culture under
/// Names/Data/Races/ or Names/Data/Cultures/ (flat files also load), with the
/// file's Key matching its filename. YAML ships as embedded resources; on first
/// run <see cref="EnsureExtracted"/> copies them to disk where users can tweak
/// them or add new folk with zero code changes. Files on disk override embedded
/// tables with the same key.
/// </summary>
public sealed class NameDataStore
{
    public Dictionary<string, CultureData> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Strict lookup: throws <see cref="UnknownNameKeyException"/> naming available keys.</summary>
    public CultureData Get(string key, string kind = "table", IEnumerable<string>? extraAvailable = null)
    {
        if (Tables.TryGetValue(key, out var d)) return d;
        if (Aliases.TryGetValue(key, out var target) && Tables.TryGetValue(target, out var a)) return a;
        var available = Tables.Keys.Concat(Aliases.Keys);
        if (extraAvailable is not null) available = available.Concat(extraAvailable);
        throw new UnknownNameKeyException(kind, key, available);
    }

    public CultureData ResolveRace(string race, IEnumerable<string>? extraAvailable = null)
        => Get(race, "race", extraAvailable);
    public CultureData ResolveCulture(string culture, IEnumerable<string>? extraAvailable = null)
        => Get(culture, "culture", extraAvailable);

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RoguelikeToolkit.World", "Names");

    public static NameDataStore Load(string? overrideDir = null)
    {
        var store = new NameDataStore();
        var problems = new List<string>();
        var des = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var asm = typeof(NameDataStore).Assembly;
        string prefix = asm.GetName().Name + ".Names.Data.";
        var embeddedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                              && n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n))
        {
            var rel = ResourceToRelative(name.Substring(prefix.Length));
            LoadOne(store, problems, des, rel, ReadEmbedded(asm, name), fromDisk: false, embeddedKeys, diskKeys: null);
        }

        if (overrideDir is not null && Directory.Exists(overrideDir))
        {
            var diskKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(overrideDir, "*.yaml", SearchOption.AllDirectories).OrderBy(f => f))
            {
                var rel = Path.GetRelativePath(overrideDir, file);
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
                LoadOne(store, problems, des, rel, content, fromDisk: true, embeddedKeys, diskKeys);
            }
        }

        if (problems.Count > 0) throw new NameDataException(problems);
        return store;
    }

    private static void LoadOne(
        NameDataStore store, List<string> problems, IDeserializer des,
        string rel, string? content, bool fromDisk,
        HashSet<string> embeddedKeys, HashSet<string>? diskKeys)
    {
        if (content is null)
        {
            problems.Add($"{rel}: cannot read embedded resource.");
            return;
        }
        CultureData? data;
        try
        {
            data = des.Deserialize<CultureData>(content);
        }
        catch (Exception ex)
        {
            problems.Add($"{rel}: invalid YAML: {ex.Message}");
            return;
        }
        if (data is null || string.IsNullOrWhiteSpace(data.Key))
        {
            problems.Add($"{rel}: missing Key (expected Key matching the filename).");
            return;
        }
        var fileBase = Path.GetFileNameWithoutExtension(rel.Replace('\\', '/').Split('/').Last());
        if (!data.Key.Equals(fileBase, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{rel}: Key '{data.Key}' does not match filename '{fileBase}'.");
            return;
        }
        // Reserve the key before content checks so a file that is both broken
        // and duplicated still reports the duplicate (and a later twin that
        // only duplicates is not masked by the first file's content errors).
        bool duplicate = fromDisk ? !diskKeys!.Add(data.Key) : !embeddedKeys.Add(data.Key);
        if (duplicate)
        {
            problems.Add(fromDisk
                ? $"{rel}: duplicate Key '{data.Key}' (two on-disk files claim it)."
                : $"{rel}: duplicate Key '{data.Key}' (two embedded files claim it).");
            return;
        }
        int before = problems.Count;
        ValidateTable(rel, data, problems);
        // Bail on content errors for this file; other files still load so one
        // bad drop does not hide the rest of the report.
        if (problems.Count > before) return;
        // On-disk tables override embedded ones with the same key: that is the feature.
        store.Tables[data.Key] = data;
        foreach (var alias in data.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            if (store.Aliases.TryGetValue(alias, out var prev) &&
                !prev.Equals(data.Key, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{rel}: alias '{alias}' already points at '{prev}'.");
            else
                store.Aliases[alias] = data.Key;
        }
    }

    /// <summary>
    /// Content checks that catch authoring mistakes at load time instead of
    /// emitting gloss-less or fallback ("Ash") names at runtime.
    /// </summary>
    private static void ValidateTable(string rel, CultureData data, List<string> problems)
    {
        if (!data.LineageDefault.Equals("patrilineal", StringComparison.OrdinalIgnoreCase) &&
            !data.LineageDefault.Equals("matrilineal", StringComparison.OrdinalIgnoreCase) &&
            !data.LineageDefault.Equals("bilineal", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown LineageDefault '{data.LineageDefault}' (want Patrilineal, Matrilineal or Bilineal).");
        if (data.GivenStems.Count == 0)
            problems.Add($"{rel}: GivenStems is empty (every table needs at least one given stem).");
        if (data.Patronymic &&
            (string.IsNullOrWhiteSpace(data.PatronymicSon) || string.IsNullOrWhiteSpace(data.PatronymicDaughter)))
            problems.Add($"{rel}: Patronymic is true but PatronymicSon/Daughter is missing.");
        CheckMorphemes(rel, "GivenStems", data.GivenStems, problems);
        CheckMorphemes(rel, "SecondStems", data.SecondStems, problems);
        CheckMorphemes(rel, "FamilyAffixes", data.FamilyAffixes, problems);
        CheckMorphemes(rel, "Clans", data.Clans, problems);
        CheckMorphemes(rel, "Epithets", data.Epithets, problems);
        CheckMorphemes(rel, "Descriptors", data.Descriptors, problems);
        CheckMorphemes(rel, "Nouns", data.Nouns, problems);
    }

    private static void CheckMorphemes(
        string rel, string list, List<MorphemeYaml> morphemes, List<string> problems)
    {
        for (int i = 0; i < morphemes.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(morphemes[i].Form) || string.IsNullOrWhiteSpace(morphemes[i].Gloss))
                problems.Add($"{rel}: {list}[{i}] needs both Form and Gloss.");
        }
    }

    private static string? ReadEmbedded(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Maps a dotted resource tail (Races.dwarf.yaml) back to a relative path.</summary>
    internal static string ResourceToRelative(string tail)
    {
        var segs = tail.Split('.');
        if (segs.Length < 2) return tail;
        var file = segs[^2] + "." + segs[^1];
        var dirs = segs.Take(segs.Length - 2);
        return dirs.Any() ? string.Join("/", dirs) + "/" + file : file;
    }

    /// <summary>Copies embedded YAML to <paramref name="dir"/> when files are missing. Never overwrites user edits.</summary>
    public static string EnsureExtracted(string? dir = null)
    {
        dir ??= DefaultDirectory;
        var asm = typeof(NameDataStore).Assembly;
        string prefix = asm.GetName().Name + ".Names.Data.";
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                              && n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            var rel = ResourceToRelative(name.Substring(prefix.Length));
            var target = Path.Combine(dir, rel);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var s = asm.GetManifestResourceStream(name);
            if (s is null) continue;
            using var out_ = File.Create(target);
            s.CopyTo(out_);
        }
        return dir;
    }
}
