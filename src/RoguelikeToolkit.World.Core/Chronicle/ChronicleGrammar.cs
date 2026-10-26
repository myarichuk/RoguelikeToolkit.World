namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// The grammar and lexicon one culture renders with: the common files with that culture's overlays merged in (see
/// <see cref="ChronicleData"/>). Immutable once built.
/// </summary>
internal sealed class ChronicleGrammar
{
    private readonly Dictionary<string, ChronicleRule[]> _symbols;
    private readonly Dictionary<string, string[]> _lexicon;

    /// <summary>Builds a grammar from merged symbol pools and lexicon lists.</summary>
    public ChronicleGrammar(string culture, Dictionary<string, ChronicleRule[]> symbols, Dictionary<string, string[]> lexicon)
    {
        Culture = culture;
        _symbols = symbols;
        _lexicon = lexicon;
    }

    /// <summary>Culture key (<see cref="ChronicleData.Common"/> for the common grammar).</summary>
    public string Culture { get; }

    /// <summary>Every symbol, in ordinal order.</summary>
    public IEnumerable<string> Symbols => _symbols.Keys.OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>Every lexicon list key, in ordinal order.</summary>
    public IEnumerable<string> LexiconKeys => _lexicon.Keys.OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>The rules of <paramref name="symbol"/>.</summary>
    public bool TryRules(string symbol, out ChronicleRule[] rules)
    {
        if (_symbols.TryGetValue(symbol, out var r)) { rules = r; return true; }
        rules = Array.Empty<ChronicleRule>();
        return false;
    }

    /// <summary>The words of lexicon list <paramref name="key"/>.</summary>
    public bool TryWords(string key, out string[] words)
    {
        if (_lexicon.TryGetValue(key, out var w) && w.Length > 0) { words = w; return true; }
        words = Array.Empty<string>();
        return false;
    }

    /// <summary>True when lexicon list <paramref name="key"/> exists and is not empty.</summary>
    public bool HasWords(string key) => _lexicon.TryGetValue(key, out var w) && w.Length > 0;

    /// <summary>The cooldown of a rule that sets none: half the candidates it competes with.</summary>
    public static int DefaultCooldown(int candidates) => candidates / 2;
}
