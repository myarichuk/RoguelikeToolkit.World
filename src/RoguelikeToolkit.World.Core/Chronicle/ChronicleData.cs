using System.Reflection;
using YamlDotNet.Serialization;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// The chronicle's grammar and lexicon files: <c>Chronicle/Data/Grammar/common.yaml</c> and
/// <c>Chronicle/Data/Lexicon/common.yaml</c>, plus one overlay of each per culture key (<c>dwarven_clan.yaml</c>...), embedded
/// in the assembly and optionally replaced or extended from a directory with the same layout.
/// <list type="bullet">
/// <item>A grammar file maps a symbol to a list of rules (<see cref="ChronicleRuleDef"/>). In an overlay, a rule whose id
/// already exists in the common pool replaces it and any other rule joins the pool; a symbol written <c>name!</c> replaces the
/// whole pool.</item>
/// <item>A lexicon file maps a list key to words. An overlay's words join the common list; <c>key!</c> replaces it.</item>
/// </list>
/// Compiled grammars are cached per culture. Problems (bad YAML, bad predicates or templates, duplicate ids) are collected,
/// never thrown, so a broken overlay costs its own rules only; <see cref="ChronicleLint"/> checks the rest.
/// </summary>
internal sealed class ChronicleData
{
    /// <summary>The key of the common files, and the culture of an event that belongs to no polity.</summary>
    public const string Common = "common";

    private static readonly object SharedGate = new();
    private static ChronicleData? _shared;

    private readonly Dictionary<string, Dictionary<string, Pool>> _grammar = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, WordList>> _lexicon = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChronicleGrammar> _compiled = new(StringComparer.Ordinal);
    private readonly List<string> _problems = new();
    private readonly object _gate = new();

    private sealed class Pool
    {
        public bool Replace;
        public readonly List<ChronicleRule> Rules = new();
    }

    private sealed class WordList
    {
        public bool Replace;
        public readonly List<string> Words = new();
    }

    private ChronicleData() { }

    /// <summary>The shipped data, loaded once.</summary>
    public static ChronicleData Shared
    {
        get
        {
            lock (SharedGate) return _shared ??= Load(null);
        }
    }

