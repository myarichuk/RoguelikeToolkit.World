namespace RoguelikeToolkit.World.Core;

/// <summary>
/// The single biome decision ladder shared by the planet biome stage and the
/// local-map derivation, so a tile and the local map generated inside it can
/// never disagree because one copy of a threshold was retuned.
/// Glacier and canyon are not decided here: they need hydrology/glaciation
/// context the callers own, and take precedence over everything but ocean.
/// </summary>
public static class BiomeClassifier
{
    public const double MountainHeight = 0.5;
    public const double TundraMaxTemperature = 0.18;
    public const double DesertMaxMoisture = 0.32;
    public const double DesertMinTemperature = 0.55;
    public const double JungleMinTemperature = 0.72;
    public const double JungleMinMoisture = 0.55;
    public const double SwampMinMoisture = 0.65;
    public const double SwampMinTemperature = 0.35;
    public const double ForestMinMoisture = 0.42;
    public const double DeepOceanHeight = -0.6;

    /// <summary>Base danger of the moisture-threshold biomes; <see cref="Classify"/> returns the same values.</summary>
    public static byte MoistureBiomeDanger(BiomeType biome) => biome switch
    {
        BiomeType.Jungle or BiomeType.Swamp => 2,
        BiomeType.Forest => 1,
        _ => 0,
    };

    /// <summary>Biome and base danger for a tile, before glacier/canyon overrides and jitter.</summary>
    public static (BiomeType Biome, byte Danger) Classify(double height, double temperature, double moisture)
    {
        if (height < ElevationGenerationStage.SeaLevel)
            return (BiomeType.Ocean, height < DeepOceanHeight ? (byte)2 : (byte)0);
        if (height > MountainHeight) return (BiomeType.Mountain, 3);
        if (temperature < TundraMaxTemperature) return (BiomeType.Tundra, 1);
        if (moisture < DesertMaxMoisture && temperature > DesertMinTemperature) return (BiomeType.Desert, 2);
        if (temperature > JungleMinTemperature && moisture > JungleMinMoisture) return (BiomeType.Jungle, 2);
        if (moisture > SwampMinMoisture && temperature > SwampMinTemperature) return (BiomeType.Swamp, 2);
        if (moisture > ForestMinMoisture) return (BiomeType.Forest, 1);
        return (BiomeType.Plains, 0);
    }
}
