using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// What the ground and the air above it do to water, by biome. Hydrology runs
/// before the biome stage, so it classifies a provisional, climate-only biome
/// (<see cref="Provisional"/>) and reads these tables from it: the runoff a
/// catchment sheds, the evaporation off an open lake, the water a lake bed
/// leaks away, and how thick a film of water has to be before it is a lake
/// rather than a marsh.
/// </summary>
/// <remarks>
/// The coefficients are deliberately mild and centred on 1 for the common land
/// covers, so river thresholds tuned on a uniform surface stay meaningful. They
/// encode direction, not precision: forest and jungle intercept and transpire,
/// sand drinks, bare rock and badlands shed, frozen ground is sealed.
/// </remarks>
public static class LandCover
{
    /// <summary>
    /// Provisional biome from the climate fields alone: the same ladder the biome stage runs, minus the
    /// facts only hydrology can supply (salt, glaciers). <paramref name="relief"/> is the largest height
    /// step to a neighbour, which splits sand desert from badlands exactly as the biome stage does.
    /// </summary>
    public static BiomeType Provisional(double height, double temperature, double precipitation, double relief)
        => BiomeClassifier.Classify(height, temperature, precipitation, new BiomeClassifier.AridSite(relief, false)).Biome;

    /// <summary>Fraction of a tile's reference rainfall that reaches the channel network (1 = bare reference ground).</summary>
    public static float RunoffCoefficient(BiomeType biome) => biome switch
    {
        BiomeType.Jungle => 0.90f,       // canopy interception and transpiration, offset by the sheer rainfall
        BiomeType.Forest => 0.90f,
        BiomeType.Swamp => 0.80f,        // storage in peat and standing water
        BiomeType.Desert => 0.70f,       // sand and alluvium soak rain in before it can gather
        BiomeType.SaltFlat => 0.85f,     // crusted, little infiltration but it ponds in place
        BiomeType.ColdDesert => 0.90f,
        BiomeType.RockDesert => 1.10f,   // pavement and scoured bedrock
        BiomeType.Badlands => 1.20f,     // bare clay, no soil: flash-flood ground
        BiomeType.Tundra => 1.10f,       // permafrost seals the ground
        BiomeType.Mountain => 1.15f,     // thin soil on steep rock
        _ => 1f,
    };

    /// <summary>
    /// Open-water evaporation multiplier from the air a lake sits in: hot dry air strips a surface far
    /// faster than humid air or cold air over the same temperature-driven baseline.
    /// </summary>
    public static float EvaporationFactor(BiomeType biome) => biome switch
    {
        BiomeType.Desert or BiomeType.Badlands or BiomeType.RockDesert or BiomeType.SaltFlat => 1.20f,
        BiomeType.ColdDesert => 1.05f,
        BiomeType.Jungle or BiomeType.Swamp => 0.88f,
        BiomeType.Forest => 0.95f,
        BiomeType.Tundra => 0.80f,
        BiomeType.Mountain => 0.85f,
        _ => 1f,
    };

    /// <summary>
    /// Water a lake bed leaks into the ground per tile, in runoff units. Sand and fractured rock are
    /// porous; peat, clay, salt crust and permafrost hold water.
    /// </summary>
    public static float Seepage(BiomeType biome) => biome switch
    {
        BiomeType.Desert => 0.12f,
        BiomeType.ColdDesert => 0.06f,
        BiomeType.RockDesert => 0.05f,
        BiomeType.Plains => 0.04f,
        BiomeType.Forest or BiomeType.Mountain or BiomeType.Badlands => 0.03f,
        BiomeType.Jungle => 0.02f,
        _ => 0f, // swamp, tundra, salt flat, ocean
    };

    /// <summary>
    /// Scale on <see cref="LakeSolver.MinLakeDepth"/>: a swamp fills a shallow hollow with peat and reeds
    /// until it reads as wetland, while thaw hollows in frozen ground hold open water at a hair's depth.
    /// </summary>
    public static float MinDepthScale(BiomeType biome) => biome switch
    {
        BiomeType.Swamp => 1.7f,
        BiomeType.Tundra => 0.7f,
        _ => 1f,
    };
}
