namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>What a commander's epithet does in battle (see <see cref="BattleDef.Epithets"/>).</summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class BattleEpithetDef
{
    /// <summary>Added to the spread of the battle's margin (recklessness widens it, caution narrows it).</summary>
    public double Spread { get; set; }
    /// <summary>Added to the commander's chance to fall.</summary>
    public double Death { get; set; }
    /// <summary>Added to the commander's initiative.</summary>
    public double Initiative { get; set; }
    /// <summary>Tactic name to a multiplier on its draw weight for this commander's side.</summary>
    public Dictionary<string, double> Tactics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
