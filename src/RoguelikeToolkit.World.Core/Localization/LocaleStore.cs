using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoguelikeToolkit.World.Core.Localization;

/// <summary>One UI language table (flat key/value strings plus layout direction).</summary>
public sealed class LocaleData
{
    public string Language { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool RightToLeft { get; set; }
    public Dictionary<string, string> Strings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Thrown when locale tables fail validation (missing keys, bad YAML).</summary>
public sealed class LocaleDataException : Exception
{
    public LocaleDataException(string message) : base(message) { }
}

/// <summary>
/// Data-driven UI strings. Locale tables ship as embedded YAML
/// (<c>Localization/en.yaml</c>, <c>ru.yaml</c>, <c>he.yaml</c>); every
/// non-English table must carry exactly the English key set, so a missing
/// translation fails fast at load instead of blanking a control at runtime.
/// </summary>
public sealed class LocaleStore
{
    private readonly Dictionary<string, string> _strings;
    private readonly LocaleData _english;

    public string Language { get; }
    public string DisplayName { get; }
    public bool RightToLeft { get; }

    private LocaleStore(LocaleData data, LocaleData english)
    {
        Language = data.Language;
        DisplayName = data.DisplayName;
        RightToLeft = data.RightToLeft;
        _english = english;
        _strings = new Dictionary<string, string>(data.Strings, StringComparer.OrdinalIgnoreCase);
    }

    public string Get(string key)
    {
        if (_strings.TryGetValue(key, out var s)) return s;
        if (_english.Strings.TryGetValue(key, out var e)) return e;
        return $"!{key}!";
    }

    public static IReadOnlyList<(string Language, string DisplayName)> AvailableLanguages()
    {
        var asm = typeof(LocaleStore).Assembly;
        string prefix = asm.GetName().Name + ".Localization.";
        var list = new List<(string, string)>();
        var des = Deserializer();
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                              && n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n))
        {
            var data = des.Deserialize<LocaleData>(ReadEmbedded(asm, name));
            if (data is not null && !string.IsNullOrWhiteSpace(data.Language))
                list.Add((data.Language, string.IsNullOrWhiteSpace(data.DisplayName) ? data.Language : data.DisplayName));
        }
        return list;
    }

    public static LocaleStore Load(string language)
    {
        var all = LoadAll();
        if (!all.TryGetValue(language, out var data))
            throw new LocaleDataException(
                $"Unknown UI language '{language}'. Available: {string.Join(", ", all.Keys.OrderBy(k => k))}.");
        var english = all["en"];
        var missing = english.Strings.Keys.Where(k => !data.Strings.ContainsKey(k)).ToArray();
        var extra = data.Strings.Keys.Where(k => !english.Strings.ContainsKey(k)).ToArray();
        if (missing.Length > 0 || extra.Length > 0)
            throw new LocaleDataException(
                $"Locale '{language}' key mismatch." +
                (missing.Length > 0 ? $" Missing: {string.Join(", ", missing)}." : "") +
                (extra.Length > 0 ? $" Extra: {string.Join(", ", extra)}." : ""));
        return new LocaleStore(data, english);
    }

    private static Dictionary<string, LocaleData> LoadAll()
    {
        var asm = typeof(LocaleStore).Assembly;
        string prefix = asm.GetName().Name + ".Localization.";
        var des = Deserializer();
        var all = new Dictionary<string, LocaleData>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                              && n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n))
        {
            LocaleData? data;
            try
            {
                data = des.Deserialize<LocaleData>(ReadEmbedded(asm, name));
            }
            catch (Exception ex)
            {
                throw new LocaleDataException($"{name}: invalid YAML: {ex.Message}");
            }
            if (data is null || string.IsNullOrWhiteSpace(data.Language))
                throw new LocaleDataException($"{name}: missing Language.");
            all[data.Language] = data;
        }
        if (!all.ContainsKey("en"))
            throw new LocaleDataException("English locale (en.yaml) is missing; it is the fallback.");
        return all;
    }

    private static IDeserializer Deserializer()
        => new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    private static string ReadEmbedded(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name);
        if (s is null) throw new LocaleDataException($"{name}: cannot read embedded resource.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
