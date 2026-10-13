using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Elevation;

/// <summary>
/// Thermal (talus) + hillslope diffusion + glacial U-valley carving + fluvial
/// stream-power incision. All transport is pairwise mass-preserving (every move
/// takes from one tile and gives to another), so total mass is conserved and
/// output stays deterministic for a given input. Runs after climate (16) and
/// before hydrology extraction (18); the fluvial pass routes its own discharge
/// on the filled surface with modeled rainfall when the climate layer exists.
///
/// Process order mirrors landscape evolution: frost/talus shatters high
/// ground first, glaciers pluck valleys (overdeepening the tarns that later
/// hold meltwater lakes), then rivers incise with stream power E = K·Q^m·S^n
/// and deposit fans/deltas where power fades. Arid gorges incise narrow and
/// deep (canyons); humid ones spread their load (valleys).
/// </summary>
[Preserve] // created via reflection by WorldGenerationPipeline.Discover; keep it through Unity code stripping
[WorldGeneratorStage(StageOrder.Erosion, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo), typeof(TectonicPlate), typeof(UserEditInfo) }, Writes = new[] { typeof(ElevationInfo) })]
public class ErosionGenerationStage : IWorldGeneratorStage
{
    public int Iterations { get; set; } = 10;
    public double Talus { get; set; } = 0.03;

    /// <summary>
    /// Extra angle of repose per unit of orogeny: welded belt rock holds
    /// <c>Talus × (1 + HardRockTalus × |orogeny|)</c>.
    /// </summary>
    public double HardRockTalus { get; set; } = 8.0;

    /// <summary>
    /// Erodibility divisor per unit of orogeny: welded belt rock sheds
    /// <c>Rate / (1 + HardRockResistance × |orogeny|)</c>. Repose angle alone
    /// cannot keep a range standing; against a needle-sharp uplift the excess
    /// over repose is so large that every rock type collapses at the same pace.
    /// </summary>
    public double HardRockResistance { get; set; } = 4.0;
    public double Rate { get; set; } = 0.25;

    /// <summary>Hillslope-diffusion smoothing passes (rolling hills). 0 disables.</summary>
    public int DiffusionIterations { get; set; } = 2;
    public double DiffusionRate { get; set; } = 0.03;

    /// <summary>Fluvial stream-power passes after thermal smoothing. 0 disables.</summary>
    public int HydraulicIterations { get; set; } = 3;
    public double HydraulicRate { get; set; } = 0.2;

    /// <summary>Stream-power exponents: E = K·Q^m·S^n.</summary>
    public double StreamPowerM { get; set; } = 0.5;
    public double StreamPowerN { get; set; } = 1.0;

    /// <summary>
    /// Discharge, as a fraction of the river threshold, at which a tile counts as
    /// a channel rather than a hillslope. Bedrock incision is weighted by
    /// <c>1 − exp(−Q / onset)</c>: slopes are shaped by creep (diffusion), channels by
    /// water, so the steep divides between valleys are not the fastest-eroding ground.
    /// </summary>
    public double ChannelOnset { get; set; } = 0.12;

    /// <summary>Sediment a channel carries per unit stream power (Kc); above it the surplus is deposited.</summary>
    public double TransportCapacity { get; set; } = 2.0;

    /// <summary>Share of the over-capacity surplus dropped per tile (the rest travels on).</summary>
    public double DepositFraction { get; set; } = 0.5;

    /// <summary>Glacial carving passes between thermal and fluvial. 0 disables.</summary>
    public int GlacialIterations { get; set; } = 2;
    public double GlacialRate { get; set; } = 0.15;

    /// <summary>Incision multiplier inside canyon reaches (narrow, deep cut).</summary>
    public double CanyonBoost { get; set; } = 2.0;

    /// <summary>Must match <see cref="HydrologyStage.RiverThresholdScale"/> so canyons and rivers agree (WorldBuilder wires it).</summary>
    public float RiverThresholdScale { get; set; } = 1f;

    /// <summary>
    /// Wind-erosion passes over arid land (deflation and sand transport). 0 disables and is bit-identical to a
    /// stage without the pass. A tile here is tens to hundreds of km across, so the product is deflation
    /// basins and sand seas (a broad lowering upwind, a broad blanket downwind), never individual dunes.
    /// </summary>
    public int AeolianIterations { get; set; } = 4;

    /// <summary>Height a fully arid, fully exposed tile yields per pass.</summary>
    public double AeolianRate { get; set; } = 0.004;

    /// <summary>Most a tile may be lowered in total (the water table, a lag of pebbles, caprock stop deflation).</summary>
    public double AeolianMaxDeflation { get; set; } = 0.03;

