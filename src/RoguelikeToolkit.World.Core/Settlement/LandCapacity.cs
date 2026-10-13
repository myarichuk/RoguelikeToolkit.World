using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>
/// Physical suitability for farming, herding, hunting, logging and fishing, 0 to 255 each. Every product
/// is a product of smooth factors (no hard cut-offs, so a threshold cannot stripe a coast into tile
/// speckle), scaled by what the biome's ground and cover allow.
/// <para>
/// Calibration is by direction, not precision, like <see cref="LandCover"/> and <see cref="SettlementScoring"/>:
/// the point is that every allocator ranks the same ground the same way. At planet scale a tile is tens to
/// hundreds of kilometres across, so "arable" means "the best the tile could do", not a field.
/// </para>
/// </summary>
public static class LandCapacity
{
    /// <summary>The size of a cold-current anomaly (in <see cref="ClimateInfo.Temperature"/> units) that counts as full upwelling.</summary>
    public const double UpwellingAnomaly = 0.04;

    private static double Ramp(double edge0, double edge1, double x)
    {
        double t = (x - edge0) / (edge1 - edge0);
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return t * t * (3 - 2 * t);
    }

    private static byte ToByte(double v) => v <= 0 ? (byte)0 : v >= 1 ? (byte)255 : (byte)(v * 255 + 0.5);

    /// <summary>Computes how much of each product the described tile can support.</summary>
    /// <remarks>
    /// Pure and deterministic. An ocean tile gets only <see cref="LandCapacityInfo.Fish"/>; any other tile gets arable, pasture, game and timber,
    /// and fish (at most half scale) only when <see cref="LandSite.OnFreshWater"/> is set.
    /// </remarks>
    public static LandCapacityInfo Evaluate(in LandSite s)
    {
        var result = new LandCapacityInfo();

        if (s.Biome == BiomeType.Ocean)
        {
            result.Fish = ToByte(OceanFish(s));
            return result;
        }

        double thermal = Ramp(0.20, 0.40, s.Temperature);
        double slope = 1 - Ramp(1.0, 4.0, s.Slope);
        double lowland = 1 - Ramp(0.20, 0.50, s.Height);

        // Tillage: a growing season, rain (or a river to draw on), level ground, low ground, usable soil.
        double rain = Ramp(0.20, 0.45, s.Precipitation) * (1 - 0.5 * Ramp(0.75, 0.95, s.Precipitation));
        double water = s.FreshWater ? Math.Max(rain, 0.70) : rain;
        double soil = ArableSoil(s.Biome, s.FreshWater);
        result.Arable = ToByte(thermal * water * slope * lowland * soil);

        // Grazing: grass survives on less rain and colder ground, tolerates slope and altitude.
        double grass = Ramp(0.10, 0.30, s.Precipitation) * (1 - 0.6 * Ramp(0.70, 0.95, s.Precipitation));
        double graze = Ramp(0.08, 0.22, s.Temperature) * grass
                       * (1 - 0.7 * Ramp(1.5, 6.0, s.Slope)) * (1 - 0.5 * Ramp(0.40, 0.60, s.Height));
        result.Pasture = ToByte(graze * PastureCover(s.Biome));

        // Game: biomass follows rain and warmth, cover follows the biome; water draws animals in.
        double productivity = 0.3 + 0.7 * Ramp(0.10, 0.50, s.Precipitation);
        double coldness = 0.5 + 0.5 * Ramp(0.05, 0.25, s.Temperature);
        double game = productivity * coldness * GameCover(s.Biome);
        if (s.FreshWater) game = Math.Min(1.0, game * 1.15);
        result.Game = ToByte(game);

        // Timber: standing wood needs rain; the biome says how much of the tile is wood.
        result.Timber = ToByte(TimberCover(s.Biome) * Ramp(0.35, 0.70, s.Precipitation) * (1 - 0.5 * Ramp(1.5, 6.0, s.Slope)));

        // Rivers and lakes are fishable even where the land beside them is not.
        if (s.OnFreshWater) result.Fish = ToByte(0.5 * Ramp(0.10, 0.30, s.Temperature));
        return result;
    }

