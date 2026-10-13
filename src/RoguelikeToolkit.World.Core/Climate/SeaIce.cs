using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>How much of the year an ocean tile is under ice.</summary>
public enum SeaIceKind : byte
{
    /// <summary>Open water all year (or a land tile: sea ice is an ocean condition).</summary>
    None = 0,
    /// <summary>Frozen over in winter, open in summer: the marginal ice zone and the fast ice of a cold coast.</summary>
    Seasonal = 1,
    /// <summary>Survives the summer: the polar pack.</summary>
    Perennial = 2
}

/// <summary>
/// Sea-ice model. Like <see cref="Glaciology"/> it is a pure function of the tile (position, height and the
/// climate layer), not a stored layer, so every consumer agrees on the mask with no extra registration or
/// ordering. Ocean tiles stay <see cref="BiomeType.Ocean"/>: ice is an overlay condition, so no biome id,
/// palette or saved file changes.
///
/// The test is a winter and a summer temperature built from the two numbers the climate stage already
/// provides: the annual mean (<see cref="ClimateInfo.Temperature"/>, which includes the ocean currents, so a
/// warm current such as the Gulf Stream keeps its coast open and a cold one pushes the edge towards the
/// equator) and the half-swing (<see cref="ClimateInfo.SeasonalRange"/>, which grows with axial tilt, so a
/// strongly tilted planet freezes more of its sea in winter and thaws more of it in summer).
/// Calibrated against Earth on default worlds pooled over 10 to 30 seeds at each of sizes 3 to 6: about 9% of the
/// ocean freezes in winter (Earth: 33 of 361 million km2), and the permanent pack is about a quarter of that
/// (Earth: 7.5 of 33). Seasonal ice sits around 68 degrees on average, the pack around 80; the first ice
/// appears at about 50 degrees. The pack's share of the frozen area still drifts from 21% to 28% between sizes 3
/// and 6, which no constant removes.
/// Consumers: <see cref="LandCapacity"/> (the fish catch of the water) and <see cref="CitySiteScorer"/>
/// (pack ice is not water access; a coast whose only usable water is seasonal ice scores <see cref="SettlementScoring.WinterIce"/>
/// lower). On default worlds that moves under 1% of the viable sites (0 to 2 dropped, up to 80 of 7000 re-scored, the best 300 almost
/// unchanged), because polar land is mostly glacier and tundra already.
/// Not modelled: the ice-albedo feedback on climate (the mask is read from the climate, never written back),
/// salinity (brine freezes later), and ice drift.
/// </summary>
public static class SeaIce
{
    /// <summary>Winter temperature (mean minus half-swing) at or below which the sea freezes over.</summary>
    public const double FreezeTemperature = 0.20;
    /// <summary>Summer temperature (mean plus half-swing) at or below which the ice survives to autumn.</summary>
    public const double PerennialTemperature = 0.28;

    // A fixed salt, like Glaciology's: the ragged margin is a function of geometry alone.
    private const int MarginNoiseSalt = 7349;
    private const double MarginNoiseAmp = 0.03;

    /// <summary>
    /// Classifies one tile. <paramref name="temperature"/> and <paramref name="seasonalRange"/> are the tile's
    /// <see cref="ClimateInfo"/> values; land (height at or above sea level) is always <see cref="SeaIceKind.None"/>.
    /// </summary>
    public static SeaIceKind Classify(Vector3D position, float height, float temperature, float seasonalRange)
    {
        if (height >= ElevationGenerationStage.SeaLevel) return SeaIceKind.None;
        double winter = temperature - seasonalRange;
        // The cheap early-out first: most of the ocean is far from freezing.
        if (winter > FreezeTemperature + MarginNoiseAmp) return SeaIceKind.None;
        double margin = SphereNoise.Fbm(position * 5.0, MarginNoiseSalt) * MarginNoiseAmp;
        if (winter > FreezeTemperature + margin) return SeaIceKind.None;
        return temperature + seasonalRange <= PerennialTemperature + margin ? SeaIceKind.Perennial : SeaIceKind.Seasonal;
    }

    /// <summary>
    /// The mask for every tile, or all <see cref="SeaIceKind.None"/> when the world has no climate layer
    /// (without temperature there is nothing to freeze the sea).
    /// </summary>
    public static SeaIceKind[] Mask(WorldDataStore store)
    {
        var mask = new SeaIceKind[store.TileCount];
        if (!store.IsLayerRegistered<ClimateInfo>()) return mask;
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        var vectors = store.GetTileVectors();
        for (int i = 0; i < mask.Length; i++)
            mask[i] = Classify(vectors[i], elev[i].Height, climate[i].Temperature, climate[i].SeasonalRange);
        return mask;
    }
}
