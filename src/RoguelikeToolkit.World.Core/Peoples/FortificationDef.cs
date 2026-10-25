namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// Walls and forts (<c>Peoples/Data/Fortification/default.yaml</c>; the history reads <c>default</c>): which forts there are, when
/// and where a polity builds one (a hostile border, an invasion, an unsafe road), how they guard roads and defences, and how
/// strong a settlement's walls are. A people's appetite for all of it is <see cref="WarfareDef.Fortify"/>. Degrees are
/// great-circle angles.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class FortificationDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Fort kinds by name.</summary>
    public Dictionary<string, FortKindDef> Kinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Kind built by a people whose way of war names none (or an unknown one).</summary>
    public string DefaultKind { get; set; } = string.Empty;

    // Building.
    /// <summary>Per-turn chance to build at full need and <c>Fortify</c> 100.</summary>
    public double FortChance { get; set; } = 0.08;
    /// <summary>Need from a bordering polity it holds a grudge against.</summary>
    public double ThreatNeed { get; set; } = 1.0;
    /// <summary>Need from having been invaded within <see cref="RecentTurns"/>.</summary>
    public double InvadedNeed { get; set; } = 1.0;
    /// <summary>Need from an own road no fort guards.</summary>
    public double RouteNeed { get; set; } = 0.3;
    /// <summary>Turns an invasion keeps a polity wary.</summary>
    public int RecentTurns { get; set; } = 3;
    /// <summary>Forts a polity holds at most: <c>ceil(MaxForts × Fortify/100)</c>, at least one.</summary>
    public int MaxForts { get; set; } = 2;
    /// <summary>A polity builds forts only once it holds this many settlements that are not posts.</summary>
    public int MinSettlements { get; set; } = 2;
    /// <summary>A fort stands at least this far from any settlement.</summary>
    public double SpacingDegrees { get; set; } = 3;
    /// <summary>Height (sea level 0) from which a site counts as high ground, preferred for a fort.</summary>
    public double HighFrom { get; set; } = 0.2;

    // Guarding.
    /// <summary>A fort guards a road with a segment end within this angle.</summary>
    public double GuardDegrees { get; set; } = 5;
    /// <summary>A fort watches for an invasion within this angle of the field.</summary>
    public double WatchDegrees { get; set; } = 8;
    /// <summary>A fort's garrison joins a defence <c>d</c> degrees away with <c>exp(-d/GarrisonDegrees)</c> of its strength.</summary>
    public double GarrisonDegrees { get; set; } = 6;

    // Road safety (the state outlaws will read).
    /// <summary>Route safety each fort guarding the route adds to the level it drifts to.</summary>
    public double FortSafety { get; set; } = 0.15;
    /// <summary>Most forts counted per route.</summary>
    public int SafetyForts { get; set; } = 3;
    /// <summary>Safety a severed route loses from the level it drifts to.</summary>
    public double SeveredSafety { get; set; } = 0.2;
    /// <summary>Share of the gap to that level closed each turn.</summary>
    public double SafetyDrift { get; set; } = 0.3;

    // Walls.
    /// <summary>Wall strength per settlement tier name (<c>Hamlet</c>, <c>Village</c>, <c>Town</c>, <c>City</c>).</summary>
    public Dictionary<string, double> TierWalls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extra wall strength of a capital.</summary>
    public double CapitalWalls { get; set; } = 0.2;
    /// <summary>Walls are multiplied by <c>WallFloor + WallFortify × Fortify/100</c> of the holder's people.</summary>
    public double WallFloor { get; set; } = 0.4;
    /// <summary>See <see cref="WallFloor"/>.</summary>
    public double WallFortify { get; set; } = 0.8;
    /// <summary>Wall strength of standing on high ground (height at or above <see cref="HighFrom"/>).</summary>
    public double HighWalls { get; set; } = 0.2;
    /// <summary>A place with weaker walls than this cannot stand a siege: it is taken (or sacked) outright.</summary>
    public double SiegeWalls { get; set; } = 0.3;
}