    private static double OceanFish(in LandSite s)
    {
        // Productive water is shallow (a shelf, light reaches the bed) and close to land (nutrient run-off),
        // and richest where cold water wells up: the Humboldt and Benguela fisheries.
        double depth = Math.Max(0, ElevationGenerationStage.SeaLevel - s.Height);
        double shelf = 1 - Ramp(0.05, 0.40, depth);
        double coast = s.NextToLand ? 1.0 : 0.6;
        double upwelling = Ramp(0.0, UpwellingAnomaly, -s.OceanHeat);
        // Ice shuts the surface: a seasonal cover halves the catch, the permanent pack all but ends it.
        double polar = s.SeaIce switch { SeaIceKind.Perennial => 0.1, SeaIceKind.Seasonal => 0.5, _ => 1.0 };
        double tropic = 1 - 0.45 * Ramp(0.60, 0.90, s.Temperature);       // warm water is thin water
        return shelf * coast * (0.35 + 0.65 * upwelling) * polar * tropic;
    }

    private static double ArableSoil(BiomeType b, bool freshWater) => b switch
    {
        BiomeType.Plains => 1.0,
        BiomeType.Savanna => 0.75,       // good soil, but the dry season is a gamble
        BiomeType.Mediterranean => 0.85, // wheat in the wet winter, vine and olive through the dry summer
        BiomeType.Forest => 0.70,        // must be cleared
        BiomeType.Jungle => 0.45,        // leached laterite
        BiomeType.Swamp => 0.25,         // must be drained
        BiomeType.ColdDesert => 0.35,
        BiomeType.Desert or BiomeType.Badlands or BiomeType.RockDesert => freshWater ? 0.50 : 0.0, // an oasis, a delta
        BiomeType.Tundra => 0.05,
        _ => 0.0,                        // mountain, glacier, canyon, salt flat, ocean
    };

    private static double PastureCover(BiomeType b) => b switch
    {
        BiomeType.Plains or BiomeType.Savanna => 1.0,
        BiomeType.Mediterranean => 0.70, // garrigue: sheep and goats, but the summer grass is gone
        BiomeType.ColdDesert => 0.60,    // steppe
        BiomeType.Forest => 0.45,        // glades
        BiomeType.Tundra => 0.35,        // reindeer moss
        BiomeType.Mountain => 0.30,      // alpine summer pasture
        BiomeType.Swamp => 0.20,
        BiomeType.Desert => 0.12,
        BiomeType.Jungle or BiomeType.RockDesert or BiomeType.Badlands => 0.10,
        _ => 0.0,
    };

    private static double GameCover(BiomeType b) => b switch
    {
        BiomeType.Forest => 1.0,
        BiomeType.Savanna => 0.90,       // the great herds
        BiomeType.Plains => 0.80,
        BiomeType.Mediterranean => 0.75, // open woodland and scrub
        BiomeType.Jungle => 0.70,
        BiomeType.Swamp => 0.60,
        BiomeType.Tundra => 0.50,
        BiomeType.Mountain => 0.40,
        BiomeType.ColdDesert => 0.35,
        BiomeType.Canyon => 0.30,
        BiomeType.Badlands => 0.15,
        BiomeType.Desert => 0.12,
        BiomeType.RockDesert => 0.10,
        BiomeType.SaltFlat or BiomeType.Glacier => 0.05,
        _ => 0.0,
    };

    private static double TimberCover(BiomeType b) => b switch
    {
        BiomeType.Forest => 1.0,
        BiomeType.Jungle => 0.80,
        BiomeType.Mountain => 0.25,      // subalpine stands
        BiomeType.Swamp => 0.20,
        BiomeType.Plains => 0.10,        // copses and galleries
        BiomeType.Tundra => 0.02,
        _ => 0.0,
    };
}
