using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>
/// Fills depressions according to water balance: inflow (runoff caught by the
/// depression's catchment) vs evaporation over the ponded area. Wet depressions
/// fill to their pour point and overflow (open lakes); arid ones pond partially
/// (closed/endorheic lakes) or stay dry (playas/salt flats).
/// <para>
/// The water balance is not hydrology alone. When land cover is supplied (the
/// provisional biome of each tile, see <see cref="LandCover"/>), the loss over
/// a candidate lake is the open-water evaporation scaled by the air above it
/// (hot dry desert air against humid jungle air) plus what the bed leaks into
/// porous ground, and the depth a hollow needs to count as a lake depends on
/// what grows in it (a swamp turns a shallow pond into wetland).
/// </para>
/// </summary>
public static class LakeSolver
{
    /// <summary>Deepest point a ponded depression must reach (height units) to count as a lake rather than a marsh.</summary>
    public const float MinLakeDepth = 0.03f;

    public static LakeSolution Solve(
        WorldDataStore store,
        ReadOnlySpan<float> heights,
        ReadOnlySpan<float> filled,
        ReadOnlySpan<float> flowFilled,
        ReadOnlySpan<int> receiverFilled,
        ReadOnlySpan<float> runoff,
        ReadOnlySpan<float> evap,
        float seaLevel = 0f,
        ReadOnlySpan<BiomeType> cover = default,
        ReadOnlySpan<float> channelLoss = default)
    {
        int n = store.TileCount;
        var solution = new LakeSolution
        {
            Depth = new float[n],
            Surface = new float[n],
            LakeId = new int[n],
            IsPlaya = new bool[n],
            Endorheic = new bool[n],
            Sink = new bool[n],
            Saline = new bool[n]
        };
        for (int i = 0; i < n; i++)
        {
            solution.Surface[i] = filled[i];
            solution.LakeId[i] = -1;
        }

        // Depression regions: connected land tiles strictly above their bed.
        var regionOf = new int[n];
        for (int i = 0; i < n; i++) regionOf[i] = -1;
        var regions = new List<List<int>>();
        Span<int> scratch = stackalloc int[6];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (heights[i] < seaLevel || filled[i] <= heights[i] + 1e-6f || regionOf[i] >= 0) continue;
            var region = new List<int>();
            regionOf[i] = regions.Count;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                region.Add(cur);
                int adjacent = store.GetAdjacent(cur, scratch);
                for (int k = 0; k < adjacent; k++)
                {
                    int nb = scratch[k];
                    if (regionOf[nb] >= 0 || heights[nb] < seaLevel || filled[nb] <= heights[nb] + 1e-6f) continue;
                    regionOf[nb] = regions.Count;
                    queue.Enqueue(nb);
                }
            }
            regions.Add(region);
        }

        var bed = heights.ToArray(); // once, not per region (was a full-planet LOH copy each)
        foreach (var region in regions)
            ResolveRegion(store, heights, bed, filled, flowFilled, receiverFilled, runoff, evap, cover, channelLoss, region, solution, scratch);

        return solution;
    }

    private static void ResolveRegion(
        WorldDataStore store,
        ReadOnlySpan<float> heights,
        float[] bed,
        ReadOnlySpan<float> filled,
        ReadOnlySpan<float> flowFilled,
        ReadOnlySpan<int> receiverFilled,
        ReadOnlySpan<float> runoff,
        ReadOnlySpan<float> evap,
        ReadOnlySpan<BiomeType> cover,
        ReadOnlySpan<float> channelLoss,
        List<int> region,
        LakeSolution solution,
        Span<int> scratch)
    {
        var inRegion = new HashSet<int>(region);

        // Pour point: lowest filled level around the region rim.
        float spill = float.MaxValue;
        foreach (int t in region)
        {
            int adjacent = store.GetAdjacent(t, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = scratch[k];
                if (inRegion.Contains(nb)) continue;
                if (filled[nb] < spill) spill = filled[nb];
            }
        }
        if (spill == float.MaxValue) spill = filled[region[0]];

        // Total supply: the region's own runoff plus what its outside donors
        // deliver. A donor delivers what is left after the channel has lost its
        // share to the ground and the sun on the way (transmission loss), so a
        // basin ringed by desert is fed by what survives the desert, not by the
        // water the same rivers would carry across wet ground.
        double external = 0;
        foreach (int t in region)
        {
            int adjacent = store.GetAdjacent(t, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                int d = scratch[k];
                if (inRegion.Contains(d) || receiverFilled[d] != t) continue;
                double held = flowFilled[d];
                if (!channelLoss.IsEmpty) held = Math.Max(0.0, held - channelLoss[d]);
                external += held;
            }
        }

        // Grow the ponded set while supply beats evaporation (lowest tiles first).
        region.Sort((a, b) =>
        {
            int c = bed[a].CompareTo(bed[b]);
            return c != 0 ? c : a.CompareTo(b);
        });
        var ponded = new List<int>();
        double water = 0;
        double evapSum = 0;
        foreach (int t in region)
        {
            double w = water + runoff[t];
            double e = evapSum + TileLoss(evap, cover, t);
            // The ponded set grows while external inflow plus its own caught
            // runoff still beats the open-water evaporation over its area.
            if (external + w > e)
            {
                ponded.Add(t);
                water = w;
                evapSum = e;
            }
            else break;
        }

        if (ponded.Count == 0)
        {
            // Dry basin: the sump floor bakes into a playa (salt flat), and the
            // basin is closed: nothing leaves it, so nothing may be routed on.
            float floor = heights[region[0]];
            foreach (int t in region)
            {
                solution.Endorheic[t] = true;
                solution.Sink[t] = true;
                if (heights[t] <= floor + 0.015f) solution.IsPlaya[t] = true;
            }
            var floorTiles = new List<int>();
            foreach (int t in region) if (solution.IsPlaya[t]) floorTiles.Add(t);
            MarkSaline(store, heights, floorTiles, solution, scratch);
            return;
        }

        bool full = ponded.Count == region.Count;
        // Closed lakes pond below the pour point; the top tile is the shoreline.
        float surface = full ? spill : Math.Min(heights[ponded[^1]] + 1e-4f, spill);

        // A film of water over a hair-thin depression is a marsh, not a lake:
        // it carries no open water to map and would only cut the river that
        // crosses it into stubs. The flow still passes over the filled surface.
        float deepest = 0f;
        int deepestTile = ponded[0];
        foreach (int t in ponded)
        {
            float d = surface - heights[t];
            if (d > deepest) { deepest = d; deepestTile = t; }
        }
        float minDepth = MinLakeDepth * (cover.IsEmpty ? 1f : LandCover.MinDepthScale(cover[deepestTile]));
        if (deepest < minDepth)
        {
            // A closed marsh still never reaches the sea: it evaporates where it stands.
            if (!full)
            {
                foreach (int t in region) { solution.Endorheic[t] = true; solution.Sink[t] = true; }
            }
            return;
        }

        var lake = new Lake(solution.Lakes.Count);
        lake.Surface = surface;
        lake.IsOpen = full;
        lake.EvapTotal = (float)evapSum;
        lake.Supply = (float)(external + water);
        foreach (int t in ponded)
        {
            lake.Tiles.Add(t);
            solution.LakeId[t] = lake.Id;
            solution.Depth[t] = Math.Max(0f, lake.Surface - heights[t]);
            solution.Surface[t] = lake.Surface;
        }
        if (full)
        {
            // Outlet: the ponded tile closest to the pour point (lowest rim
            // surface, then lowest index). Evaporation is subtracted here so
            // downstream rivers carry the lake's net outflow.
            int outlet = ponded[0];
            float bestRim = float.MaxValue;
            foreach (int t in ponded)
            {
                int adjacent = store.GetAdjacent(t, scratch);
                for (int k = 0; k < adjacent; k++)
                {
                    int nb = scratch[k];
                    if (inRegion.Contains(nb)) continue;
                    if (filled[nb] < bestRim - 1e-9f || (Math.Abs(filled[nb] - bestRim) < 1e-9f && t < outlet))
                    {
                        bestRim = filled[nb];
                        outlet = t;
                    }
                }
            }
            lake.Outlet = outlet;
        }
        // Terminal lake: every tile of its basin drains to it and nowhere else.
        if (!full) foreach (int t in region) solution.Endorheic[t] = true;
        if (lake.IsSaline) MarkSaline(store, heights, ponded, solution, scratch);
        solution.Lakes.Add(lake);
    }

    /// <summary>
    /// Salt crust: the brine tiles themselves plus the dry land they touch, the margin the water has
    /// shrunk back from and left crusted.
    /// </summary>
    private static void MarkSaline(WorldDataStore store, ReadOnlySpan<float> heights, List<int> brine, LakeSolution solution, Span<int> scratch)
    {
        foreach (int t in brine) solution.Saline[t] = true;
        foreach (int t in brine)
        {
            int adjacent = store.GetAdjacent(t, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = scratch[k];
                if (heights[nb] >= 0f && solution.Depth[nb] <= 0f) solution.Saline[nb] = true;
            }
        }
    }

    /// <summary>Water a ponded tile loses per unit time: open-water evaporation under its local air, plus seepage through its bed.</summary>
    private static double TileLoss(ReadOnlySpan<float> evap, ReadOnlySpan<BiomeType> cover, int tile)
        => cover.IsEmpty
            ? evap[tile]
            : evap[tile] * LandCover.EvaporationFactor(cover[tile]) + LandCover.Seepage(cover[tile]);
}
