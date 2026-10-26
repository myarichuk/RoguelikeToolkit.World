namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// Outlaw bands (<c>Peoples/Data/Outlaws/default.yaml</c>; the history reads <c>default</c>): where they come from, where they
/// hide, how often they raid the road they work, and how they end. Safety and traffic are route states (0..1).
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class OutlawDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;

    // Sources.
    /// <summary>Displaced people left unabsorbed this many turns may turn outlaw ...</summary>
    public int DisplacedTurns { get; set; } = 2;
    /// <summary>... with this chance per group per turn.</summary>
    public double DisplacedChance { get; set; } = 0.3;
    /// <summary>Chance per side routed in a decisive battle last turn that its deserters form a band.</summary>
    public double DeserterChance { get; set; } = 0.7;
    /// <summary>Chance per side beaten (not routed) last turn that its deserters form a band.</summary>
    public double DefeatChance { get; set; } = 0.3;
    /// <summary>Chance the people of a settlement captured or razed last turn form a band.</summary>
    public double DispossessedChance { get; set; } = 0.3;
    /// <summary>Chance a ruined merchant turns outlaw.</summary>
    public double RuinedChance { get; set; } = 0.5;
    /// <summary>A band forms only when an unworked route passes within this angle of where it comes from.</summary>
    public double RouteDegrees { get; set; } = 20;
    /// <summary>Terrain classes (the battle model's) a band hides in.</summary>
    public List<string> HavenTerrains { get; set; } = new();
    /// <summary>The haven lies within this angle of the road the band works.</summary>
    public double HavenDegrees { get; set; } = 6;
    /// <summary>No outlaw is created within this many of the world's person cap (the court keeps the rest).</summary>
    public int PersonReserve { get; set; } = 512;
    /// <summary>A new outlaw is this share of a lifespan old, at least ...</summary>
    public double AgeMin { get; set; } = 0.2;
    /// <summary>... and at most this.</summary>
    public double AgeMax { get; set; } = 0.45;

    // Raids.
    /// <summary>Raid chance per turn: <c>(1 − safety) × RaidRate × (TrafficFloor + traffic)</c>.</summary>
    public double RaidRate { get; set; } = 0.6;
    /// <summary>See <see cref="RaidRate"/>.</summary>
    public double TrafficFloor { get; set; } = 0.3;
    /// <summary>Share of the victim merchant's wealth a raid takes.</summary>
    public double RaidLoss { get; set; } = 0.25;
    /// <summary>Renown a raid earns the band's leader.</summary>
    public int RaidRenown { get; set; } = 1;
    /// <summary>Raid chance of nameless brigands on a route no band works, as a share of a band's.</summary>
    public double BrigandShare { get; set; } = 0.15;

    // Ends.
    /// <summary>Per-turn chance the keepers hang the leader: <c>HangBase + HangSafety × safety + HangForts × forts</c>.</summary>
    public double HangBase { get; set; } = 0.04;
    /// <summary>See <see cref="HangBase"/>.</summary>
    public double HangSafety { get; set; } = 0.2;
    /// <summary>See <see cref="HangBase"/>.</summary>
    public double HangForts { get; set; } = 0.05;
    /// <summary>Per-turn chance the keeper's ruler pardons the band, × the ruler's agreeableness (0..1).</summary>
    public double PardonChance { get; set; } = 0.08;
}
