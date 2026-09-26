using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Shared hydrography helpers: depression filling, flat-resolved flow routing,
/// lake water balance, accumulation, and downstream tracing. Used by
/// <see cref="ErosionGenerationStage"/> (stream-power pass) and
/// <see cref="HydrologyStage"/> (river/lake extraction) so both agree.
/// </summary>
public static class Hydrography
{
    public static int LowestNeighbor(WorldDataStore store, ReadOnlySpan<float> heights, int tile, Span<int> scratch)
    {
        int adjacent = store.GetAdjacent(tile, scratch);
        int lowest = -1;
        float best = heights[tile];
        for (int k = 0; k < adjacent; k++)
        {
            int j = scratch[k];
            if (heights[j] < best) { best = heights[j]; lowest = j; }
        }
        return lowest;
    }

    /// <summary>
    /// Steepest-descent flow accumulation. Each tile seeds <paramref name="runoffWeight"/>
    /// units of water (rainfall + glacier melt); an empty weight span means
    /// uniform rainfall (weight 1 everywhere). Deterministic for given inputs.
    /// Raw steepest descent (no depression handling); prefer the filled-surface
    /// routing below for real hydrology.
    /// </summary>
    public static void ComputeFlow(WorldDataStore store, Span<ElevationInfo> elev, Span<float> flow, ReadOnlySpan<float> runoffWeight = default)
    {
        int n = store.TileCount;
        bool uniform = runoffWeight.IsEmpty;
        for (int i = 0; i < n; i++) flow[i] = uniform ? 1f : runoffWeight[i];

        // Descending-height order via index sort (deterministic: tie-break by index).
        // Copy heights first: Span cannot be captured by the sort lambda.
        var snapshot = new float[n];
        for (int i = 0; i < n; i++) snapshot[i] = elev[i].Height;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = snapshot[b].CompareTo(snapshot[a]);
            return c != 0 ? c : a.CompareTo(b);
        });

        Span<int> scratch = stackalloc int[6];
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = elev[i].Height;

        foreach (int i in order)
        {
            int lowest = LowestNeighbor(store, heights, i, scratch);
            if (lowest >= 0) flow[lowest] += flow[i];
        }
    }

    /// <summary>Trace steepest descent from head until sea, sink, or maxLen.</summary>
    public static List<int> TraceDownstream(WorldDataStore store, ReadOnlySpan<float> heights, int head, int maxLen = 256)
    {
        var path = new List<int> { head };
        Span<int> scratch = stackalloc int[6];
        int cur = head;
        if (heights[cur] < ElevationGenerationStage.SeaLevel) return path;
        for (int s = 0; s < maxLen; s++)
        {
            int lowest = LowestNeighbor(store, heights, cur, scratch);
            if (lowest < 0) break;
            path.Add(lowest);
            cur = lowest;
            if (heights[cur] < ElevationGenerationStage.SeaLevel) break;
        }
        return path;
    }

    /// <summary>
    /// Trace steepest descent on a routing surface until a terminal tile
    /// (sea, lake, ...), a sink, or maxLen. The terminal tile is included.
    /// </summary>
    public static List<int> TraceDownstream(
        WorldDataStore store, ReadOnlySpan<float> surface, int head, Predicate<int> isTerminal, int maxLen = 256)
    {
        var path = new List<int> { head };
        Span<int> scratch = stackalloc int[6];
        int cur = head;
        if (isTerminal(cur)) return path;
        for (int s = 0; s < maxLen; s++)
        {
            int lowest = LowestNeighbor(store, surface, cur, scratch);
            if (lowest < 0) break;
            path.Add(lowest);
            cur = lowest;
            if (isTerminal(cur)) break;
        }
        return path;
    }

    /// <summary>
    /// Priority-flood fill (Barnes 2014): the lowest surface that drains
    /// everywhere to the sea. Ocean tiles are fixed outlets; land depressions
    /// fill to their pour point. Deterministic (index-tiebroken heap).
    /// </summary>
    public static void ComputeFilledSurface(
        WorldDataStore store, ReadOnlySpan<float> heights, Span<float> filled, float seaLevel = 0f)
    {
        int n = store.TileCount;
        for (int i = 0; i < n; i++) filled[i] = heights[i];

        var closed = new bool[n];
        var heap = new PriorityQueue<int, (double Height, int Index)>();

        for (int i = 0; i < n; i++)
        {
            if (heights[i] >= seaLevel) continue;
            closed[i] = true;
            heap.Enqueue(i, (heights[i], i));
        }
        if (heap.Count == 0)
        {
            // Pathological all-land world: the global minimum is the outlet.
            int min = 0;
            for (int i = 1; i < n; i++) if (heights[i] < heights[min]) min = i;
            closed[min] = true;
            heap.Enqueue(min, (heights[min], min));
        }

        Span<int> scratch = stackalloc int[6];
        while (heap.Count > 0)
        {
            int cur = heap.Dequeue();
            int adjacent = store.GetAdjacent(cur, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = scratch[k];
                if (closed[nb]) continue;
                closed[nb] = true;
                float level = Math.Max(heights[nb], filled[cur]);
                filled[nb] = level;
                heap.Enqueue(nb, (level, nb));
            }
        }
    }

    /// <summary>
    /// Drainage receivers on a routing surface. Strict descents route to the
    /// lowest neighbor; equal-height flats (fill plateaus, lake surfaces) route
    /// toward their outlet by BFS; true pits (closed-lake floors) get -1.
    /// Deterministic (index-ordered BFS over sorted neighbors).
    /// The optional <paramref name="drainDepth"/> output records each flat
    /// tile's BFS distance from its outlet (0 elsewhere): accumulation must
    /// process flats farthest-first, since index order is not drainage order
    /// on level ground.
    /// </summary>
    public static void ComputeReceivers(
        WorldDataStore store, ReadOnlySpan<float> surface, Span<int> receiver, Span<int> drainDepth = default)
    {
        int n = store.TileCount;
        bool wantDepth = !drainDepth.IsEmpty;
        Span<int> scratch = stackalloc int[6];
        for (int i = 0; i < n; i++)
        {
            receiver[i] = LowestNeighbor(store, surface, i, scratch);
            if (wantDepth) drainDepth[i] = 0;
        }

        // Receiverless tiles sit on flats (no strictly lower neighbor). Resolve
        // them by reversed BFS: seed with every routed tile in index order and
        // claim receiverless equal-surface neighbors inward from draining
        // ground, so each flat drains toward its outlet.
        var isFlat = new bool[n];
        for (int i = 0; i < n; i++) isFlat[i] = receiver[i] < 0;
        var resolved = new bool[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (isFlat[i]) continue;
            resolved[i] = true;
            queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int adjacent = store.GetAdjacent(cur, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = scratch[k];
                if (!isFlat[nb] || resolved[nb]) continue;
                if (surface[nb] != surface[cur]) continue;
                receiver[nb] = cur;
                resolved[nb] = true;
                if (wantDepth) drainDepth[nb] = drainDepth[cur] + 1;
                queue.Enqueue(nb);
            }
        }
        // Leftover flats are true pits (closed basins): receiver stays -1.
    }

    /// <summary>
    /// Trace a river reach along resolved receivers (flat-safe, unlike raw
    /// steepest descent which stalls on fill plateaus). The path runs from the
    /// head through river tiles and includes one terminal tile: the sea/lake
    /// water tile it pours into, or nothing further for sink rivers.
    /// </summary>
    public static List<int> TraceRiver(
        WorldDataStore store,
        ReadOnlySpan<int> receiver,
        Predicate<int> isRiver,
        Predicate<int> isTerminalWater,
        int head,
        int maxLen = 4096)
    {
        var path = new List<int> { head };
        var seen = new HashSet<int> { head };
        int cur = head;
        for (int s = 0; s < maxLen; s++)
        {
            int next = receiver[cur];
            if (next < 0 || !seen.Add(next)) break;
            if (isTerminalWater(next)) { path.Add(next); break; }
            if (!isRiver(next)) break;
            path.Add(next);
            cur = next;
        }
        return path;
    }

    /// <summary>
    /// Route runoff along receivers in drainage order: descending surface, then
    /// flat depth (farthest-from-outlet first — index order alone loses water
    /// on level ground), then index. An optional evaporation sink per tile
    /// (open-lake outlets) and per-tile channel loss (desert transmission loss)
    /// are subtracted before the tile passes its flow downstream. Closed-basin
    /// tiles (receiver -1) hold their water: it evaporates or ponds rather
    /// than reaching the sea.
    /// </summary>
    public static void AccumulateFlow(
        WorldDataStore store,
        ReadOnlySpan<float> surface,
        ReadOnlySpan<int> receiver,
        ReadOnlySpan<float> runoff,
        Span<float> flow,
        ReadOnlySpan<float> evapSink = default,
        ReadOnlySpan<float> channelLoss = default,
        ReadOnlySpan<int> drainDepth = default)
    {
        int n = store.TileCount;
        for (int i = 0; i < n; i++) flow[i] = runoff[i];

        var snapshot = new float[n];
        for (int i = 0; i < n; i++) snapshot[i] = surface[i];
        var depth = drainDepth.IsEmpty ? null : drainDepth.ToArray();
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = snapshot[b].CompareTo(snapshot[a]);
            if (c != 0) return c;
            if (depth != null)
            {
                c = depth[b].CompareTo(depth[a]);
                if (c != 0) return c;
            }
            return a.CompareTo(b);
        });

        bool hasSink = !evapSink.IsEmpty;
        bool hasLoss = !channelLoss.IsEmpty;
        foreach (int i in order)
        {
            float held = flow[i];
            if (hasSink && evapSink[i] > 0)
                held = Math.Max(0f, held - evapSink[i]);
            if (hasLoss && channelLoss[i] > 0)
                held = Math.Max(0f, held - channelLoss[i]);
            int r = receiver[i];
            if (r >= 0) flow[r] += held;
            else flow[i] = held;
        }
    }

    /// <summary>
    /// Open-water evaporation in runoff units: hot deserts evaporate several
    /// times the mean rainfall, freezing ground almost nothing.
    /// </summary>
    public static float EvaporationRate(float temperature)
        => 0.25f + 1.1f * temperature;
}

