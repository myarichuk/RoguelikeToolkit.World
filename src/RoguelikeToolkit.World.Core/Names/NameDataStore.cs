using System.Reflection;
using YamlDotNet.Serialization;

namespace RoguelikeToolkit.World.Core.Names;

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
    /// <summary>Loaded tables keyed by <see cref="CultureData.Key"/> (case-insensitive).</summary>
    public Dictionary<string, CultureData> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extra names that resolve to a table, built from each table's <see cref="CultureData.Aliases"/> (case-insensitive alias to table key).</summary>
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

    /// <summary>Strict lookup of a race key or alias; throws <see cref="UnknownNameKeyException"/> with Kind <c>race</c> if there is no such table.</summary>
    public CultureData ResolveRace(string race, IEnumerable<string>? extraAvailable = null)
        => Get(race, "race", extraAvailable);
    /// <summary>Strict lookup of a culture key or alias; throws <see cref="UnknownNameKeyException"/> with Kind <c>culture</c> if there is no such table.</summary>
    public CultureData ResolveCulture(string culture, IEnumerable<string>? extraAvailable = null)
        => Get(culture, "culture", extraAvailable);

    /// <summary>
    /// Per-user data folder. ApplicationData can be empty (containers, service accounts), and
    /// combining onto "" would silently put the tables under whatever the working directory is,
    /// so fall back through the other per-user folders to the application folder.
    /// </summary>
    public static string DefaultDirectory
    {
        get
        {
            var root = new[]
                {
                    Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolder.UserProfile,
                }
                .Select(Environment.GetFolderPath)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p))
                ?? AppContext.BaseDirectory;
            return Path.Combine(root, "RoguelikeToolkit.World", "Names");
        }
    }

    /// <summary>
    /// Loads the shipped tables and, when <paramref name="overrideDir"/> exists, every <c>*.yaml</c> under it (recursively); an on-disk table replaces the shipped one with the same key.
    /// A null or missing directory loads only the shipped tables.
    /// </summary>
    /// <exception cref="NameDataException">Any file is unreadable, malformed or invalid, or two files claim the same key; all problems are reported together.</exception>
    public static NameDataStore Load(string? overrideDir = null)
    {
        var store = LoadCore(overrideDir, out var problems);
        if (problems.Count > 0) throw new NameDataException(problems);
        return store;
    }

    /// <summary>
    /// Like <see cref="Load"/>, but never throws for bad tables: every file that validates is
    /// used, a broken on-disk file leaves the shipped table of the same key in place, and the
    /// problems are reported instead. For editors and tools, where one typo should not discard
    /// every other edit.
    /// </summary>
    public static NameDataStore LoadLenient(string? overrideDir, out IReadOnlyList<string> problems)
    {
        var store = LoadCore(overrideDir, out var list);
        problems = list;
        return store;
    }

    private static NameDataStore LoadCore(string? overrideDir, out List<string> problems)
    {
        var store = new NameDataStore();
        problems = new List<string>();
        var des = AotYamlObjectFactory.CreateDeserializer();

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
        foreach (var (field, v, lo) in new[]
                 {
                     ("CompoundChance", data.CompoundChance, 0), ("ClanChance", data.ClanChance, -1),
                     ("EpithetChance", data.EpithetChance, 0), ("FamilyChance", data.FamilyChance, 0),
                 })
            if (v < lo || v > 100) problems.Add($"{rel}: {field} {v} is out of range ({lo}-100).");
        if (!data.PlaceOrder.Equals("DescriptorFirst", StringComparison.OrdinalIgnoreCase) &&
            !data.PlaceOrder.Equals("NounFirst", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown PlaceOrder '{data.PlaceOrder}' (want DescriptorFirst or NounFirst).");
        if (!data.PlaceJoin.Equals("Fuse", StringComparison.OrdinalIgnoreCase) &&
            !data.PlaceJoin.Equals("Space", StringComparison.OrdinalIgnoreCase) &&
            !data.PlaceJoin.Equals("Hyphen", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{rel}: unknown PlaceJoin '{data.PlaceJoin}' (want Fuse, Space or Hyphen).");
        if (!string.IsNullOrWhiteSpace(data.PlaceMutation))
        {
            if (!data.PlaceMutation.Equals("Soft", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{rel}: unknown PlaceMutation '{data.PlaceMutation}' (want Soft, or leave it blank).");
            else if (!data.PlaceOrder.Equals("NounFirst", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{rel}: PlaceMutation needs PlaceOrder NounFirst (it mutates the descriptor after the noun).");
        }
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

    /// <summary>
    /// Copies embedded YAML to <paramref name="dir"/>, keeping it current without clobbering edits.
    /// A manifest (<c>.shipped.json</c>) records the hash of each file as last shipped:
    /// missing files are written; files still identical to what was last shipped are refreshed
    /// when the library ships a newer version; files the user changed are never touched.
    /// A copy from before the manifest existed cannot be told apart from an edit, so it is
    /// refreshed too but the old content is kept next to it as <c>name.yaml.old</c>.
    /// </summary>
    public static string EnsureExtracted(string? dir = null)
    {
        dir ??= DefaultDirectory;
        var asm = typeof(NameDataStore).Assembly;
        string prefix = asm.GetName().Name + ".Names.Data.";
        string manifestPath = Path.Combine(dir, ".shipped.json");
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(manifestPath))
                manifest = ShippedManifestJson.Read(File.ReadAllText(manifestPath)) ?? manifest;
            manifest = new Dictionary<string, string>(manifest, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // A corrupt manifest just means we treat every file as legacy (backed up, then refreshed).
        }

        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                              && n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            var rel = ResourceToRelative(name.Substring(prefix.Length));
            var target = Path.Combine(dir, rel);
            byte[] shipped;
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s is null) continue;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                shipped = ms.ToArray();
            }
            string shippedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(shipped));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
            {
                File.WriteAllBytes(target, shipped);
                manifest[rel] = shippedHash;
                continue;
            }
            string onDisk = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target)));
            if (onDisk == shippedHash)
            {
                manifest[rel] = shippedHash;
            }
            else if (manifest.TryGetValue(rel, out var lastShipped))
            {
                if (lastShipped == onDisk)
                {
                    // Untouched since the last extraction: safe to upgrade.
                    File.WriteAllBytes(target, shipped);
                    manifest[rel] = shippedHash;
                }
                // else: the user edited it; their copy wins.
            }
            else
            {
                // Pre-manifest copy: stale-or-edited is unknowable, so upgrade but keep the old one.
                File.Copy(target, target + ".old", overwrite: true);
                File.WriteAllBytes(target, shipped);
                manifest[rel] = shippedHash;
            }
        }

        File.WriteAllText(manifestPath, ShippedManifestJson.Write(manifest));
        return dir;
    }
}
