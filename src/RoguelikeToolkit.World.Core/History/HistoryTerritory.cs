using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
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
    /// Assigns every tile an owner: the index into <paramref name="capitals"/> of the capital with the cheapest path to
    /// it (multi-source shortest path; ties to the lowest capital index, then the lowest tile), or -1 for water and
    /// unreachable land. A step costs more where the ground resists a state's reach: climbing, high mountains, a river
    /// to cross, desert, marsh, ice (<see cref="StepCost"/>), so borders settle on ranges, rivers and wastes instead of
    /// the straight bisectors of a plain hop count. Pins (<paramref name="pinnedTiles"/>, tile to owner) win over the
    /// flood and are ignored on water, on out-of-range tiles and on capital tiles (a capital always keeps its own
    /// polity). Pure function of its inputs.
    /// </summary>
    /// <param name="store">Tile store (elevation for the land mask and terrain, hydrology and biomes when registered, adjacency).</param>
    /// <param name="capitals">Capital tiles in polity-id order.</param>
    /// <param name="pinnedTiles">Forced tile ownership, applied before the flood.</param>
    public static int[] AssignOwners(WorldDataStore store, IReadOnlyList<int> capitals, IReadOnlyDictionary<int, int> pinnedTiles)
    {
        int n = store.TileCount;
        var owners = new int[n];
        for (int i = 0; i < owners.Length; i++) owners[i] = -1;
        if (n == 0) return owners;

        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        bool hasBiome = store.IsLayerRegistered<BiomeInfo>();
        var biome = hasBiome ? store.GetSpan<BiomeInfo>() : default;

        if (pinnedTiles != null)
        {
            // A capital tile always belongs to its own polity: a pin on it would leave that polity landless.
            var capitalTiles = new HashSet<int>(capitals);
            foreach (var kv in pinnedTiles)
            {
                if ((uint)kv.Key >= (uint)n || capitalTiles.Contains(kv.Key)) continue;
                if (kv.Value < 0 || kv.Value >= capitals.Count) continue;
                if (hasElev && elev[kv.Key].Height < LandThreshold) continue;
                owners[kv.Key] = kv.Value;
            }
        }

        if (capitals.Count == 0) return owners;

        // Dijkstra over land; the key orders by cost, then owner, then tile, so the result never depends on heap shape.
        var cost = new double[n];
        for (int i = 0; i < n; i++) cost[i] = double.PositiveInfinity;
        var settled = new bool[n];
        var queue = new PriorityQueue<int, (double Cost, int Owner, int Tile)>();
        for (int id = 0; id < capitals.Count; id++)
        {
            int capital = capitals[id];
            if ((uint)capital >= (uint)n) continue;
            if (hasElev && elev[capital].Height < LandThreshold) continue;
            if (owners[capital] != -1) continue;
            owners[capital] = id;
            cost[capital] = 0;
            queue.Enqueue(capital, (0, id, capital));
        }

        Span<int> neighbors = stackalloc int[6];
        while (queue.TryDequeue(out int tile, out var key))
        {
            if (settled[tile] || key.Cost > cost[tile] || key.Owner != owners[tile]) continue; // stale entry
            settled[tile] = true;
            int adjacent = store.GetAdjacent(tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int next = neighbors[k];
                if (settled[next]) continue;
                if (hasElev && elev[next].Height < LandThreshold) continue;
                if (pinnedTiles != null && owners[next] != -1 && cost[next] == double.PositiveInfinity) continue; // a pin
                double c = key.Cost + StepCost(tile, next, elev, hydro, biome);
                if (c < cost[next] || (c == cost[next] && key.Owner < owners[next]))
                {
                    cost[next] = c;
                    owners[next] = key.Owner;
                    queue.Enqueue(next, (c, key.Owner, next));
                }
            }
        }

        return owners;
    }

    /// <summary>
    /// Cost of carrying a state's reach from <paramref name="from"/> into <paramref name="to"/>: one per step, plus the climb,
    /// plus high ground (a range is a frontier), plus crossing onto a river, scaled by how hostile the ground is to hold
    /// (desert, marsh, tundra, ice; forest a little). Missing layers contribute nothing, which is the plain hop count.
    /// </summary>
    internal static double StepCost(int from, int to, ReadOnlySpan<ElevationInfo> elev, ReadOnlySpan<HydrologyInfo> hydro, ReadOnlySpan<BiomeInfo> biome)
    {
        double c = 1.0;
        if (!elev.IsEmpty)
        {
            double rise = Math.Abs(elev[to].Height - elev[from].Height);
            c += 6.0 * rise;
            double high = elev[to].Height - 0.35;
            if (high > 0) c += 4.0 * high;
        }
        if (!hydro.IsEmpty && hydro[to].IsRiver == 1 && hydro[from].IsRiver == 0) c += 1.0;
        if (!biome.IsEmpty) c *= Hostility(biome[to].Biome);
        return c;
    }

    private static double Hostility(BiomeType biome) => biome switch
    {
        BiomeType.Glacier => 2.5,
        BiomeType.Desert or BiomeType.RockDesert or BiomeType.SaltFlat or BiomeType.ColdDesert or BiomeType.Badlands => 1.8,
        BiomeType.Swamp or BiomeType.Tundra or BiomeType.Mountain or BiomeType.Canyon => 1.6,
        BiomeType.Jungle => 1.4,
        BiomeType.Forest => 1.15,
        _ => 1.0,
    };
}
