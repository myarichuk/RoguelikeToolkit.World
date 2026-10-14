namespace RoguelikeToolkit.World.Core.Lineage;

/// <summary>
/// Pairwise folk compatibility: symmetric, string-keyed, code-built. Unlisted pairs
/// default to <see cref="CompatibilityRule.Blend"/>; a folk is always blend-compatible
/// with itself. Populate during load (for example from Peoples' compat data), then
/// treat as read-only.
/// </summary>
public sealed class CompatibilityMatrix
{
    private readonly Dictionary<(string, string), CompatibilityRule> _rules = new();

    /// <summary>Adds (or replaces) the rule for a folk pair, symmetrically.</summary>
    /// <param name="folkA">First folk key.</param>
    /// <param name="folkB">Second folk key; must differ from <paramref name="folkA"/>.</param>
    /// <param name="rule">Rule for the pair.</param>
    /// <exception cref="ArgumentException">A folk key is blank, or both keys are the same folk.</exception>
    public CompatibilityMatrix With(string folkA, string folkB, CompatibilityRule rule)
    {
        if (string.IsNullOrWhiteSpace(folkA)) throw new ArgumentException("Folk key must be non-blank.", nameof(folkA));
        if (string.IsNullOrWhiteSpace(folkB)) throw new ArgumentException("Folk key must be non-blank.", nameof(folkB));
        if (folkA.Equals(folkB, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A folk is always blend-compatible with itself; no row needed.", nameof(folkB));
        _rules[Key(folkA, folkB)] = rule;
        return this;
    }

    /// <summary>Resolves the rule for a pair: self-pairs blend, unlisted pairs blend.</summary>
    /// <param name="folkA">First folk key.</param>
    /// <param name="folkB">Second folk key.</param>
    /// <exception cref="ArgumentException">A folk key is blank.</exception>
    public CompatibilityRule Resolve(string folkA, string folkB)
    {
        if (string.IsNullOrWhiteSpace(folkA)) throw new ArgumentException("Folk key must be non-blank.", nameof(folkA));
        if (string.IsNullOrWhiteSpace(folkB)) throw new ArgumentException("Folk key must be non-blank.", nameof(folkB));
        if (folkA.Equals(folkB, StringComparison.OrdinalIgnoreCase)) return CompatibilityRule.Blend;
        return _rules.TryGetValue(Key(folkA, folkB), out var rule) ? rule : CompatibilityRule.Blend;
    }

    /// <summary>
    /// Union check for a folk pair: false (and <paramref name="rule"/> set to
    /// <see cref="CompatibilityRule.Sterile"/>) when the pair is sterile, otherwise true.
    /// </summary>
    /// <param name="folkA">First folk key.</param>
    /// <param name="folkB">Second folk key.</param>
    /// <param name="rule">The resolved rule.</param>
    public bool TryUnion(string folkA, string folkB, out CompatibilityRule rule)
    {
        rule = Resolve(folkA, folkB);
        return rule != CompatibilityRule.Sterile;
    }

    private static (string, string) Key(string a, string b)
    {
        var x = a.ToLowerInvariant();
        var y = b.ToLowerInvariant();
        return string.CompareOrdinal(x, y) <= 0 ? (x, y) : (y, x);
    }
}
