using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Elevation;

public partial class ErosionGenerationStage
{
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

    private static int[] DescendingOrder(float[] surface)
    {
        return TileOrdering.DescendingByValue(surface);
    }
}
