namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// One way of fighting (<c>Peoples/Data/Tactics/*.yaml</c>, Key = file name). A side draws its tactic by its doctrine weight
/// (<see cref="WarfareDef.Doctrine"/>) times how well the tactic suits the ground and season; the tactic then scales the side's
/// power by <see cref="TerrainFit"/>, <see cref="Seasons"/> and <see cref="Counters"/>. Terrain class, season and tactic names
/// are open vocabularies (terrain classes come from <see cref="BattleDef.TerrainDefence"/>); a missing entry reads 1.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class TacticDef
{
    /// <summary>Tactic name, matching the file name.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Arm that fights it (<c>foot</c>, <c>horse</c>, <c>missile</c>, <c>beast</c>, <c>siege</c>, <c>naval</c>...); picks the side's signature unit.</summary>
    public string Arm { get; set; } = string.Empty;
    /// <summary>Battle kinds it can be used in (<c>Field</c>, <c>Relief</c>, <c>Siege</c>, <c>Naval</c>); empty means field and relief battles.</summary>
    public List<string> Kinds { get; set; } = new();
    /// <summary>Only the side that won the manoeuvre may use it; an attacker or defender using it makes the battle an ambush.</summary>
    public bool Initiative { get; set; }
    /// <summary>Multiplier on the user's power (a surprise), &gt; 0.</summary>
    public double Surprise { get; set; } = 1.0;
    /// <summary>Multiplier on the enemy defender's terrain bonus while this tactic is used against it (0..1 strips cover).</summary>
    public double Cover { get; set; } = 1.0;
    /// <summary>Power multiplier per terrain class (0.5..1.5).</summary>
    public Dictionary<string, double> TerrainFit { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Power multiplier against the named enemy tactic (0.7..1.3).</summary>
    public Dictionary<string, double> Counters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Power multiplier per season (<c>Spring</c>, <c>Summer</c>, <c>Autumn</c>, <c>Winter</c>).</summary>
    public Dictionary<string, double> Seasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Turning-point beats a winner fighting this way may draw (weights and conditions from <see cref="BattleDef.Beats"/>).</summary>
    public List<string> Beats { get; set; } = new();
}
