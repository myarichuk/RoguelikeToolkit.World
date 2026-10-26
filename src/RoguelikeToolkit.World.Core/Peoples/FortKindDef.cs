namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>One kind of fort a people builds (see <see cref="FortificationDef.Kinds"/>, <see cref="WarfareDef.FortKind"/>).</summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class FortKindDef
{
    /// <summary>Wall strength of the fort (0 none, 1 a stone keep; more for a hold cut into rock).</summary>
    public double Walls { get; set; } = 1.0;
    /// <summary>Fighting strength (levy units) its garrison adds to a defence nearby and holds the fort with.</summary>
    public double Garrison { get; set; } = 100;
    /// <summary>Added to the defender's chance to be warned of an invasion within <see cref="FortificationDef.WatchDegrees"/>.</summary>
    public double Watch { get; set; } = 0.2;
}
