namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// How hard ground is to hold: the step costs of the territory flood that turns capitals into borders. A step from one
/// tile to the next costs <see cref="Step"/> + <see cref="Climb"/> × the height difference + <see cref="High"/> × the height
/// above <see cref="HighFrom"/> + <see cref="River"/> when it steps onto a river, all times the target biome's entry in
/// <see cref="Biomes"/> (1 when absent). A culture scales any of these with <see cref="CultureDef.TerrainAffinity"/>.
/// Data only; loaded from <c>Peoples/Data/Territory/</c>, where <c>default.yaml</c> is the one the history uses.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class TerritoryDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Base cost of one step, &gt; 0.</summary>
    public double Step { get; set; } = 1.0;
    /// <summary>Cost per unit of height difference between the two tiles, &gt;= 0.</summary>
    public double Climb { get; set; }
    /// <summary>Cost per unit of height above <see cref="HighFrom"/> on the target tile (a range is a frontier), &gt;= 0.</summary>
    public double High { get; set; }
    /// <summary>Height from which <see cref="High"/> applies.</summary>
    public double HighFrom { get; set; } = 0.35;
    /// <summary>Extra cost of stepping onto a river from dry land, &gt;= 0.</summary>
    public double River { get; set; }
    /// <summary>Multiplier per target biome name (<c>Desert</c>, <c>Swamp</c>, ...), &gt; 0; unlisted biomes are 1.</summary>
    public Dictionary<string, double> Biomes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
