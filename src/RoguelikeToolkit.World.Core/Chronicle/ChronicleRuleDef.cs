namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// One grammar rule as written in <c>Chronicle/Data/Grammar/*.yaml</c> (keys are lower case:
/// <c>{ id, when, w, cd, t }</c>). Compiled into a <see cref="ChronicleRule"/> by <see cref="ChronicleData"/>.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
internal sealed class ChronicleRuleDef
{
    /// <summary>Rule id, unique within its symbol; an overlay rule with the same id replaces the common one.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Predicates, all of which must hold (see <see cref="ChroniclePredicate"/>).</summary>
    public List<string> When { get; set; } = new();
    /// <summary>Draw weight (&gt; 0; default 1).</summary>
    public double W { get; set; } = 1.0;
    /// <summary>Cooldown in uses of the symbol, or -1 for the default (half the rules it competes with).</summary>
    public int Cd { get; set; } = -1;
    /// <summary>The template text (see <see cref="ChronicleTemplate"/>).</summary>
    public string T { get; set; } = string.Empty;
}
