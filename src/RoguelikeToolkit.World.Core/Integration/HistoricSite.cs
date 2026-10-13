using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// A game-owned historic site projected onto the world grid. Produced and owned
/// by the game's history simulation; the library only consumes it at query time.
/// </summary>
public sealed class HistoricSite
{
    public string Id { get; init; } = string.Empty;
    public HistoricSiteKind Kind { get; init; } = HistoricSiteKind.Other;
    public int TileIndex { get; init; } = -1;
    public GeoCoord Position { get; init; }
    /// <summary>Added to tile danger. Ruins/battlefields are positive, shrines may be negative.</summary>
    public float DangerDelta { get; init; }
    /// <summary>Added to city habitability score. Ruins often slightly positive (roads), battlefields negative.</summary>
    public float HabitabilityDelta { get; init; }
    public Dictionary<string, string> Metadata { get; } = new();
}
