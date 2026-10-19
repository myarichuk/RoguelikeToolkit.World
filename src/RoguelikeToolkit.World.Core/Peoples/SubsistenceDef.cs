namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// One way of making a living (agrarian, pastoral, artisan, ...): which land yields it values, which terrain it
/// seeks, and which resources it works. A culture names its default profile in <see cref="CultureDef.Subsistence"/>;
/// a folk may restrict the profiles open to it with <see cref="FolkDef.Subsistence"/>. Data only; loaded and
/// validated by <see cref="PeoplesStore"/> from <c>Peoples/Data/Subsistence/</c>. M5.1: nothing reads it yet.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class SubsistenceDef
{
    /// <summary>Profile key, matching the file name (case-insensitive); e.g. <c>agrarian</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Human-readable label for pickers. Blank falls back to a title-cased Key.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>True for peoples that live in fixed settlements; false for herders and foragers who range.</summary>
    public bool Sedentary { get; set; } = true;

    /// <summary>Weight on the tile's arable capacity, 0-10.</summary>
    public double Arable { get; set; }
    /// <summary>Weight on the tile's pasture capacity, 0-10.</summary>
    public double Pasture { get; set; }
    /// <summary>Weight on the tile's game capacity, 0-10.</summary>
    public double Game { get; set; }
    /// <summary>Weight on the tile's timber capacity, 0-10.</summary>
    public double Timber { get; set; }
    /// <summary>Weight on the tile's fish capacity, 0-10.</summary>
    public double Fish { get; set; }

    /// <summary>Weight on defensible ground (height, slope, relief, water on several sides), -10-10.</summary>
    public double Defensibility { get; set; }
    /// <summary>Weight on a coast or lake shore, -10-10.</summary>
    public double Coast { get; set; }
    /// <summary>Weight on a river, -10-10.</summary>
    public double River { get; set; }
    /// <summary>Weight on nearness to a road, -10-10.</summary>
    public double Road { get; set; }

    /// <summary>The resources this profile works; each resource key at most once.</summary>
    public List<SubsistenceExploit> Exploits { get; set; } = new();
}
