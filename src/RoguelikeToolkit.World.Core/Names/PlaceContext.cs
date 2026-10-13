namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Map context a place name should reflect (water, woods, relief, resources).</summary>
public sealed class PlaceContext
{
    /// <summary>Water is close: favours water, river and lake words.</summary>
    public bool NearWater { get; init; }
    /// <summary>Woods are close: favours forest words.</summary>
    public bool NearForest { get; init; }
    /// <summary>The forest is a dark one: favours dark and forest words.</summary>
    public bool DarkForest { get; init; }
    /// <summary>High ground: favours high, mountain and hill words.</summary>
    public bool Highland { get; init; }
    /// <summary>Valley terrain: favours valley words.</summary>
    public bool Valley { get; init; }
    /// <summary>A resource deposit is present: favours deposit words.</summary>
    public bool HasDeposit { get; init; }
    /// <summary>The place is hazardous: favours danger words.</summary>
    public bool Dangerous { get; init; }
    /// <summary>Informational kind (a <see cref="PlaceKinds"/> value). <see cref="FantasyNameGenerator.GeneratePlace"/> ignores it and uses its own <c>kind</c> argument; callers pass this value along.</summary>
    public string Kind { get; init; } = PlaceKinds.Village;
}
