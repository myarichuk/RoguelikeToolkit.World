using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Elevation;

public partial class ErosionGenerationStage
{
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
}
