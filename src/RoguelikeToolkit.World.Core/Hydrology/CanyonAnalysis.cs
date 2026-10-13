using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Canyon (slot-gorge) physics shared by erosion (incision), feature extraction,
/// and biomes. Canyons need a powerful river, weak hillslopes (arid: little
/// vegetation/soil creep to widen the gorge), and relief to cut through —
/// the Colorado-plateau recipe. Humid gorges widen into valleys instead.
/// </summary>
public static class CanyonAnalysis
{
    /// <summary>Discharge multiple of the river threshold for canyon cutting.</summary>
    public const double FlowMultiple = 1.5;

    /// <summary>Aridity gate: wetter climates widen gorges into valleys.</summary>
    public const double MaxPrecipitation = 0.50;

    /// <summary>Minimum local relief (neighborhood max-min height).</summary>
    public const double MinRelief = 0.12;

    /// <summary>Minimum channel slope on the routing surface.</summary>
    public const double MinSlope = 0.01;

    public static bool IsCanyonTile(double flow, double riverThreshold, double precipitation, double relief, double slope)
        => flow >= riverThreshold * FlowMultiple
            && precipitation <= MaxPrecipitation
            && relief >= MinRelief
            && slope >= MinSlope;

    /// <summary>Local relief: max-min bed height over the tile neighborhood.</summary>
    public static float Relief(WorldDataStore store, ReadOnlySpan<float> heights, int tile, Span<int> scratch)
    {
        float lo = heights[tile], hi = heights[tile];
        int adjacent = store.GetAdjacent(tile, scratch);
        for (int k = 0; k < adjacent; k++)
        {
            float h = heights[scratch[k]];
            if (h < lo) lo = h;
            if (h > hi) hi = h;
        }
        return hi - lo;
    }
}
