using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Extensible historic-site kinds. Games can map their own events onto these.</summary>
public enum HistoricSiteKind : byte
{
    Settlement = 0,
    Ruin = 1,
    Battlefield = 2,
    Shrine = 3,
    Other = 255
}

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

/// <summary>Per-tile history modifier resolved from an <see cref="IHistoricalContext"/>.</summary>
public struct TileHistoryModifier
{
    public float DangerDelta;
    public float HabitabilityDelta;
}

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

/// <summary>Query-time options. Null History means pure geography.</summary>
public sealed class QueryOptions
{
    public IHistoricalContext? History { get; init; }

    public static QueryOptions Default { get; } = new QueryOptions();
}

/// <summary>
/// Adapter indexing external historic sites by tile for uniform radius queries.
/// </summary>
public sealed class HistoricSiteIndex
{
    private readonly Dictionary<int, List<HistoricSite>> _byTile = new();
    private readonly List<HistoricSite> _all = new();

    public void Add(HistoricSite site)
    {
        _all.Add(site);
        if (site.TileIndex >= 0)
        {
            if (!_byTile.TryGetValue(site.TileIndex, out var list))
            {
                list = new List<HistoricSite>();
                _byTile[site.TileIndex] = list;
            }
            list.Add(site);
        }
    }

    public IReadOnlyList<HistoricSite> All => _all;

    public IEnumerable<HistoricSite> SitesOnTile(int tileIndex)
    {
        if (_byTile.TryGetValue(tileIndex, out var list)) return list;
        return Array.Empty<HistoricSite>();
    }

    /// <summary>Builds an index from a context by sampling every tile center. O(n) build.</summary>
    public static HistoricSiteIndex FromContext(IHistoricalContext context, WorldDataStore store, double radiusKm = 400.0)
    {
        var index = new HistoricSiteIndex();
        var seen = new HashSet<string>();
        for (int i = 0; i < store.TileCount; i++)
        {
            var c = store.GetGeoCoord(i);
            foreach (var s in context.GetSitesNear(c, radiusKm))
            {
                if (seen.Add(s.Id)) index.Add(s);
            }
        }
        return index;
    }
}
