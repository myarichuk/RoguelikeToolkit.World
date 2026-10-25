namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// A culture's way of war (<c>Peoples/Data/Warfare/&lt;culture&gt;.yaml</c>, Key = the culture key; <c>default.yaml</c> serves every
/// culture without its own file). Scores are 0-100.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class WarfareDef
{
    /// <summary>Culture key, or <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Tactic name to preference weight (&gt;= 0); the tactic drawn in battle follows these weights.</summary>
    public Dictionary<string, double> Doctrine { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Signature units.</summary>
    public List<WarfareUnitDef> Units { get; set; } = new();
    /// <summary>How far and fast hosts move: initiative, reach, river bypass, pursuit.</summary>
    public double Mobility { get; set; } = 50;
    /// <summary>How well a beaten host holds together: blunts the winner's pursuit.</summary>
    public double Discipline { get; set; } = 50;
    /// <summary>Skill at taking walls (sieges).</summary>
    public double SiegeSkill { get; set; } = 50;
    /// <summary>How well a host lives off the land beyond its supply lines.</summary>
    public double Forage { get; set; } = 50;
    /// <summary>How little winter campaigning hurts.</summary>
    public double ColdTolerance { get; set; } = 50;
    /// <summary>Multiplier on the levy a polity raises (&gt; 0).</summary>
    public double Levy { get; set; } = 1.0;
    /// <summary>Season name to the weight of campaigning in it (&gt;= 0; at least one positive).</summary>
    public Dictionary<string, double> CampaignSeasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>How readily this people fortifies (0-100): the chance and number of forts it builds, and how strong its towns' walls are.</summary>
    public double Fortify { get; set; } = 40;
    /// <summary>The kind of fort it builds (a key of <see cref="FortificationDef.Kinds"/>).</summary>
    public string FortKind { get; set; } = string.Empty;
    /// <summary>How much at home on the sea this people is (0-100): from <see cref="LogisticsDef.SeaFrom"/> its hosts and supply sail.</summary>
    public double Seafaring { get; set; } = 30;
    /// <summary>Customs of war (keys of <see cref="BattleDef.Customs"/>): change model terms and unlock beats.</summary>
    public List<string> Customs { get; set; } = new();
}
