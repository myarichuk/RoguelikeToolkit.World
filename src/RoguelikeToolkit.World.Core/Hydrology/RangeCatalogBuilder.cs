using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>Builds mountain-range and valley features from elevation + rivers.</summary>
public static class RangeCatalogBuilder
{
    public static void Populate(WorldMap map, RangeCatalog catalog, float mountainThreshold = 0.5f, float riverThresholdScale = 1f)
    {
        var store = map.DataStore;
        if (!store.IsLayerRegistered<ElevationInfo>()) return;
        var elev = store.GetSpan<ElevationInfo>();
        int n = store.TileCount;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;

        // Mountain ranges: connected components of high tiles.
        var visited = new bool[n];
        Span<int> scratch = stackalloc int[6];
        int nextId = 0;
        for (int i = 0; i < n; i++)
        {
            if (visited[i] || elev[i].Height < mountainThreshold) continue;
            var feat = new RangeFeature(nextId++, RangeKind.MountainRange);
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                feat.Tiles.Add(cur);
                int adj = store.GetAdjacent(cur, scratch);
                for (int k = 0; k < adj; k++)
                {
                    int nb = scratch[k];
                    if (!visited[nb] && elev[nb].Height >= mountainThreshold)
                    {
                        visited[nb] = true;
                        queue.Enqueue(nb);
                    }
                }
            }
            if (feat.Tiles.Count > 0) catalog.Features.Add(feat);
        }

        // Canyons: arid high-discharge reaches cutting relief, grouped by
        // connectivity. Needs hydrology (flow) and climate (aridity gate).
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        if (hasHydro && hasClimate)
        {
            float riverThreshold = Hydrography.RiverThreshold(n, riverThresholdScale);
            var beds = new float[n];
            for (int i = 0; i < n; i++) beds[i] = elev[i].Height;
            var canyonSeed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (hydro[i].IsRiver != 1) continue;
                float slope = SurfaceDescent(store, hydro, i, scratch);
                float relief = CanyonAnalysis.Relief(store, beds, i, scratch);
                if (CanyonAnalysis.IsCanyonTile(hydro[i].Flow, riverThreshold, climate[i].Precipitation, relief, slope))
                    canyonSeed[i] = true;
            }
            GroupSeeds(store, canyonSeed, catalog, ref nextId, RangeKind.Canyon);
        }

        // Playas: dry basin floors (salt flats) grouped by connectivity.
        if (hasHydro)
        {
            var playaSeed = new bool[n];
            for (int i = 0; i < n; i++) playaSeed[i] = hydro[i].IsPlaya == 1;
            GroupSeeds(store, playaSeed, catalog, ref nextId, RangeKind.Playa);
        }

        // Valleys: river-adjacent land tiles grouped by connectivity.
        if (hasHydro)
        {
            var valleySeed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (hydro[i].IsRiver == 1) continue;
                if (elev[i].Height < ElevationGenerationStage.SeaLevel || elev[i].Height > 0.35f) continue;
                int adj = store.GetAdjacent(i, scratch);
                for (int k = 0; k < adj; k++)
                {
                    if (hydro[scratch[k]].IsRiver == 1) { valleySeed[i] = true; break; }
                }
            }
            GroupSeeds(store, valleySeed, catalog, ref nextId, RangeKind.Valley);
        }
    }

    /// <summary>Groups seeded tiles into connected features of the given kind.</summary>
    private static void GroupSeeds(WorldDataStore store, bool[] seeds, RangeCatalog catalog, ref int nextId, RangeKind kind)
    {
        int n = store.TileCount;
        Span<int> scratch = stackalloc int[6];
        var visited = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (visited[i] || !seeds[i]) continue;
            var feat = new RangeFeature(nextId++, kind);
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                feat.Tiles.Add(cur);
                int adj = store.GetAdjacent(cur, scratch);
                for (int k = 0; k < adj; k++)
                {
                    int nb = scratch[k];
                    if (!visited[nb] && seeds[nb]) { visited[nb] = true; queue.Enqueue(nb); }
                }
            }
            if (feat.Tiles.Count > 0) catalog.Features.Add(feat);
        }
    }

    /// <summary>Channel slope: drop from this tile to its lowest surface neighbor.</summary>
    private static float SurfaceDescent(WorldDataStore store, Span<HydrologyInfo> hydro, int tile, Span<int> scratch)
    {
        float best = hydro[tile].Surface;
        int adjacent = store.GetAdjacent(tile, scratch);
        for (int k = 0; k < adjacent; k++)
        {
            float s = hydro[scratch[k]].Surface;
            if (s < best) best = s;
        }
        return hydro[tile].Surface - best;
    }
}
