namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>What a custom of war does (see <see cref="BattleDef.Customs"/>, <see cref="WarfareDef.Customs"/>).</summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class BattleCustomDef
{
    /// <summary>Multiplier on the pursuit losses this side inflicts when it wins decisively.</summary>
    public double Pursuit { get; set; } = 1.0;
    /// <summary>Share of enemy commanders who would fall that are taken for ransom instead (0..1).</summary>
    public double Capture { get; set; }
    /// <summary>Added to this side's morale.</summary>
    public double Morale { get; set; }
    /// <summary>A settlement this side would capture is razed instead.</summary>
    public bool Raze { get; set; }
    /// <summary>Multiplier on the casualties this side's victories inflict on the loser's settlements.</summary>
    public double Casualties { get; set; } = 1.0;
}
