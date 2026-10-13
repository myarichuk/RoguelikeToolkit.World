using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

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
