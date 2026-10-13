namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Map context a place name should reflect (water, woods, relief, resources).</summary>
public sealed class PlaceContext
{
    public bool NearWater { get; init; }
    public bool NearForest { get; init; }
    public bool DarkForest { get; init; }
    public bool Highland { get; init; }
    public bool Valley { get; init; }
    public bool HasDeposit { get; init; }
    public bool Dangerous { get; init; }
    public string Kind { get; init; } = PlaceKinds.Village;
}