/// <summary>One resolved lake: ponded tiles, surface level, and outflow state.</summary>
public sealed class Lake
{
    public int Id { get; }
    public List<int> Tiles { get; } = new();
    public float Surface { get; set; }
    public bool IsOpen { get; set; }
    public float EvapTotal { get; set; }
    /// <summary>Outlet tile (an in-lake tile draining out); -1 for closed lakes.</summary>
    public int Outlet { get; set; } = -1;

    public Lake(int id) => Id = id;
}

/// <summary>
/// Result of the lake water-balance solver: per-tile depths, ids, playas, and
/// the routing (hydro) surface with lakes leveled to their water surface.
/// </summary>
public sealed class LakeSolution
{
    public float[] Depth = Array.Empty<float>();
    public float[] Surface = Array.Empty<float>();
    public int[] LakeId = Array.Empty<int>();
    public bool[] IsPlaya = Array.Empty<bool>();
    public List<Lake> Lakes { get; } = new();
}

/// <summary>
/// Fills depressions according to water balance: inflow (runoff caught by the
/// depression's catchment) vs evaporation over the ponded area. Wet depressions
/// fill to their pour point and overflow (open lakes); arid ones pond partially
/// (closed/endorheic lakes) or stay dry (playas/salt flats).
/// </summary>
public static class LakeSolver
{
    public static LakeSolution Solve(
        WorldDataStore store,
        ReadOnlySpan<float> heights,
        ReadOnlySpan<float> filled,
        ReadOnlySpan<float> flowFilled,
        ReadOnlySpan<int> receiverFilled,
        ReadOnlySpan<float> runoff,
        ReadOnlySpan<float> evap,
        float seaLevel = 0f)
    {
        int n = store.TileCount;
        var solution = new LakeSolution
        {
            Depth = new float[n],
            Surface = new float[n],
            LakeId = new int[n],
            IsPlaya = new bool[n]
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

        foreach (var region in regions)
            ResolveRegion(store, heights, filled, flowFilled, receiverFilled, runoff, evap, region, solution, scratch);

        return solution;
    }

    private static void ResolveRegion(
        WorldDataStore store,
        ReadOnlySpan<float> heights,
        ReadOnlySpan<float> filled,
        ReadOnlySpan<float> flowFilled,
        ReadOnlySpan<int> receiverFilled,
        ReadOnlySpan<float> runoff,
        ReadOnlySpan<float> evap,
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

        // Total supply: the region's own runoff plus water routed in from
        // outside (all incoming through-flow minus internal passing).
        double incoming = 0;
        double internalPass = 0;
        foreach (int t in region)
        {
            incoming += flowFilled[t] - runoff[t];
            int r = receiverFilled[t];
            if (r >= 0 && inRegion.Contains(r)) internalPass += flowFilled[t];
        }
        double external = Math.Max(0.0, incoming - internalPass);

        // Grow the ponded set while supply beats evaporation (lowest tiles first).
        // (Copy to an array: spans cannot be captured by the sort lambda.)
        var bed = heights.ToArray();
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
            double e = evapSum + evap[t];
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
            // Dry basin: the sump floor bakes into a playa (salt flat).
            float floor = heights[region[0]];
            foreach (int t in region)
            {
                if (heights[t] <= floor + 0.015f) solution.IsPlaya[t] = true;
            }
            return;
        }

        var lake = new Lake(solution.Lakes.Count);
        bool full = ponded.Count == region.Count;
        // Closed lakes pond below the pour point; the top tile is the shoreline.
        lake.Surface = full ? spill : Math.Min(heights[ponded[^1]] + 1e-4f, spill);
        lake.IsOpen = full;
        lake.EvapTotal = (float)evapSum;
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
        solution.Lakes.Add(lake);
    }
}
