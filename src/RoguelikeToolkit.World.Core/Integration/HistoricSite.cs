using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// A game-owned historic site projected onto the world grid. Produced and owned
/// by the game's history simulation; the library only consumes it at query time.
/// </summary>
public sealed class HistoricSite
{
    /// <summary>Game-assigned identifier; <see cref="HistoricSiteIndex.FromContext"/> de-duplicates sites by this value.</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>What sort of historic site this is.</summary>
    public HistoricSiteKind Kind { get; init; } = HistoricSiteKind.Other;
    /// <summary>Planet tile the site sits on, or -1 when it is not attached to a tile (such sites are not returned by <see cref="HistoricSiteIndex.SitesOnTile"/>).</summary>
    public int TileIndex { get; init; } = -1;
    /// <summary>Geographic position of the site.</summary>
    public GeoCoord Position { get; init; }
    /// <summary>Added to tile danger. Ruins/battlefields are positive, shrines may be negative.</summary>
    public float DangerDelta { get; init; }
    /// <summary>Added to city habitability score. Ruins often slightly positive (roads), battlefields negative.</summary>
    public float HabitabilityDelta { get; init; }
    /// <summary>Free-form game-side key/value data carried with the site; the library never reads it.</summary>
    public Dictionary<string, string> Metadata { get; } = new();
}
