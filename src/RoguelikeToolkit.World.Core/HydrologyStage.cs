using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Derives the per-tile <see cref="HydrologyInfo"/> field from elevation,
/// rainfall (when the climate layer is registered), and glacier melt, and
/// builds nothing else. Catalogs (rivers, water bodies) are populated via
/// <see cref="PopulateCatalogs"/> so <see cref="World"/> owns them.
/// Runs after erosion (17) and climate (16).
///
/// Routing runs on the priority-flood filled surface: every land tile drains
/// somewhere. Depressions pond according to water balance (inflow vs
/// evaporation) into open lakes (overflow downstream) or closed endorheic
/// lakes; dry basins bake into playas. Rivers therefore always terminate in
/// the sea, a lake, or an inland sink — never mid-slope.
/// </summary>
[WorldGeneratorStage(18, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo) }, Writes = new[] { typeof(HydrologyInfo) })]
public class HydrologyStage : IWorldGeneratorStage
{
    public float RiverThresholdScale { get; set; } = 1.0f;
    public int MaxRivers { get; set; } = 64;
    /// <summary>
    /// Transmission-loss strength multiplier. 1.0 charges the full local water
    /// deficit; lower values keep more desert rivers alive.
    /// </summary>
    public float LossFactor { get; set; } = 1.0f;

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var hydro = store.GetSpan<HydrologyInfo>();

