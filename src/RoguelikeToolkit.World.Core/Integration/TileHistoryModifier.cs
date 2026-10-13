namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>Per-tile history modifier resolved from an <see cref="IHistoricalContext"/>.</summary>
public struct TileHistoryModifier
{
    public float DangerDelta;
    public float HabitabilityDelta;
}
