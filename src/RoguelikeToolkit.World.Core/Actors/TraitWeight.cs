namespace RoguelikeToolkit.World.Core.Actors;

/// <summary>
/// How strongly a folk's personality prior pulls one OCEAN trait.
/// <c>Low</c> rolls in the bottom half of the byte range, <c>Balanced</c> rolls
/// the full range, <c>High</c> rolls in the top half. Weights bias, never gate:
/// any value in the half stays reachable.
/// </summary>
public enum TraitWeight
{
    /// <summary>Trait rolls 0-127.</summary>
    Low = 0,
    /// <summary>Trait rolls 0-255.</summary>
    Balanced = 1,
    /// <summary>Trait rolls 128-255.</summary>
    High = 2,
}
