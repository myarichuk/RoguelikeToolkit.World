namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// What one event says, as named facts for the grammar: plain values (names, numbers, predicate values such as
/// <c>season</c> or the several <c>beat</c>s of a battle) and lexicon references (a slot such as <c>terrain.noun</c> that
/// renders as a word drawn from a lexicon list such as <c>terrain.forest</c>). Built by <see cref="ChronicleFactBuilder"/>;
/// read by predicates and slots. A key may hold both: the value for predicates, the reference for rendering.
/// </summary>
internal sealed class ChronicleFacts
{
    /// <summary>A slot rendered from the lexicon.</summary>
    internal readonly struct LexRef
    {
        /// <summary>Builds a reference.</summary>
        public LexRef(string key, string? culture, string fallback)
        {
            Key = key;
            Culture = culture;
            Fallback = fallback;
        }

        /// <summary>Lexicon list to draw from.</summary>
        public string Key { get; }
        /// <summary>Culture whose overlay supplies the list (a battle cry is the winner's), or null for the event's.</summary>
        public string? Culture { get; }
        /// <summary>Text used when no lexicon has the list (an open vocabulary entry data added without a word for it).</summary>
        public string Fallback { get; }
    }

    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LexRef> _lex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _short = new(StringComparer.Ordinal);

    /// <summary>Culture key whose grammar and lexicon overlay render the event (<c>common</c> when it has no polity).</summary>
    public string Culture { get; set; } = ChronicleData.Common;

    /// <summary>Every key with a value or a lexicon reference.</summary>
    public IEnumerable<string> Keys => _values.Keys.Concat(_lex.Keys.Where(k => !_values.ContainsKey(k)));

    /// <summary>Sets <paramref name="key"/> to the single value <paramref name="value"/> (ignored when blank).</summary>
    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _values[key] = new List<string>(1) { value! };
    }

    /// <summary>Adds <paramref name="value"/> to the values of <paramref name="key"/> (ignored when blank).</summary>
    public void Add(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!_values.TryGetValue(key, out var list)) _values[key] = list = new List<string>(2);
        list.Add(value!);
    }

    /// <summary>Makes <paramref name="key"/> render from the lexicon list <paramref name="lexKey"/>.</summary>
    public void SetLex(string key, string lexKey, string fallback, string? culture = null)
        => _lex[key] = new LexRef(lexKey, culture, fallback);

    /// <summary>Gives <paramref name="key"/> a short form for its later mentions in the event (a realm's bare name).</summary>
    public void SetShort(string key, string? text)
    {
        if (!string.IsNullOrEmpty(text)) _short[key] = text!;
    }

    /// <summary>The short form of <paramref name="key"/>.</summary>
    public bool TryShort(string key, out string text)
    {
        if (_short.TryGetValue(key, out var t)) { text = t; return true; }
        text = string.Empty;
        return false;
    }

    /// <summary>True when <paramref name="key"/> has a value or a lexicon reference.</summary>
    public bool Has(string key) => _values.ContainsKey(key) || _lex.ContainsKey(key);

    /// <summary>True when one of the values of <paramref name="key"/> is <paramref name="value"/> (ignoring case).</summary>
    public bool HasValue(string key, string value)
    {
        if (!_values.TryGetValue(key, out var list)) return false;
        foreach (var v in list)
            if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The first value of <paramref name="key"/>.</summary>
    public bool TryValue(string key, out string value)
    {
        if (_values.TryGetValue(key, out var list) && list.Count > 0) { value = list[0]; return true; }
        value = string.Empty;
        return false;
    }

    /// <summary>The values of <paramref name="key"/> (empty when none).</summary>
    public IReadOnlyList<string> Values(string key)
        => _values.TryGetValue(key, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>The lexicon reference of <paramref name="key"/>.</summary>
    public bool TryLex(string key, out LexRef lex) => _lex.TryGetValue(key, out lex);
}
