namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>A signature unit of a way of war (see <see cref="WarfareDef.Units"/>); a name for prose and records.</summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class WarfareUnitDef
{
    /// <summary>Unit name (<c>wolf_riders</c>...).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Arm it fights in; a side fighting a tactic of this arm fields one of its units of the same arm.</summary>
    public string Arm { get; set; } = string.Empty;
    /// <summary>Draw weight among the units of the same arm, &gt; 0.</summary>
    public double Weight { get; set; } = 1.0;
}