        int n = store.TileCount;
        // Water sources: rainfall, scaled by modeled precipitation when
        // available, plus glacier melt. Rivers therefore start at wet
        // ground or glaciers — never by fiat.
        var vectors = store.GetTileVectors();
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        var runoff = new float[n];
        var evap = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (hasClimate)
            {
                runoff[i] = Glaciology.RunoffWeight(vectors[i], elev[i].Height, climate[i].Precipitation, climate[i].Temperature);
                evap[i] = Hydrography.EvaporationRate(climate[i].Temperature);
            }
            else
            {
                runoff[i] = Glaciology.RunoffWeight(vectors[i], elev[i].Height);
                evap[i] = Hydrography.EvaporationRate(Glaciology.EstimateTemperature(vectors[i], elev[i].Height));
            }
        }

        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = elev[i].Height;

        // Fill depressions, route once to measure catchment supply, solve lake
        // water balance, then route for real on the leveled hydro surface.
        var filled = new float[n];
        Hydrography.ComputeFilledSurface(store, heights, filled);
        var recvFilled = new int[n];
        var depthFilled = new int[n];
        Hydrography.ComputeReceivers(store, filled, recvFilled, depthFilled);
        var flowFilled = new float[n];
        Hydrography.AccumulateFlow(store, filled, recvFilled, runoff, flowFilled, default, default, depthFilled);
        var lakes = LakeSolver.Solve(store, heights, filled, flowFilled, recvFilled, runoff, evap);

        var evapSink = new float[n];
        foreach (var lake in lakes.Lakes)
        {
            if (lake.IsOpen && lake.Outlet >= 0) evapSink[lake.Outlet] = lake.EvapTotal;
        }
        // Transmission loss: every land tile keeps its water deficit
        // (evaporation minus rainfall) out of the channel, so arid tiles
        // contribute nothing downstream and desert rivers fed by distant wet
        // headwaters shrink and can die in inland sinks (Nile/Okavango-style),
        // while humid rivers persist.
        var loss = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (heights[i] >= ElevationGenerationStage.SeaLevel && lakes.Depth[i] <= 0f)
                loss[i] = (Math.Max(0f, evap[i] - runoff[i]) + evap[i] * 0.1f) * LossFactor;
        }
        var receiver = new int[n];
        var depth = new int[n];
        Hydrography.ComputeReceivers(store, lakes.Surface, receiver, depth);
        var flow = new float[n];
        Hydrography.AccumulateFlow(store, lakes.Surface, receiver, runoff, flow, evapSink, loss, depth);

        float threshold = Math.Max(6f, n / 200f) * RiverThresholdScale;

        // Water bodies: connected components of sub-sea-level tiles, then lakes.
        var bodyId = new int[n];
        for (int i = 0; i < n; i++) bodyId[i] = -1;
        int nextBody = 0;
        Span<int> scratch = stackalloc int[6];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (elev[i].Height >= ElevationGenerationStage.SeaLevel || bodyId[i] >= 0) continue;
            bodyId[i] = nextBody;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int adj = store.GetAdjacent(cur, scratch);
                for (int k = 0; k < adj; k++)
                {
                    int nb = scratch[k];
                    if (elev[nb].Height < ElevationGenerationStage.SeaLevel && bodyId[nb] < 0)
                    {
                        bodyId[nb] = nextBody;
                        queue.Enqueue(nb);
                    }
                }
            }
            nextBody++;
        }
        foreach (var lake in lakes.Lakes)
        {
            foreach (int t in lake.Tiles) bodyId[t] = nextBody;
            nextBody++;
        }

        for (int i = 0; i < n; i++)
        {
            bool land = elev[i].Height >= ElevationGenerationStage.SeaLevel;
            bool lake = lakes.Depth[i] > 0f;
            bool river = flow[i] >= threshold && land && !lake;
            hydro[i] = new HydrologyInfo
            {
                Flow = flow[i],
                WaterBodyId = bodyId[i],
                IsRiver = (byte)(river ? 1 : 0),
                Surface = lakes.Surface[i],
                LakeDepth = lakes.Depth[i],
                IsPlaya = (byte)(lakes.IsPlaya[i] ? 1 : 0)
            };
        }
    }

    /// <summary>
    /// Builds river and water-body catalogs from the generated fields.
    /// Public so hosts without a <see cref="World"/> (e.g. the visualizer)
    /// can classify water tiles from a bare <see cref="WorldMap"/>.
    /// </summary>
    public static void PopulateCatalogs(WorldMap map, RiverCatalog rivers, WaterBodyCatalog bodies, int maxRivers = 64)
    {
        var store = map.DataStore;
        if (!store.IsLayerRegistered<ElevationInfo>() || !store.IsLayerRegistered<HydrologyInfo>()) return;
        var elev = store.GetSpan<ElevationInfo>();
        var hydro = store.GetSpan<HydrologyInfo>();
        int n = store.TileCount;

        var surface = new float[n];
        var bed = new float[n];
        var depth = new float[n];
        for (int i = 0; i < n; i++)
        {
            surface[i] = hydro[i].Surface;
            bed[i] = elev[i].Height;
            depth[i] = hydro[i].LakeDepth;
        }

        // Group water tiles by body id.
        var groups = new Dictionary<int, WaterBody>();
        for (int i = 0; i < n; i++)
        {
            int id = hydro[i].WaterBodyId;
            if (id < 0) continue;
            if (!groups.TryGetValue(id, out var body))
            {
                body = new WaterBody(id, WaterBodyKind.Lake);
                groups[id] = body;
                bodies.Bodies.Add(body);
            }
            body.Tiles.Add(i);
        }
        // Kinds: above-sea-level groups are always lakes; the largest marine
        // group is the ocean, big marine groups are seas, small ones are lakes
        // (landlocked seas like the Caspian).
        if (bodies.Bodies.Count > 0)
        {
            bodies.Bodies.Sort((a, b) => b.Tiles.Count.CompareTo(a.Tiles.Count));
            int oceanId = -1;
            int bestMarine = -1;
            foreach (var body in bodies.Bodies)
            {
                bool marine = elev[body.Tiles[0]].Height < ElevationGenerationStage.SeaLevel;
                if (marine && body.Tiles.Count > bestMarine) { bestMarine = body.Tiles.Count; oceanId = body.Id; }
            }
            for (int k = 0; k < bodies.Bodies.Count; k++)
            {
                var old = bodies.Bodies[k];
                bool marine = elev[old.Tiles[0]].Height < ElevationGenerationStage.SeaLevel;
                var kind = !marine ? WaterBodyKind.Lake
                    : old.Id == oceanId ? WaterBodyKind.Ocean
                    : old.Tiles.Count > n / 40 ? WaterBodyKind.Sea : WaterBodyKind.Lake;
                var rebuilt = new WaterBody(old.Id, kind);
                rebuilt.Tiles.AddRange(old.Tiles);
                bodies.Bodies[k] = rebuilt;
            }
        }
        // Boundaries: water tiles adjacent to land (or, for lakes, to dry land).
        Span<int> scratch = stackalloc int[6];
        foreach (var body in bodies.Bodies)
        {
            var inBody = new HashSet<int>(body.Tiles);
            foreach (int t in body.Tiles)
            {
                int adj = store.GetAdjacent(t, scratch);
                for (int k = 0; k < adj; k++)
                {
                    if (!inBody.Contains(scratch[k])) { body.Boundary.Add(t); break; }
                }
            }
        }

        // Rivers: trace downstream from head tiles. A head is a river tile with
        // no upstream river neighbor: higher on the routing surface, or level
        // with it but carrying less water (flat-channel rule, so fill plateaus
        // and lake outlets don't spawn a head per tile).
        var riverTiles = new List<int>();
        for (int i = 0; i < n; i++)
            if (hydro[i].IsRiver == 1) riverTiles.Add(i);
        if (riverTiles.Count == 0) return;

        var riverSet = new HashSet<int>(riverTiles);
        var heads = new List<int>();
        foreach (int t in riverTiles)
        {
            int adj = store.GetAdjacent(t, scratch);
            bool hasUpstreamRiver = false;
            for (int k = 0; k < adj; k++)
            {
                int nb = scratch[k];
                if (!riverSet.Contains(nb)) continue;
                if (surface[nb] > surface[t] + 1e-6f) { hasUpstreamRiver = true; break; }
                if (Math.Abs(surface[nb] - surface[t]) <= 1e-6f && hydro[nb].Flow < hydro[t].Flow)
                {
                    hasUpstreamRiver = true;
                    break;
                }
            }
            if (!hasUpstreamRiver) heads.Add(t);
        }
        // Longest-first: sort heads by flow descending for stable ids.
        var flows = new float[n];
        for (int i = 0; i < n; i++) flows[i] = hydro[i].Flow;
        heads.Sort((a, b) => flows[b].CompareTo(flows[a]));

        var isRiverTile = new bool[n];
        for (int i = 0; i < n; i++) isRiverTile[i] = hydro[i].IsRiver == 1;
        bool IsTerminalWater(int t) =>
            bed[t] < ElevationGenerationStage.SeaLevel || depth[t] > 0f;
        bool IsRiver(int t) => isRiverTile[t];

        // Receivers recomputed deterministically from the stored surface, so
        // tracing follows the same flat-resolved channels the flow did.
        var receiver = new int[n];
        Hydrography.ComputeReceivers(store, surface, receiver);

        int riverId = 0;
        var tileOwner = new Dictionary<int, int>();
        foreach (int head in heads)
        {
            if (rivers.Rivers.Count >= maxRivers) break;
            if (tileOwner.ContainsKey(head)) continue;
            var full = Hydrography.TraceRiver(store, receiver, IsRiver, IsTerminalWater, head);
            // Tributaries end at the confluence: trim the path at the first tile
            // already owned by an earlier river, so every reach belongs to exactly
            // one river and tiles resolve unambiguously back to their catalog entry.
            int end = full.Count;
            for (int k = 1; k < full.Count; k++)
            {
                if (tileOwner.ContainsKey(full[k])) { end = k; break; }
            }
            if (end < 2) continue;
            var river = new River(riverId++);
            for (int k = 0; k < end; k++)
            {
                river.Path.Add(full[k]);
                tileOwner[full[k]] = river.Id;
            }
            if (end < full.Count)
            {
                // Joins a cataloged reach downstream: inherit its terminal.
                var downstream = rivers.Rivers[tileOwner[full[end]]];
                river.Terminal = downstream.Terminal;
                river.TerminalBody = downstream.TerminalBody;
            }
            else
            {
                int tail = full[^1];
                if (elev[tail].Height < ElevationGenerationStage.SeaLevel)
                {
                    river.Terminal = RiverTerminal.Sea;
                    river.TerminalBody = hydro[tail].WaterBodyId;
                }
                else if (hydro[tail].LakeDepth > 0f)
                {
                    river.Terminal = RiverTerminal.Lake;
                    river.TerminalBody = hydro[tail].WaterBodyId;
                }
                else
                {
                    river.Terminal = RiverTerminal.Sink;
                    river.TerminalBody = -1;
                }
            }
            for (int k = river.Path.Count - 1; k >= 0; k--)
            {
                if (hydro[river.Path[k]].IsRiver == 1) { river.MouthFlow = hydro[river.Path[k]].Flow; break; }
            }
            rivers.Rivers.Add(river);
        }
    }
}

/// <summary>Builds mountain-range and valley features from elevation + rivers.</summary>
public static class RangeCatalogBuilder
{
    public static void Populate(WorldMap map, RangeCatalog catalog, float mountainThreshold = 0.5f)
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
            float riverThreshold = Math.Max(6f, n / 200f);
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
