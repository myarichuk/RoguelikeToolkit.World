using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Hydrology;

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
[Preserve] // created via reflection by WorldGenerationPipeline.Discover; keep it through Unity code stripping
[WorldGeneratorStage(18, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo), typeof(UserEditInfo) }, Writes = new[] { typeof(HydrologyInfo) })]
public class HydrologyStage : IWorldGeneratorStage
{
    /// <summary>
    /// Scales the discharge a channel needs (lower = more rivers). 0.92 is calibrated for the default pipeline,
    /// whose climate is recomputed on the eroded relief (<see cref="WorldBuilder.WithClimateRefresh"/>): that
    /// pass is drier inland, and 0.92 restores the river count the earlier tuning (scale 1 on the unrefreshed
    /// climate) produced, 513 vs 513 over seven reference worlds. Use 1 with <c>WithClimateRefresh(false)</c> to
    /// get the pre-refresh pipeline back.
    /// </summary>
    public float RiverThresholdScale { get; set; } = 0.92f;
    public int MaxRivers { get; set; } = 256;

    /// <summary>
    /// How much steeper ground lowers the discharge needed to start a channel:
    /// a tile descending <see cref="SteepSlope"/> or more needs only
    /// <c>1 / (1 + SteepBoost)</c> of the base threshold, so rivers begin on
    /// mountain flanks instead of waiting for the foothills. 0 restores one
    /// global threshold.
    /// </summary>
    public float SteepBoost { get; set; } = 3f;

    /// <summary>Descent per tile (on the routing surface) at which the full <see cref="SteepBoost"/> applies.</summary>
    public float SteepSlope { get; set; } = 0.05f;
    /// <summary>
    /// Transmission-loss strength multiplier. 1.0 charges the full local water
    /// deficit; lower values keep more desert rivers alive.
    /// </summary>
    public float LossFactor { get; set; } = 1.0f;

    /// <summary>
    /// Let the provisional biome shape the water balance (runoff coefficient, lake evaporation,
    /// bed seepage, minimum lake depth). False restores the climate-only balance.
    /// </summary>
    public bool UseLandCover { get; set; } = true;

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
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = elev[i].Height;

        // Land cover: the biome each tile will be, judged from climate alone
        // (the biome stage runs after this one). It sets how much of the rain
        // a catchment sheds and what a lake over it loses. Without a climate
        // layer there is nothing to classify, and cover stays neutral.
        var cover = new BiomeType[n];
        Span<int> reliefScratch = stackalloc int[6];
        if (hasClimate && UseLandCover)
        {
            for (int i = 0; i < n; i++)
            {
                float relief = CanyonAnalysis.Relief(store, heights, i, reliefScratch);
                cover[i] = LandCover.Provisional(heights[i], climate[i].Temperature, climate[i].Precipitation, relief, climate[i].RainSeasonality, climate[i].RainPhase);
            }
            // A painted biome is the author's statement of what the ground is: it sheds, soaks and
            // evaporates like that biome, whatever the climate fields would have said.
            if (store.IsLayerRegistered<UserEditInfo>())
            {
                var pins = store.GetSpan<UserEditInfo>();
                for (int i = 0; i < n; i++)
                    if ((pins[i].Flags & UserEditFlags.PinBiome) != 0) cover[i] = pins[i].PinBiome;
            }
        }
        else
        {
            for (int i = 0; i < n; i++) cover[i] = heights[i] >= ElevationGenerationStage.SeaLevel ? BiomeType.Plains : BiomeType.Ocean;
        }

