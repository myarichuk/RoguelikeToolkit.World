namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>Per-tile history modifier resolved from an <see cref="IHistoricalContext"/>.</summary>
public struct TileHistoryModifier
{
    /// <summary>Added to the tile's danger level; rounded to the nearest integer when applied.</summary>
    public float DangerDelta;
    /// <summary>Added to the tile's city-site habitability score.</summary>
    public float HabitabilityDelta;
}
