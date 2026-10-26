namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>A compiled grammar rule: one alternative of a symbol, with its conditions, weight, cooldown and template.</summary>
internal sealed class ChronicleRule
{
    /// <summary>Builds a rule.</summary>
    public ChronicleRule(string symbol, string id, ChroniclePredicate[] when, double weight, int cooldown, ChronicleTemplate template, string origin)
    {
        Symbol = symbol;
        Id = id;
        When = when;
        Weight = weight;
        Cooldown = cooldown;
        Template = template;
        Origin = origin;
    }

    /// <summary>The symbol the rule expands.</summary>
    public string Symbol { get; }
    /// <summary>Rule id, unique within its symbol.</summary>
    public string Id { get; }
    /// <summary>Conditions, all of which must hold.</summary>
    public ChroniclePredicate[] When { get; }
    /// <summary>Draw weight (&gt; 0).</summary>
    public double Weight { get; }
    /// <summary>Uses of the symbol during which the rule cools after it was drawn, or -1 for the symbol default.</summary>
    public int Cooldown { get; }
    /// <summary>The template.</summary>
    public ChronicleTemplate Template { get; }
    /// <summary>The file the rule came from (<c>common</c> or a culture key).</summary>
    public string Origin { get; }

    /// <summary>True when every condition holds for <paramref name="facts"/>.</summary>
    public bool Matches(ChronicleFacts facts)
    {
        foreach (var p in When)
            if (!p.Holds(facts)) return false;
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Symbol}/{Id}";
}
