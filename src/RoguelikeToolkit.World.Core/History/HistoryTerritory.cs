using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Tile-ownership projection: the single deterministic function mapping capitals (plus user pins)
/// onto a per-tile owner map. <see cref="PolityFormStage"/> builds the first projection;
/// <see cref="HistoryCatalog.Respatialize"/> re-runs it after every map edit, so a null edit maps
/// every tile back to its old owner and never flickers the fork marker.
/// </summary>
public static class HistoryTerritory
{
    /// <summary>Tile height at or above this is land and can be owned; below it is water and stays unclaimed.</summary>
    /// <remarks>Matches the settlement scorer: below-sea-level tiles are water, not ground.</remarks>
    public const double LandThreshold = 0.0;

    /// <summary>
    /// Assigns every tile an owner: the index into <paramref name="capitals"/> of the nearest capital by
    /// tile-graph distance (multi-source flood fill, ties to the lowest capital index), or -1 for water
    /// and unreachable land. Pins (<paramref name="pinnedTiles"/>, tile to owner) win over the flood fill
    /// and are ignored on water or out-of-range tiles. Pure function of its inputs.
    /// </summary>
    /// <param name="store">Tile store (elevation span for the land mask, adjacency for the flood fill).</param>
    /// <param name="capitals">Capital tiles in polity-id order.</param>
    /// <param name="pinnedTiles">Forced tile ownership, applied before the flood fill.</param>
    public static int[] AssignOwners(WorldDataStore store, IReadOnlyList<int> capitals, IReadOnlyDictionary<int, int> pinnedTiles)
    {
        int n = store.TileCount;
        var owners = new int[n];
        for (int i = 0; i < owners.Length; i++) owners[i] = -1;
        if (n == 0) return owners;

        var elev = store.IsLayerRegistered<ElevationInfo>() ? store.GetSpan<ElevationInfo>() : default;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();


        if (pinnedTiles != null)
        {
            foreach (var kv in pinnedTiles)
            {
                if ((uint)kv.Key >= (uint)n) continue;
                if (kv.Value < 0 || kv.Value >= capitals.Count) continue;
                if (hasElev && elev[kv.Key].Height < LandThreshold) continue;
                owners[kv.Key] = kv.Value;
            }
        }

        if (capitals.Count == 0) return owners;

        var queue = new Queue<int>();
        for (int id = 0; id < capitals.Count; id++)
        {
            int capital = capitals[id];
            if ((uint)capital >= (uint)n) continue;
            if (hasElev && elev[capital].Height < LandThreshold) continue;
            if (owners[capital] != -1) continue;
            owners[capital] = id;
            queue.Enqueue(capital);
        }

        Span<int> neighbors = stackalloc int[6];
        while (queue.Count > 0)
        {
            int tile = queue.Dequeue();
            int owner = owners[tile];
            int adjacent = store.GetAdjacent(tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int next = neighbors[k];
                if (owners[next] != -1) continue;
                if (hasElev && elev[next].Height < LandThreshold) continue;
                owners[next] = owner;
                queue.Enqueue(next);
            }
        }

        return owners;
    }
}
