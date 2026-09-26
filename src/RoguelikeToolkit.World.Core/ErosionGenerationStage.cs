using System;

namespace RoguelikeToolkit.World.Core;

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
[WorldGeneratorStage(17, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo), typeof(TectonicPlate) }, Writes = new[] { typeof(ElevationInfo) })]
public class ErosionGenerationStage : IWorldGeneratorStage
{
    public int Iterations { get; set; } = 10;
    public double Talus { get; set; } = 0.02;
    public double Rate { get; set; } = 0.25;

    /// <summary>Hillslope-diffusion smoothing passes (rolling hills). 0 disables.</summary>
    public int DiffusionIterations { get; set; } = 2;
    public double DiffusionRate { get; set; } = 0.03;

    /// <summary>Fluvial stream-power passes after thermal smoothing. 0 disables.</summary>
    public int HydraulicIterations { get; set; } = 3;
    public double HydraulicRate { get; set; } = 0.08;

    /// <summary>Stream-power exponents: E = K·Q^m·S^n.</summary>
    public double StreamPowerM { get; set; } = 0.5;
    public double StreamPowerN { get; set; } = 1.0;

    /// <summary>Glacial carving passes between thermal and fluvial. 0 disables.</summary>
    public int GlacialIterations { get; set; } = 2;
    public double GlacialRate { get; set; } = 0.15;

    /// <summary>Incision multiplier inside canyon reaches (narrow, deep cut).</summary>
    public double CanyonBoost { get; set; } = 2.0;

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
        // climate hovers near freezing (periglacial freeze-thaw zone).
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < store.TileCount; i++)
            {
                int adjacent = store.GetAdjacent(i, neighbors);
                int lowest = -1;
                float lowestHeight = elev[i].Height;

                for (int k = 0; k < adjacent; k++)
                {
                    int j = neighbors[k];
                    if (elev[j].Height < lowestHeight)
                    {
                        lowestHeight = elev[j].Height;
                        lowest = j;
                    }
                }

                if (lowest < 0) continue;

                double slope = elev[i].Height - lowestHeight;
                if (slope > Talus)
                {
                    double rate = Rate;
                    if (hasClimate)
                    {
                        float temp = climate[i].Temperature;
                        if (temp > 0.05f && temp < 0.30f) rate *= 1.4; // freeze-thaw
                    }
                    float move = (float)((slope - Talus) * rate * 0.5);
                    elev[i].Height -= move;
                    elev[lowest].Height += move;
                }
            }
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

        // Fluvial pass: stream-power incision ordered down the filled surface.
        for (int h = 0; h < HydraulicIterations; h++)
        {
            FluvialPass(store, elev, neighbors, hasClimate, climate);
        }
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

        float riverThreshold = Math.Max(6f, n / 200f);
        bool hasPlates = store.IsLayerRegistered<TectonicPlate>();
        var plates = hasPlates ? store.GetSpan<TectonicPlate>() : default;

        var order = DescendingOrder(filled);
        foreach (int i in order)
        {
            // Oceans neither erode nor collect river sediment here (deltas
            // build outward from the river tiles themselves).
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            int r = receiver[i];
            if (r < 0) continue;
            double slope = filled[i] - filled[r];
            if (slope <= 0) continue;

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

            // Stream power E = K·Q^m·S^n, capped so fluvial work never inverts
            // the channel profile (water cannot incise below its outlet).
            double streamPower = HydraulicRate * Math.Pow(Math.Max(1.0, flow[i]), StreamPowerM)
                * Math.Pow(slope, StreamPowerN) / Math.Max(0.4, hardness);
            if (canyon) streamPower *= CanyonBoost;
            float move = (float)Math.Min(streamPower, slope * 0.45);
            if (move <= 0) continue;
            elev[i].Height -= move;
            elev[r].Height += move;

            // Where power fades the load splays into fans/deltas — except in
            // canyons, where the narrow cut keeps every grain in the channel.
            if (!canyon && elev[r].Height >= ElevationGenerationStage.SeaLevel - 0.05f)
            {
                int adjacent = store.GetAdjacent(r, neighbors);
                float fanTotal = 0f;
                for (int k = 0; k < adjacent; k++)
                {
                    int j = neighbors[k];
                    if (j == i || elev[j].Height >= elev[r].Height) continue;
                    fanTotal += 1f;
                }
                if (fanTotal > 0)
                {
                    float fanEach = move * 0.25f / fanTotal;
                    for (int k = 0; k < adjacent; k++)
                    {
                        int j = neighbors[k];
                        if (j == i || elev[j].Height >= elev[r].Height) continue;
                        elev[r].Height -= fanEach;
                        elev[j].Height += fanEach;
                    }
                }
            }
        }
    }

    private static int[] DescendingOrder(float[] surface)
    {
        int n = surface.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = surface[b].CompareTo(surface[a]);
            return c != 0 ? c : a.CompareTo(b);
        });
        return order;
    }
}
