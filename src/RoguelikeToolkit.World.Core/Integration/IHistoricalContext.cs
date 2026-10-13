using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// Hook surface for Caves-of-Qud-style history integration. Implemented by the
/// game; the library never generates history itself. Return false / empty when
/// a tile has no history.
/// </summary>
public interface IHistoricalContext
{
    bool TryGetTileModifier(int worldTileIndex, out TileHistoryModifier modifier);
    IEnumerable<HistoricSite> GetSitesNear(GeoCoord position, double radiusKm);
}