    /// <summary>Share of airborne sand a fully arid tile drops on the spot even on open ground (the rest hops on downwind).</summary>
    public double AeolianDeposit { get; set; } = 0.25;

    public ErosionGenerationStage()
    {
    }

    public ErosionGenerationStage(int iterations = 10)
    {
        Iterations = iterations;
    }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();

        Span<int> neighbors = stackalloc int[6];

        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;

        // Thermal: talus-angle collapse, boosted by frost shattering where the
        // climate hovers near freezing (periglacial freeze-thaw zone). Jacobi
        // sweeps (deltas accumulate against a frozen surface, then apply), so
        // the result cannot depend on tile order: a tile sheds toward every
        // neighbor that sits more than its angle of repose below it, in
        // proportion to the excess. The old in-place sweep handed everything
        // to the single steepest neighbor, so peaks collapsed into the foothill
        // beside them and left plateau benches instead of flanks.
        bool hasPlates = store.IsLayerRegistered<TectonicPlate>();
        var plateSpan = hasPlates ? store.GetSpan<TectonicPlate>() : default;
        var talusAt = new float[store.TileCount];
        var erodibility = new float[store.TileCount];
        for (int i = 0; i < talusAt.Length; i++)
        {
            // Bedrock stands steeper where an orogen has welded it (folded,
            // metamorphosed, cemented): ranges keep their flanks while soft
            // sediment and rift basins slump at the base angle.
            double hard = hasPlates ? Math.Min(Math.Abs(plateSpan[i].Orogeny), 1.5) : 0.0;
            talusAt[i] = (float)(Talus * (1.0 + HardRockTalus * hard));
            erodibility[i] = (float)(1.0 / (1.0 + HardRockResistance * hard));
        }
        var delta = new float[store.TileCount];
        Span<float> excess = stackalloc float[6];
        for (int iter = 0; iter < Iterations; iter++)
        {
            Array.Clear(delta);
            for (int i = 0; i < store.TileCount; i++)
            {
                int adjacent = store.GetAdjacent(i, neighbors);
                float total = 0f, steepest = 0f;
                for (int k = 0; k < adjacent; k++)
                {
                    float e = elev[i].Height - elev[neighbors[k]].Height - talusAt[i];
                    if (e < 0f) e = 0f;
                    excess[k] = e;
                    total += e;
                    if (e > steepest) steepest = e;
                }
                if (total <= 0f) continue;

                double rate = Rate;
                if (hasClimate)
                {
                    float temp = climate[i].Temperature;
                    if (temp > 0.05f && temp < 0.30f) rate *= 1.4; // freeze-thaw
                }
                float shed = (float)(steepest * rate * 0.5) * erodibility[i];
                delta[i] -= shed;
                for (int k = 0; k < adjacent; k++)
                    if (excess[k] > 0f) delta[neighbors[k]] += shed * excess[k] / total;
            }
            for (int i = 0; i < store.TileCount; i++) elev[i].Height += delta[i];
        }

        // Diffusion: gentle creep rounding every slope (rolling hills). Skipped
        // with Iterations == 0 (pristine).
        if (Iterations > 0)
        {
            for (int d = 0; d < DiffusionIterations; d++)
            {
                for (int i = 0; i < store.TileCount; i++)
                {
                    int adjacent = store.GetAdjacent(i, neighbors);
                    int lowest = -1;
                    float best = elev[i].Height;
                    for (int k = 0; k < adjacent; k++)
                    {
                        int j = neighbors[k];
                        if (elev[j].Height < best) { best = elev[j].Height; lowest = j; }
                    }
                    if (lowest < 0) continue;
                    double slope = elev[i].Height - best;
                    if (slope <= 0) continue;
                    float move = (float)Math.Min(slope * DiffusionRate, slope * 0.25);
                    elev[i].Height -= move;
                    elev[lowest].Height += move;
                }
            }
        }

        // Glacial pass: U-valley carving with overdeepening. Skipped with
        // Iterations == 0 (pristine).
        if (Iterations > 0 && GlacialIterations > 0)
        {
            GlacialPass(store, elev, neighbors, hasClimate, climate);
        }

        // Fluvial pass: stream-power incision ordered down the filled surface. Skipped with Iterations == 0 (pristine).
        if (Iterations > 0)
        for (int h = 0; h < HydraulicIterations; h++)
        {
            FluvialPass(store, elev, neighbors, hasClimate, climate);
        }

