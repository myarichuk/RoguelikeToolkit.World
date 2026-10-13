using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// Hook surface for Caves-of-Qud-style history integration. Implemented by the
/// game; the library never generates history itself. Return false / empty when
/// a tile has no history.
/// </summary>
public interface IHistoricalContext
{
    /// <summary>Looks up the history modifier of one planet tile.</summary>
    /// <param name="worldTileIndex">Planet tile index.</param>
    /// <param name="modifier">The tile's modifier when found.</param>
    /// <returns>False when the tile has no history; <paramref name="modifier"/> is then ignored.</returns>
    bool TryGetTileModifier(int worldTileIndex, out TileHistoryModifier modifier);
    /// <summary>Returns the historic sites within a radius of a position.</summary>
    /// <param name="position">Center of the search.</param>
    /// <param name="radiusKm">Search radius in km.</param>
    /// <returns>The matching sites; empty when none.</returns>
    IEnumerable<HistoricSite> GetSitesNear(GeoCoord position, double radiusKm);
}
