using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
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
[WorldGeneratorStage(StageOrder.Hydrology, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo), typeof(UserEditInfo) }, Writes = new[] { typeof(HydrologyInfo) })]
public partial class HydrologyStage : IWorldGeneratorStage
{
    /// <summary>
    /// Scales the discharge a channel needs (lower = more rivers). 0.92 is calibrated for the default pipeline,
    /// whose climate is recomputed on the eroded relief (<see cref="WorldBuilder.WithClimateRefresh"/>): that
    /// pass is drier inland, and 0.92 restores the river count the earlier tuning (scale 1 on the unrefreshed
    /// climate) produced, 513 vs 513 over seven reference worlds. Use 1 with <c>WithClimateRefresh(false)</c> to
    /// get the pre-refresh pipeline back.
    /// </summary>
    public float RiverThresholdScale { get => _riverThresholdScale; set => _riverThresholdScale = StageKnob.Finite(value, nameof(RiverThresholdScale)); }
    private float _riverThresholdScale = 0.92f;
    /// <summary>Cap on the number of rivers kept in the river catalog, applied when <see cref="World"/> calls <see cref="PopulateCatalogs"/>. Does not affect the per-tile <see cref="HydrologyInfo.IsRiver"/> flags.</summary>
    public int MaxRivers { get => _maxRivers; set => _maxRivers = StageKnob.NonNegative(value, nameof(MaxRivers)); }
    private int _maxRivers = 256;

    /// <summary>
    /// How much steeper ground lowers the discharge needed to start a channel:
    /// a tile descending <see cref="SteepSlope"/> or more needs only
    /// <c>1 / (1 + SteepBoost)</c> of the base threshold, so rivers begin on
    /// mountain flanks instead of waiting for the foothills. 0 restores one
    /// global threshold.
    /// </summary>
    public float SteepBoost { get => _steepBoost; set => _steepBoost = StageKnob.Finite(value, nameof(SteepBoost)); }
    private float _steepBoost = 3f;

    /// <summary>Descent per tile (on the routing surface) at which the full <see cref="SteepBoost"/> applies.</summary>
    public float SteepSlope { get => _steepSlope; set => _steepSlope = StageKnob.Finite(value, nameof(SteepSlope)); }
    private float _steepSlope = 0.05f;
    /// <summary>
    /// Transmission-loss strength multiplier. 1.0 charges the full local water
    /// deficit; lower values keep more desert rivers alive.
    /// </summary>
    public float LossFactor { get => _lossFactor; set => _lossFactor = StageKnob.Finite(value, nameof(LossFactor)); }
    private float _lossFactor = 1.0f;

    /// <summary>
    /// Let the provisional biome shape the water balance (runoff coefficient, lake evaporation,
    /// bed seepage, minimum lake depth). False restores the climate-only balance.
    /// </summary>
    public bool UseLandCover { get; set; } = true;

    /// <summary>Routes water over the eroded surface, solves lakes, and writes <see cref="HydrologyInfo"/> for every tile of <paramref name="map"/>.</summary>
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
        Hydrography.AccumulateFlow(
            store, Hydrography.DrainageOrder(filled, depthFilled), recvFilled, runoff, flowFilled, default, loss);
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
        bool overridden = false;
        for (int i = 0; i < n; i++)
        {
            // A closed lake's dry basin drains down its own tree to the shore, never over the pour point, and an open
            // lake drains through its outlet, where its evaporation is charged.
            if (lakes.BasinReceiver[i] < 0) continue;
            receiver[i] = lakes.BasinReceiver[i];
            overridden = true;
        }
        if (overridden) LevelDepths(lakes.Surface, receiver, depth);
        var flow = new float[n];
        // One sort of the final routing serves accumulation, channel extraction and the endorheic walk.
        int[] drainOrder = Hydrography.DrainageOrder(lakes.Surface, depth);
        Hydrography.AccumulateFlow(store, drainOrder, receiver, runoff, flow, evapSink, loss);

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
        // Marine groups other than the ocean that are small enough to be lakes in the catalog (landlocked seas, the
        // Caspian rule) have no path to the sea either: what drains into them is endorheic.
        var landlocked = new bool[nextBody];
        if (nextBody > 1)
        {
            var bodySize = new int[nextBody];
            for (int i = 0; i < n; i++) if (bodyId[i] >= 0) bodySize[bodyId[i]]++;
            int ocean = 0;
            for (int b = 1; b < nextBody; b++) if (bodySize[b] > bodySize[ocean]) ocean = b;
            for (int b = 0; b < nextBody; b++) landlocked[b] = b != ocean && IsLandlockedMarineSize(bodySize[b], n);
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
        foreach (int i in drainOrder)
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
        for (int k = drainOrder.Length - 1; k >= 0; k--)
        {
            int i = drainOrder[k];
            if (elev[i].Height < ElevationGenerationStage.SeaLevel)
            {
                endorheic[i] = landlocked[bodyId[i]];
                continue;
            }
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
    /// Drain depth on level ground recomputed from the receivers themselves: hops to the first receiver that is lower (or
    /// none). Drainage order sorts level ground by this depth, farthest first, so every donor is visited before its
    /// receiver whatever produced the receivers (flat resolution or a lake's own tree).
    /// </summary>
    private static void LevelDepths(ReadOnlySpan<float> surface, ReadOnlySpan<int> receiver, Span<int> depth)
    {
        int n = receiver.Length;
        for (int i = 0; i < n; i++) depth[i] = -1;
        var chain = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (depth[i] >= 0) continue;
            int t = i;
            while (depth[t] < 0)
            {
                int r = receiver[t];
                if (r < 0 || surface[r] != surface[t]) { depth[t] = 0; break; }
                chain.Add(t);
                t = r;
                if (chain.Count > n) throw new InvalidOperationException("Drainage receivers form a cycle.");
            }
            for (int k = chain.Count - 1; k >= 0; k--) depth[chain[k]] = depth[receiver[chain[k]]] + 1;
            chain.Clear();
        }
    }

    /// <summary>A marine group (below sea level, not the ocean) of at most this share of the world is a lake, not a sea: one fortieth.</summary>
    internal static bool IsLandlockedMarineSize(int tiles, int tileCount) => tiles <= tileCount / 40;
}