        var runoff = new float[n];
        var rain = new float[n];   // runoff on neutral cover: the reference the transmission-loss deficit is measured against
        var evap = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (hasClimate)
            {
                runoff[i] = Glaciology.RunoffWeight(vectors[i], elev[i].Height, climate[i].Precipitation, climate[i].Temperature, LandCover.RunoffCoefficient(cover[i]));
                rain[i] = Glaciology.RunoffWeight(vectors[i], elev[i].Height, climate[i].Precipitation, climate[i].Temperature);
                evap[i] = Hydrography.EvaporationRate(climate[i].Temperature);
            }
            else
            {
                runoff[i] = rain[i] = Glaciology.RunoffWeight(vectors[i], elev[i].Height);
                evap[i] = Hydrography.EvaporationRate(Glaciology.EstimateTemperature(vectors[i], elev[i].Height));
            }
        }

        // Fill depressions, route once to measure catchment supply, solve lake
        // water balance, then route for real on the leveled hydro surface.
        var filled = new float[n];
        Hydrography.ComputeFilledSurface(store, heights, filled);
        var recvFilled = new int[n];
        var depthFilled = new int[n];
        Hydrography.ComputeReceivers(store, filled, recvFilled, depthFilled);
        // Transmission loss: every land tile keeps its water deficit
        // (evaporation minus rainfall) out of the channel, so arid tiles
        // contribute nothing downstream and desert rivers fed by distant wet
        // headwaters shrink and can die in inland sinks (Nile/Okavango-style),
        // while humid rivers persist. The loss is charged before lakes are
        // solved too, so a basin in dry country is fed by what survives the
        // desert and can end up closed instead of overflowing on phantom water.
        var loss = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (heights[i] >= ElevationGenerationStage.SeaLevel)
                loss[i] = (Math.Max(0f, evap[i] - rain[i]) + evap[i] * 0.1f) * LossFactor;
        }
        var flowFilled = new float[n];
        Hydrography.AccumulateFlow(store, filled, recvFilled, runoff, flowFilled, default, loss, depthFilled);
        var lakes = LakeSolver.Solve(store, heights, filled, flowFilled, recvFilled, runoff, evap,
            (float)ElevationGenerationStage.SeaLevel, hasClimate && UseLandCover ? cover : default, loss);

        var evapSink = new float[n];
        foreach (var lake in lakes.Lakes)
        {
            if (lake.IsOpen && lake.Outlet >= 0) evapSink[lake.Outlet] = lake.EvapTotal;
            else if (!lake.IsOpen && lake.Tiles.Count > 0) evapSink[lake.Tiles[0]] += lake.EvapTotal;
        }
        // A closed depression without a lake (dry basin, closed marsh) swallows
        // everything that reaches it: a playa is where rivers go to die.
        for (int i = 0; i < n; i++)
        {
            if (lakes.Sink[i]) evapSink[i] = float.MaxValue;
        }
        // Final routing charges the loss only on dry land: ponded tiles are
        // water, and lose to evaporation through the lake balance instead.
        for (int i = 0; i < n; i++)
        {
            if (lakes.Depth[i] > 0f) loss[i] = 0f;
        }
        var receiver = new int[n];
        var depth = new int[n];
        Hydrography.ComputeReceivers(store, lakes.Surface, receiver, depth);
        var flow = new float[n];
        Hydrography.AccumulateFlow(store, lakes.Surface, receiver, runoff, flow, evapSink, loss, depth);

        float threshold = Hydrography.RiverThreshold(n, RiverThresholdScale);

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

        // Channels. A tile is a river where its discharge clears the head
        // threshold for its slope; once a channel exists it keeps going while
        // the flow stays above the steepest-ground threshold, so a stream born
        // on a mountain flank does not disappear the moment it reaches gentle
        // ground (the base threshold is higher there than the flow it carries).
        // Donors are visited before receivers.
        var isRiver = new bool[n];
        var fed = new bool[n];
        float floorThreshold = Hydrography.ChannelThreshold(threshold, float.MaxValue, SteepBoost, SteepSlope);
        foreach (int i in Hydrography.DrainageOrder(lakes.Surface, depth))
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel || lakes.Depth[i] > 0f) continue;
            int rcv = receiver[i];
            float descent = rcv >= 0 ? lakes.Surface[i] - lakes.Surface[rcv] : 0f;
            float need = fed[i] ? floorThreshold : Hydrography.ChannelThreshold(threshold, descent, SteepBoost, SteepSlope);
            if (flow[i] < need) continue;
            isRiver[i] = true;
            if (rcv >= 0) fed[rcv] = true;
        }

        // Endorheic reach: a land tile has no path to the sea when its drainage
        // ends in a closed depression or an inland sink. Receivers are visited
        // before their donors, so each tile just inherits its receiver's answer.
        var endorheic = new bool[n];
        int[] drainOrder = Hydrography.DrainageOrder(lakes.Surface, depth);
        for (int k = drainOrder.Length - 1; k >= 0; k--)
        {
            int i = drainOrder[k];
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            int rcv = receiver[i];
            endorheic[i] = lakes.Endorheic[i] || rcv < 0 || endorheic[rcv];
        }

        for (int i = 0; i < n; i++)
        {
            hydro[i] = new HydrologyInfo
            {
                Flow = flow[i],
                WaterBodyId = bodyId[i],
                IsRiver = (byte)(isRiver[i] ? 1 : 0),
                Surface = lakes.Surface[i],
                LakeDepth = lakes.Depth[i],
                IsPlaya = (byte)(lakes.IsPlaya[i] ? 1 : 0),
                Endorheic = (byte)(endorheic[i] ? 1 : 0),
                Saline = (byte)(lakes.Saline[i] ? 1 : 0)
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
            bodies.Bodies.Sort((a, b) =>
            {
                int c = b.Tiles.Count.CompareTo(a.Tiles.Count);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
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
        // Flat-resolved receivers, shared by head detection and tracing below.
        var drainTo = new int[n];
        Hydrography.ComputeReceivers(store, surface, drainTo);

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
            bool hasInboundLink = false;
            for (int k = 0; k < adj; k++)
            {
                int nb = scratch[k];
                if (!riverSet.Contains(nb)) continue;
                if (drainTo[nb] == t) hasInboundLink = true;
                if (surface[nb] > surface[t] + 1e-6f) { hasUpstreamRiver = true; break; }
                if (Math.Abs(surface[nb] - surface[t]) <= 1e-6f && hydro[nb].Flow < hydro[t].Flow)
                {
                    hasUpstreamRiver = true;
                    break;
                }
            }
            // A head starts a new reach: no upstream river neighbor by surface,
            // or no river neighbor draining into it (receiver divergence, e.g.
            // a reach broken mid-course by transmission loss). The latter keeps
            // every IsRiver tile on exactly one cataloged reach.
            if (!hasUpstreamRiver || !hasInboundLink) heads.Add(t);
        }
        // Longest-first: sort heads by flow descending for stable ids.
        var flows = new float[n];
        for (int i = 0; i < n; i++) flows[i] = hydro[i].Flow;
        heads.Sort((a, b) =>
        {
            int c = flows[b].CompareTo(flows[a]);
            return c != 0 ? c : a.CompareTo(b);
        });

        var isRiverTile = new bool[n];
        for (int i = 0; i < n; i++) isRiverTile[i] = hydro[i].IsRiver == 1;
        bool IsTerminalWater(int t) =>
            bed[t] < ElevationGenerationStage.SeaLevel || depth[t] > 0f;
        bool IsRiver(int t) => isRiverTile[t];

        // Receivers recomputed deterministically from the stored surface, so
        // tracing follows the same flat-resolved channels (copied from drainTo).
        var receiver = new int[n];
        drainTo.CopyTo(receiver, 0); // Same routing head detection used; computed once above.

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
            if (end < 2) continue; // Cataloged rivers are multi-tile reaches (valid LINESTRING, Path.Count >= 2); lone trickles stay IsRiver flags only.
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