    /// <summary>Problems found while loading (file: reason); empty for the shipped data.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Culture keys that have a grammar or lexicon overlay, in ordinal order.</summary>
    public IReadOnlyList<string> Cultures
        => _grammar.Keys.Concat(_lexicon.Keys).Where(k => k != Common).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Loads the embedded files and then, when <paramref name="overrideDir"/> exists, every <c>Grammar/*.yaml</c> and
    /// <c>Lexicon/*.yaml</c> under it; an on-disk file replaces the embedded file of the same name.
    /// </summary>
    public static ChronicleData Load(string? overrideDir)
    {
        var sources = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        var asm = typeof(ChronicleData).GetTypeInfo().Assembly;
        string prefix = asm.GetName().Name + ".Chronicle.Data.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) continue;
            string tail = name.Substring(prefix.Length);
            int dot = tail.IndexOf('.');
            if (dot < 0) continue;
            sources[tail.Substring(0, dot) + "/" + tail.Substring(dot + 1)] = ReadEmbedded(asm, name);
        }
        if (overrideDir is not null && Directory.Exists(overrideDir))
        {
            foreach (var folder in new[] { "Grammar", "Lexicon" })
            {
                string dir = Path.Combine(overrideDir, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*.yaml").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string rel = folder + "/" + Path.GetFileName(file);
                    try { sources[rel] = File.ReadAllText(file); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { sources[rel] = null; }
                }
            }
        }
        return FromSources(sources.Select(kv => (kv.Key, kv.Value)));
    }

    /// <summary>Builds data from <c>(relative path, YAML)</c> pairs such as <c>Grammar/common.yaml</c>; a null text is unreadable.</summary>
    internal static ChronicleData FromSources(IEnumerable<(string Path, string? Yaml)> sources)
    {
        var data = new ChronicleData();
        var des = AotYamlObjectFactory.CreateChronicleDeserializer();
        foreach (var (path, yaml) in sources.OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            if (yaml is null) { data._problems.Add($"{path}: cannot read file."); continue; }
            string file = Path.GetFileNameWithoutExtension(path);
            if (path.StartsWith("Grammar/", StringComparison.OrdinalIgnoreCase)) data.LoadGrammar(des, path, file, yaml);
            else if (path.StartsWith("Lexicon/", StringComparison.OrdinalIgnoreCase)) data.LoadLexicon(des, path, file, yaml);
            else data._problems.Add($"{path}: not under Grammar/ or Lexicon/.");
        }
        if (!data._grammar.ContainsKey(Common)) data._problems.Add("Grammar/common.yaml: missing.");
        return data;
    }

    /// <summary>The grammar <paramref name="culture"/> renders with: common plus its overlays (just common for an unknown culture).</summary>
    public ChronicleGrammar For(string? culture)
    {
        string key = string.IsNullOrEmpty(culture) ? Common : culture!;
        lock (_gate)
        {
            if (_compiled.TryGetValue(key, out var g)) return g;
            g = Compile(key);
            _compiled[key] = g;
            return g;
        }
    }

    private ChronicleGrammar Compile(string culture)
    {
        var symbols = new Dictionary<string, List<ChronicleRule>>(StringComparer.Ordinal);
        if (_grammar.TryGetValue(Common, out var common))
            foreach (var kv in common) symbols[kv.Key] = new List<ChronicleRule>(kv.Value.Rules);
        if (culture != Common && _grammar.TryGetValue(culture, out var overlay))
        {
            foreach (var kv in overlay.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Value.Replace || !symbols.TryGetValue(kv.Key, out var pool))
                {
                    symbols[kv.Key] = new List<ChronicleRule>(kv.Value.Rules);
                    continue;
                }
                foreach (var rule in kv.Value.Rules)
                {
                    int at = pool.FindIndex(r => string.Equals(r.Id, rule.Id, StringComparison.Ordinal));
                    if (at >= 0) pool[at] = rule;
                    else pool.Add(rule);
                }
            }
        }
        var lexicon = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (_lexicon.TryGetValue(Common, out var commonWords))
            foreach (var kv in commonWords) lexicon[kv.Key] = new List<string>(kv.Value.Words);
        if (culture != Common && _lexicon.TryGetValue(culture, out var overlayWords))
        {
            foreach (var kv in overlayWords.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Value.Replace || !lexicon.TryGetValue(kv.Key, out var list))
                {
                    lexicon[kv.Key] = new List<string>(kv.Value.Words);
                    continue;
                }
                foreach (var w in kv.Value.Words)
                    if (!list.Contains(w, StringComparer.Ordinal)) list.Add(w);
            }
        }
        return new ChronicleGrammar(culture,
            symbols.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal),
            lexicon.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal));
    }

    private void LoadGrammar(IDeserializer des, string path, string culture, string yaml)
    {
        Dictionary<string, List<ChronicleRuleDef>>? doc;
        try
        {
            doc = des.Deserialize<Dictionary<string, List<ChronicleRuleDef>>>(yaml);
        }
        catch (Exception ex)
        {
            _problems.Add($"{path}: invalid YAML: {ex.Message}");
            return;
        }
        var file = new Dictionary<string, Pool>(StringComparer.Ordinal);
        foreach (var kv in doc ?? new Dictionary<string, List<ChronicleRuleDef>>())
        {
            string symbol = kv.Key.Trim();
            var pool = new Pool();
            if (symbol.EndsWith("!", StringComparison.Ordinal)) { pool.Replace = true; symbol = symbol.Substring(0, symbol.Length - 1); }
            if (!ChroniclePredicate.IsName(symbol)) { _problems.Add($"{path}: bad symbol name '{kv.Key}'."); continue; }
            if (file.ContainsKey(symbol)) { _problems.Add($"{path}: symbol '{symbol}' appears twice."); continue; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in kv.Value ?? new List<ChronicleRuleDef>())
            {
                string where = $"{path}: {symbol}/{def.Id}";
                if (string.IsNullOrWhiteSpace(def.Id)) { _problems.Add($"{path}: a rule of '{symbol}' has no id."); continue; }
                if (!ids.Add(def.Id)) { _problems.Add($"{where}: duplicate id."); continue; }
                if (!(def.W > 0) || double.IsInfinity(def.W)) { _problems.Add($"{where}: weight must be positive."); continue; }
                if (!ChronicleTemplate.TryParse(def.T, out var template, out var terr)) { _problems.Add($"{where}: {terr}."); continue; }
                var when = new List<ChroniclePredicate>();
                bool ok = true;
                foreach (var w in def.When ?? new List<string>())
                {
                    if (ChroniclePredicate.TryParse(w, out var p, out var perr)) when.Add(p!);
                    else { _problems.Add($"{where}: predicate '{w}': {perr}."); ok = false; }
                }
                if (!ok) continue;
                pool.Rules.Add(new ChronicleRule(symbol, def.Id.Trim(), when.ToArray(), def.W, def.Cd, template!, culture));
            }
            file[symbol] = pool;
        }
        _grammar[culture] = file;
    }

    private void LoadLexicon(IDeserializer des, string path, string culture, string yaml)
    {
        Dictionary<string, List<string>>? doc;
        try
        {
            doc = des.Deserialize<Dictionary<string, List<string>>>(yaml);
        }
        catch (Exception ex)
        {
            _problems.Add($"{path}: invalid YAML: {ex.Message}");
            return;
        }
        var file = new Dictionary<string, WordList>(StringComparer.Ordinal);
        foreach (var kv in doc ?? new Dictionary<string, List<string>>())
        {
            string key = kv.Key.Trim();
            var list = new WordList();
            if (key.EndsWith("!", StringComparison.Ordinal)) { list.Replace = true; key = key.Substring(0, key.Length - 1); }
            if (!ChroniclePredicate.IsName(key)) { _problems.Add($"{path}: bad list key '{kv.Key}'."); continue; }
            if (file.ContainsKey(key)) { _problems.Add($"{path}: list '{key}' appears twice."); continue; }
            foreach (var w in kv.Value ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(w)) { _problems.Add($"{path}: {key}: blank word."); continue; }
                if (!ChronicleTemplate.TryParse(w, out _, out var err)) { _problems.Add($"{path}: {key}: '{w}': {err}."); continue; }
                if (!list.Words.Contains(w, StringComparer.Ordinal)) list.Words.Add(w.Trim());
            }
            file[key] = list;
        }
        _lexicon[culture] = file;
    }

    private static string? ReadEmbedded(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
