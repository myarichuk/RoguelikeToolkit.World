using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Elevation;

public partial class ErosionGenerationStage
{
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
}
