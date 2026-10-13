using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>
/// Shared hydrography helpers: depression filling, flat-resolved flow routing,
/// lake water balance, accumulation, and downstream tracing. Used by
/// <see cref="ErosionGenerationStage"/> (stream-power pass) and
/// <see cref="HydrologyStage"/> (river/lake extraction) so both agree.
/// </summary>
public static class Hydrography
{
    /// <summary>
    /// Discharge at which a tile counts as a river (and the base for canyon
    /// cutting). The single definition shared by erosion, hydrology, biomes and
    /// range extraction; <paramref name="scale"/> is
    /// <see cref="HydrologyStage.RiverThresholdScale"/>.
    /// </summary>
    public static float RiverThreshold(int tileCount, float scale = 1f)
        => Math.Max(6f, tileCount / 200f) * scale;

    /// <summary>
    /// Channel head threshold on a given reach. Water concentrates into a
    /// channel on a much smaller catchment where the ground is steep, so the
    /// base discharge is divided by up to <c>1 + steepBoost</c> as the descent to
    /// the receiver approaches <paramref name="steepSlope"/> (Montgomery–Dietrich
    /// area–slope initiation). Gentle plains keep the full base threshold.
    /// </summary>
    public static float ChannelThreshold(float baseThreshold, float descent, float steepBoost, float steepSlope)
    {
        if (steepBoost <= 0f || steepSlope <= 0f || descent <= 0f) return baseThreshold;
        float t = Math.Min(1f, descent / steepSlope);
        return baseThreshold / (1f + steepBoost * t);
    }

    /// <summary>
    /// Donors-before-receivers processing order: surface descending, then
    /// distance-to-outlet descending on level ground, then index. Shared by
    /// flow accumulation, river extraction and sediment routing, which all
    /// pass something downstream and must not visit a receiver first.
    /// </summary>
    public static int[] DrainageOrder(ReadOnlySpan<float> surface, ReadOnlySpan<int> drainDepth = default)
    {
        int n = surface.Length;
        var snapshot = surface.ToArray();
        var depth = drainDepth.IsEmpty ? null : drainDepth.ToArray();
        if (depth == null) return TileOrdering.DescendingByValue(snapshot);
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = snapshot[b].CompareTo(snapshot[a]);
            if (c != 0) return c;
            c = depth[b].CompareTo(depth[a]);
            return c != 0 ? c : a.CompareTo(b);
        });
        return order;
    }

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
        var order = TileOrdering.DescendingByValue(snapshot);

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
    /// Receiver of a single tile, identical to <c>ComputeReceivers(...)[tile]</c>
    /// but computed locally: strict descents are O(1); a tile on a flat only
    /// explores its own equal-surface plateau (the same reverse BFS, seeded by
    /// the plateau's draining rim tiles in index order). Use for single-tile
    /// queries instead of routing the whole planet.
    /// </summary>
    public static int ReceiverAt(WorldDataStore store, Func<int, float> surface, int tile)
    {
        Span<int> nbs = stackalloc int[6];
        int Lowest(int t, Span<int> scratch)
        {
            int adj = store.GetAdjacent(t, scratch);
            int lowest = -1;
            float best = surface(t);
            for (int k = 0; k < adj; k++)
            {
                float h = surface(scratch[k]);
                if (h < best) { best = h; lowest = scratch[k]; }
            }
            return lowest;
        }

        int direct = Lowest(tile, nbs);
        if (direct >= 0) return direct;

        // Plateau = connected receiverless tiles at exactly this surface level;
        // seeds = routed tiles at the same level touching it.
        float level = surface(tile);
        var plateau = new List<int> { tile };
        var inPlateau = new HashSet<int> { tile };
        var seeds = new SortedSet<int>();
        Span<int> inner = stackalloc int[6];
        for (int head = 0; head < plateau.Count; head++)
        {
            int adj = store.GetAdjacent(plateau[head], nbs);
            for (int k = 0; k < adj; k++)
            {
                int nb = nbs[k];
                if (surface(nb) != level || inPlateau.Contains(nb) || seeds.Contains(nb)) continue;
                if (Lowest(nb, inner) >= 0) seeds.Add(nb);
                else { inPlateau.Add(nb); plateau.Add(nb); }
            }
        }

        var claimed = new Dictionary<int, int>();
        var queue = new Queue<int>(seeds);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int adj = store.GetAdjacent(cur, nbs);
            for (int k = 0; k < adj; k++)
            {
                int nb = nbs[k];
                if (!inPlateau.Contains(nb) || claimed.ContainsKey(nb)) continue;
                claimed[nb] = cur;
                queue.Enqueue(nb);
            }
        }
        return claimed.TryGetValue(tile, out int r) ? r : -1;
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

        int[] order = DrainageOrder(surface, drainDepth);

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

    /// <summary>
    /// Evaporative demand minus rainfall, in runoff units: positive where the ground loses more water to
    /// the air than it receives (precipitation below evaporation), negative where it has a surplus to shed.
    /// </summary>
    public static float WaterDeficit(float temperature, float precipitation)
        => EvaporationRate(temperature) - (0.3f + 1.4f * precipitation);
}