        // Aeolian pass: wind strips loose ground in dry country and lays it down downwind. After the
        // fluvial passes so water has already taken what it can; before the pin restore so pins stay exact.
        if (Iterations > 0 && AeolianIterations > 0 && hasClimate)
            AeolianPass(store, elev, neighbors, climate, erodibility);

        // Fixed ground is restored last: erosion shapes everything around a pin (and may have moved
        // mass through it), but the pinned tile itself ends exactly where the author drew it.
        if (store.IsLayerRegistered<UserEditInfo>())
        {
            var edits = store.GetSpan<UserEditInfo>();
            for (int i = 0; i < edits.Length; i++)
                if ((edits[i].Flags & UserEditFlags.PinHeight) != 0) elev[i].Height = edits[i].PinHeight;
        }
    }

    /// <summary>
    /// Jacobi transport on a frozen surface: every decision reads last pass's heights and loads, deltas are
    /// summed and applied together, so tile order cannot matter. Sand is lifted from arid tiles
    /// (capped per tile), hops one tile to the downwind neighbour, and is dropped there where the surface
    /// rises, the wind is gone, or the ground is vegetated; whatever is still airborne at the end is laid
    /// down where it hangs. Every move takes from one tile and gives to another, so mass is conserved.
    /// </summary>
    private void AeolianPass(
        WorldDataStore store, Span<ElevationInfo> elev, Span<int> neighbors, Span<ClimateInfo> climate, float[] erodibility)
    {
        int n = store.TileCount;
        var vectors = store.GetTileVectors();
        bool pins = store.IsLayerRegistered<UserEditInfo>();
        var edits = pins ? store.GetSpan<UserEditInfo>() : default;

        var arid = new float[n];
        var downwind = new int[n];
        var strength = new float[n];
        for (int i = 0; i < n; i++)
        {
            downwind[i] = -1;
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            // Dryness: full below 0.15 of the precipitation scale, gone by 0.40; frozen ground is not loose.
            double dry = 1.0 - Smooth((climate[i].Precipitation - 0.15) / 0.25);
            double thawed = Smooth((climate[i].Temperature - 0.10) / 0.15);
            arid[i] = (float)(dry * thawed);

            var wind = new Vector3D(climate[i].WindX, climate[i].WindY, climate[i].WindZ);
            int adjacent = store.GetAdjacent(i, neighbors);
            double best = 0.15; // below this the wind is not carrying anything along a tile edge
            for (int k = 0; k < adjacent; k++)
            {
                double along = Vector3D.Dot((vectors[neighbors[k]] - vectors[i]).Normalize(), wind);
                if (along > best) { best = along; downwind[i] = neighbors[k]; strength[i] = (float)Math.Min(along, 1.0); }
            }
        }

        var lowered = new float[n];
        var load = new float[n];
        var nextLoad = new float[n];
        var delta = new float[n];
        float maxLoss = (float)AeolianMaxDeflation;
        for (int pass = 0; pass < AeolianIterations; pass++)
        {
            Array.Clear(delta);
            Array.Clear(nextLoad);
            for (int i = 0; i < n; i++)
            {
                float lift = 0f;
                bool pinned = pins && (edits[i].Flags & UserEditFlags.PinHeight) != 0;
                if (arid[i] > 0f && downwind[i] >= 0 && !pinned)
                {
                    lift = Math.Min((float)AeolianRate * arid[i] * strength[i] * erodibility[i], maxLoss - lowered[i]);
                    if (lift < 0f) lift = 0f;
                    lowered[i] += lift;
                    delta[i] -= lift;
                }
                float airborne = load[i] + lift;
                if (airborne <= 0f) continue;

                int j = downwind[i];
                float keep;
                if (j < 0 || elev[i].Height < ElevationGenerationStage.SeaLevel) keep = 1f;                         // calm or sea: it settles
                else
                {
                    // Vegetated ground traps sand; open ground lets most of it hop on; a rising surface stops it.
                    keep = 1f - arid[i] * (1f - (float)AeolianDeposit);
                    if (elev[j].Height > elev[i].Height) keep = Math.Max(keep, 0.8f);
                }
                delta[i] += airborne * keep;
                if (keep < 1f) nextLoad[j] += airborne * (1f - keep);
            }
            for (int i = 0; i < n; i++) elev[i].Height += delta[i];
            (load, nextLoad) = (nextLoad, load);
        }
        // Whatever is still in the air comes down where it is.
        for (int i = 0; i < n; i++) elev[i].Height += load[i];
    }

    private static double Smooth(double t)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;
        return t * t * (3.0 - 2.0 * t);
    }

    private void GlacialPass(
        WorldDataStore store, Span<ElevationInfo> elev, Span<int> neighbors, bool hasClimate, Span<ClimateInfo> climate)
    {
        int n = store.TileCount;
        var vectors = store.GetTileVectors();
        var isGlacier = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            isGlacier[i] = hasClimate
                ? Glaciology.IsGlacierTile(vectors[i], elev[i].Height, climate[i].Temperature, climate[i].Precipitation)
                : Glaciology.IsGlacierTile(vectors[i], elev[i].Height);
        }

        // Ice flux: each glacier tile accumulates its upstream ice area along
        // steepest descent (outlet glaciers carve hardest).
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = elev[i].Height;
        var flux = new float[n];
        for (int i = 0; i < n; i++) flux[i] = isGlacier[i] ? 1f : 0f;
        var order = DescendingOrder(heights);
        float maxFlux = 1f;
        foreach (int i in order)
        {
            if (!isGlacier[i]) continue;
            int lowest = Hydrography.LowestNeighbor(store, heights, i, neighbors);
            if (lowest >= 0 && isGlacier[lowest]) flux[lowest] += flux[i];
        }
        for (int i = 0; i < n; i++) if (flux[i] > maxFlux) maxFlux = flux[i];

        for (int g = 0; g < GlacialIterations; g++)
        {
            for (int i = 0; i < n; i++)
            {
                if (!isGlacier[i]) continue;
                int adjacent = store.GetAdjacent(i, neighbors);

                // Cirque check: a glacier head with no upstream ice plucks its
                // headwall harder (bergschrund quarrying).
                bool hasUpstreamIce = false;
                int highestWall = -1;
                float wallTop = elev[i].Height;
                for (int k = 0; k < adjacent; k++)
                {
                    int j = neighbors[k];
                    if (isGlacier[j] && elev[j].Height > elev[i].Height) hasUpstreamIce = true;
                    if (elev[j].Height > wallTop) { wallTop = elev[j].Height; highestWall = j; }
                }

                // Floor abrasion: flux-weighted, allowed slight slope inversion
                // so tongues gouge overdeepenings (future tarns).
                int lowest = -1;
                float best = elev[i].Height;
                for (int k = 0; k < adjacent; k++)
                {
                    int j = neighbors[k];
                    if (elev[j].Height < best) { best = elev[j].Height; lowest = j; }
                }
                if (lowest >= 0)
                {
                    double slope = elev[i].Height - best;
                    double q = flux[i] / maxFlux;
                    double carve = GlacialRate * (0.25 + 0.75 * q) * (0.3 + Math.Min(slope * 8.0, 1.2));
                    if (!hasUpstreamIce) carve *= 1.5;
                    float move = (float)Math.Min(carve, slope * 1.1);
                    if (move > 0)
                    {
                        elev[i].Height -= move;
                        elev[lowest].Height += move; // frontal moraine / outwash
                    }
                }

                // Wall plucking: U-widening draws the valley walls down into
                // the ice, which carries the debris away downstream.
                if (highestWall >= 0)
                {
                    double wallSlope = wallTop - elev[i].Height;
                    float wallMove = (float)Math.Min(wallSlope * GlacialRate * 0.3, wallSlope * 0.25);
                    if (wallMove > 0)
                    {
                        elev[highestWall].Height -= wallMove;
                        elev[i].Height += wallMove;
                    }
                }
            }
        }
    }

    private void FluvialPass(
        WorldDataStore store, Span<ElevationInfo> elev, Span<int> neighbors, bool hasClimate, Span<ClimateInfo> climate)
    {
        int n = store.TileCount;
        var vectors = store.GetTileVectors();

        var runoff = new float[n];
        var loss = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (hasClimate)
            {
                runoff[i] = Glaciology.RunoffWeight(vectors[i], elev[i].Height, climate[i].Precipitation, climate[i].Temperature);
                float evap = Hydrography.EvaporationRate(climate[i].Temperature);
                if (elev[i].Height >= ElevationGenerationStage.SeaLevel)
                    loss[i] = Math.Max(0f, evap - runoff[i]) + evap * 0.1f;
            }
            else
            {
                runoff[i] = Glaciology.RunoffWeight(vectors[i], elev[i].Height);
            }
        }

        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = elev[i].Height;
        var filled = new float[n];
        Hydrography.ComputeFilledSurface(store, heights, filled);
        var receiver = new int[n];
        var depth = new int[n];
        Hydrography.ComputeReceivers(store, filled, receiver, depth);
        var flow = new float[n];
        Hydrography.AccumulateFlow(store, filled, receiver, runoff, flow, default, loss, depth);

        float riverThreshold = Hydrography.RiverThreshold(n, RiverThresholdScale);
        double channelOnset = Math.Max(1.0, riverThreshold * ChannelOnset);
        bool hasPlates = store.IsLayerRegistered<TectonicPlate>();
        var plates = hasPlates ? store.GetSpan<TectonicPlate>() : default;

        // Donors before receivers, flats included: sediment is carried in `load`
        // and handed to the receiver, so a receiver processed before its donor
        // would silently lose mass.
        var order = Hydrography.DrainageOrder(filled, depth);

        // Transport-limited stream power. Each tile receives the sediment its
        // catchment shed upstream and can carry Kc·Q^m·S^n of it. Spare
        // capacity incises the bed (detachment-limited, E = K·Q^m·S^n scaled by
        // bedrock hardness); a surplus is dropped on the spot (fans, floodplains,
        // aggradation where the slope relaxes). Whatever survives to the coast
        // builds the delta. Without the capacity term a trunk channel received
        // the whole catchment's erosion and could never cut below it, so
        // valleys filled back in and a higher K only flattened the ridges.
        var load = new float[n];
        Span<int> mouth = stackalloc int[6];
        foreach (int i in order)
        {
            float carried = load[i];
            load[i] = 0f;

            if (elev[i].Height < ElevationGenerationStage.SeaLevel)
            {
                // Base level: the open-water tile and its marine neighbors share the delta.
                if (carried > 0f) SpreadDelta(store, elev, i, carried, mouth);
                continue;
            }
            int r = receiver[i];
            if (r < 0)
            {
                elev[i].Height += carried; // closed basin: the sump keeps its sediment
                continue;
            }
            double slope = filled[i] - filled[r];
            if (slope <= 0)
            {
                load[r] += carried; // lake surface / fill plateau: sediment is in transit
                continue;
            }

            // Bedrock hardness: orogenic (metamorphic) belts resist, rift
            // volcanics and soft sediments yield, lithology noise varies.
            double hardness = 1.0;
            if (hasPlates)
            {
                hardness += Math.Abs(plates[i].Orogeny) * 0.8;
                if (plates[i].Orogeny < -0.05) hardness *= 0.8; // rift volcanics
                hardness += SphereNoise.Fbm(vectors[i] * 4.0, 5150) * 0.25 + 0.25;
            }

            double precip = hasClimate ? climate[i].Precipitation : 0.5;
            float relief = CanyonAnalysis.Relief(store, heights, i, neighbors);
            bool canyon = CanyonAnalysis.IsCanyonTile(flow[i], riverThreshold, precip, relief, slope);

            double power = DetMath.Pow(Math.Max(1.0, flow[i]), StreamPowerM) * DetMath.Pow(slope, StreamPowerN);
            double capacity = TransportCapacity * power;
            if (carried > capacity)
            {
                float drop = (float)((carried - capacity) * DepositFraction);
                elev[i].Height += drop;
                carried -= drop;
            }
            else
            {
                // Stream power E = K·Q^m·S^n, capped by spare capacity and so that fluvial
                // work never inverts the channel profile (water cannot incise below its outlet).
                double channelized = 1.0 - DetMath.Exp(-flow[i] / channelOnset);
                double cut = HydraulicRate * power * channelized / Math.Max(0.4, hardness);
                if (canyon) cut *= CanyonBoost;
                float move = (float)Math.Min(Math.Min(cut, capacity - carried), slope * 0.45);
                if (move > 0f)
                {
                    elev[i].Height -= move;
                    carried += move;
                }
            }
            load[r] += carried;
        }
    }

    /// <summary>
    /// Deposits a river's remaining load at its mouth: spread evenly over the
    /// marine tile and its marine neighbors, so deltas fan out instead of
    /// piling a spike onto the single tile the channel enters.
    /// </summary>
    private static void SpreadDelta(WorldDataStore store, Span<ElevationInfo> elev, int tile, float amount, Span<int> scratch)
    {
        int adjacent = store.GetAdjacent(tile, scratch);
        int marine = 1;
        for (int k = 0; k < adjacent; k++)
            if (elev[scratch[k]].Height < ElevationGenerationStage.SeaLevel) marine++;
        float each = amount / marine;
        elev[tile].Height += each;
        for (int k = 0; k < adjacent; k++)
            if (elev[scratch[k]].Height < ElevationGenerationStage.SeaLevel) elev[scratch[k]].Height += each;
    }

    private static int[] DescendingOrder(float[] surface)
    {
        return TileOrdering.DescendingByValue(surface);
    }
}
