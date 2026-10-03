using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Land-ice model: where glaciers sit, and what they do to water.
/// Glaciers are a derived surface condition (a pure function of position,
/// bedrock height, and climate), not a stored layer, so every stage agrees on
/// the mask without extra registration or ordering constraints. Fixed noise
/// salts keep the mask identical no matter which stage or seed asks.
/// Hydrological role: glacier tiles seed extra runoff (meltwater) into flow
/// accumulation, so rivers plausibly start at glaciers; glacial erosion
/// carves overdeepenings that later hold meltwater lakes.
///
/// The climate-aware mask models accumulation vs ablation: ice needs cold AND
/// moisture (wet ranges glaciate at higher temperatures; cold dry continental
/// interiors stay tundra, like Siberia). The polar ice-sheet gate keeps dry
/// frozen deserts ice-free, so polar ice forms patchy caps rather than solid
/// continent-spanning blobs.
/// </summary>
public static class Glaciology
{
    /// <summary>Extra runoff weight of a glacier tile (uniform rainfall = 1).</summary>
    public const double MeltBonusWeight = 2.0;

    // Fixed noise salts: the mask is a function of geometry (+ climate), shared
    // by erosion, hydrology, biomes, and queries. (Elevation already carries the
    // world seed.)
    private const int MaskNoiseSalt = 7331;
    private const int ClimateNoiseSalt = 7337;
    private const double SnowlineEquator = 0.75;
    private const double SnowlinePole = -0.06;
    private const double MaskNoiseAmp = 0.12;

    /// <summary>
    /// Dry-polar gate: cold land drier than this stays tundra instead of
    /// glaciating (no accumulation, no ice sheet). Set at the dry tail of the
    /// measured cold-land precip distribution (cold-land mean ~= 0.55, gate at
    /// ~= land P20 over the Phase 3 seed matrix, sizes 3-6 x seeds 42/7):
    /// 0.35 left s3seed42 at zero tundra because its only cool-dry tiles sit
    /// at p 0.365-0.372 (the drier cold tiles there carry stale E4-clamped
    /// temp 0.000 and stay ice via the deep-freeze floor).
    /// </summary>
    public const double DryPolarGate = 0.40;

    /// <summary>
    /// True for land tiles at or above the local snowline. The snowline
    /// descends from the equator toward the poles; noise raggeds the margin.
    /// Ocean tiles are never glaciers (sea ice is not modeled).
    /// Fallback for pipelines without a climate layer; prefer the
    /// climate-aware overload whenever temperature is modeled.
    /// </summary>
    public static bool IsGlacierTile(Vector3D position, float height)
    {
        if (height < ElevationGenerationStage.SeaLevel) return false;
        var geo = position.ToGeoCoord();
        double t = DetMath.Pow(Math.Abs(geo.Latitude) / 90.0, 1.25);
        double snowline = SnowlineEquator + (SnowlinePole - SnowlineEquator) * t;
        double noise = SphereNoise.Fbm(position * 5.0, MaskNoiseSalt);
        return height >= snowline + noise * MaskNoiseAmp;
    }

    /// <summary>
    /// Climate-aware glacier mask: accumulation (cold + snowfall) beats ablation
    /// (warmth). Wet ground glaciates at higher temperatures; dry polar ground
    /// stays tundra. High polar caps (|lat| &gt; 82, Greenland/Antarctica
    /// analogues) keep ice whenever cold.
    /// </summary>
    public static bool IsGlacierTile(Vector3D position, float height, float temperature, float precipitation)
    {
        if (height < ElevationGenerationStage.SeaLevel) return false;
        double snowTemp = 0.15 + 0.11 * precipitation;
        double margin = SphereNoise.Fbm(position * 5.0, ClimateNoiseSalt) * 0.04;
        if (temperature > snowTemp + margin) return false;
        // Dry frozen deserts: without accumulation there is no ice sheet.
        // (Latitude is only needed here, after the cheap early-out above.)
        if (precipitation < DryPolarGate && temperature > 0.03 + margin
            && Math.Abs(position.ToGeoCoord().Latitude) < 82.0)
            return false;
        return true;
    }

    /// <summary>
    /// Latitude + lapse-rate temperature estimate, matching the climate stage's
    /// formula. Used only to route runoff when no climate layer is registered.
    /// </summary>
    public static float EstimateTemperature(Vector3D position, float height)
    {
        var geo = position.ToGeoCoord();
        double t = 1.0 - Math.Abs(geo.Latitude) / 90.0;
        t -= Math.Max(0.0, (double)height) * 0.35;
        return (float)Math.Clamp(t, 0.0, 1.0);
    }

    /// <summary>Runoff weight of one tile: rainfall plus glacier melt.</summary>
    public static float RunoffWeight(Vector3D position, float height, float precipitation, float temperature)
    {
        // precipitation ~[0..1]; mean weight stays near 1 so flow thresholds behave.
        float weight = 0.3f + 1.4f * precipitation;
        if (IsGlacierTile(position, height, temperature, precipitation)) weight += (float)MeltBonusWeight;
        return weight;
    }

    /// <summary>Runoff weight with estimated temperature (no climate layer yet).</summary>
    public static float RunoffWeight(Vector3D position, float height, float precipitation)
    {
        float temperature = EstimateTemperature(position, height);
        return RunoffWeight(position, height, precipitation, temperature);
    }

    /// <summary>Uniform-rainfall runoff weight (no climate layer registered).</summary>
    public static float RunoffWeight(Vector3D position, float height)
    {
        float weight = 1f;
        if (IsGlacierTile(position, height)) weight += (float)MeltBonusWeight;
        return weight;
    }
}
