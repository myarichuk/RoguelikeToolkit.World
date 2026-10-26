namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// One parsed <c>when</c> condition of a rule, tested against an event's <see cref="ChronicleFacts"/>:
/// <list type="bullet">
/// <item><c>key=value</c> holds when the fact has that value (<c>key=a|b</c>: any of them; facts such as <c>beat</c> and
/// <c>tag</c> carry several values);</item>
/// <item><c>key!=value</c> (or a quoted <c>"!key=value"</c>) holds when it has none of them;</item>
/// <item><c>key</c> holds when the fact is present at all, <c>"!key"</c> when it is absent.</item>
/// </list>
/// Values compare ordinally, ignoring case.
/// </summary>
internal sealed class ChroniclePredicate
{
    private ChroniclePredicate(string key, string[] values, bool negated, string source)
    {
        Key = key;
        Values = values;
        Negated = negated;
        Source = source;
    }

    /// <summary>The fact the predicate reads.</summary>
    public string Key { get; }
    /// <summary>Accepted values; empty for a presence test.</summary>
    public string[] Values { get; }
    /// <summary>True for <c>!=</c> and <c>!key</c>.</summary>
    public bool Negated { get; }
    /// <summary>The text it was parsed from.</summary>
    public string Source { get; }

    /// <summary>Parses <paramref name="text"/>; false (with a reason) when it is malformed.</summary>
    public static bool TryParse(string? text, out ChroniclePredicate? predicate, out string error)
    {
        predicate = null;
        error = string.Empty;
        string s = (text ?? string.Empty).Trim();
        if (s.Length == 0) { error = "empty predicate"; return false; }
        bool negated = false;
        if (s[0] == '!') { negated = true; s = s.Substring(1).Trim(); }
        string key;
        string[] values;
        int ne = s.IndexOf("!=", StringComparison.Ordinal);
        int eq = s.IndexOf('=');
        if (ne >= 0)
        {
            if (negated) { error = "double negation"; return false; }
            negated = true;
            key = s.Substring(0, ne).Trim();
            values = s.Substring(ne + 2).Split('|');
        }
        else if (eq >= 0)
        {
            key = s.Substring(0, eq).Trim();
            values = s.Substring(eq + 1).Split('|');
        }
        else
        {
            key = s;
            values = Array.Empty<string>();
        }
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = values[i].Trim();
            if (values[i].Length == 0) { error = "empty value"; return false; }
        }
        if (key.Length == 0 || !IsName(key)) { error = $"bad key '{key}'"; return false; }
        predicate = new ChroniclePredicate(key, values, negated, text!.Trim());
        return true;
    }

    /// <summary>True when the predicate holds for <paramref name="facts"/>.</summary>
    public bool Holds(ChronicleFacts facts)
    {
        bool hit;
        if (Values.Length == 0) hit = facts.Has(Key);
        else
        {
            hit = false;
            foreach (var v in Values)
                if (facts.HasValue(Key, v)) { hit = true; break; }
        }
        return hit != Negated;
    }

    /// <summary>True for a fact or symbol name: letters, digits, <c>_</c>, <c>.</c> and <c>-</c>.</summary>
    internal static bool IsName(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-')) return false;
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Source;
}
