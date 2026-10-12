using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// The facts about one tile the capacity model needs. A plain value so plugins, tests and the editor can
/// score a hypothetical tile without a world.
/// </summary>
/// <param name="Biome">The tile's final biome.</param>
/// <param name="Height">Surface height (sea level is 0).</param>
/// <param name="Temperature">Annual-mean <see cref="ClimateInfo.Temperature"/>.</param>
/// <param name="Precipitation"><see cref="ClimateInfo.Precipitation"/>.</param>
/// <param name="Slope">Steepest height change to a neighbour per unit of arc on the unit sphere. A gradient, not a step, so it means the same thing at every world size (a raw step shrinks as tiles get smaller).</param>
/// <param name="FreshWater">A river channel or lake on this tile or a neighbour: irrigation water within reach.</param>
/// <param name="OnFreshWater">This tile itself is a lake or carries a river (a fishable surface).</param>
/// <param name="Saline">The ground holds salt.</param>
/// <param name="OceanHeat">The ocean-current anomaly (<see cref="ClimateInfo.OceanHeat"/>); negative beside a cold current.</param>
/// <param name="NextToLand">A neighbour is land, so an ocean tile is on the coast.</param>
public readonly record struct LandSite(
    BiomeType Biome,
    double Height,
    double Temperature,
    double Precipitation,
    double Slope,
    bool FreshWater,
    bool OnFreshWater,
    bool Saline,
    double OceanHeat,
    bool NextToLand);

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
        double polar = 0.4 + 0.6 * Ramp(0.05, 0.20, s.Temperature);      // pack ice shuts the surface
        double tropic = 1 - 0.45 * Ramp(0.60, 0.90, s.Temperature);       // warm water is thin water
        return shelf * coast * (0.35 + 0.65 * upwelling) * polar * tropic;
    }

    private static double ArableSoil(BiomeType b, bool freshWater) => b switch
    {
        BiomeType.Plains => 1.0,
        BiomeType.Savanna => 0.75,       // good soil, but the dry season is a gamble
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

/// <summary>
/// Fills <see cref="LandCapacityInfo"/> from the finished physics and *resets* <see cref="LandUseInfo"/> to
/// "nothing decided". It is the first writer of the land-use layer, so the pipeline's clearing rule gives
/// every allocation stage a clean slate on every run (including <see cref="World.Recompute"/>): a plugin
/// that reads and writes <see cref="LandUseInfo"/> refines what the stages before it decided, exactly like
/// erosion refines elevation. Not attribute-discovered; <see cref="WorldBuilder.WithLandUse"/> adds it.
/// <para>
/// Order 25 sits after the biome stage (20) and the 21..24 gap for biome and danger tweaks, so capacity
/// reads the biome the world finally has. Allocation plugins go at 26..98; <see cref="LandUseResolveStage"/>
/// closes the pipeline at 99.
/// </para>
/// </summary>
[Preserve]
public sealed class LandCapacityStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed
{
    public const int StageOrder = 25;

    public string Name => "LandCapacity";
    public int Order => StageOrder;
    public Type[] Reads => new[] { typeof(ElevationInfo), typeof(LocalMapInfo) };
    public Type[] ReadsOptional => new[] { typeof(ClimateInfo), typeof(HydrologyInfo) };
    public Type[] Writes => new[] { typeof(LandCapacityInfo), typeof(LandUseInfo) };

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var local = store.GetSpan<LocalMapInfo>();
        var capacity = store.GetSpan<LandCapacityInfo>();
        var use = store.GetSpan<LandUseInfo>();
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;

        var vectors = store.GetTileVectors();
        Span<int> nb = stackalloc int[6];
        for (int i = 0; i < capacity.Length; i++)
        {
            use[i] = default;

            double h = elev[i].Height;
            double slope = 0;
            bool fresh = hasHydro && IsFresh(hydro[i]);
            bool onFresh = fresh;
            bool nextToLand = false;
            int n = store.GetAdjacent(i, nb);
            for (int k = 0; k < n; k++)
            {
                int j = nb[k];
                double arc = (vectors[j] - vectors[i]).Length;
                if (arc > 0) slope = Math.Max(slope, Math.Abs(elev[j].Height - h) / arc);
                if (hasHydro && IsFresh(hydro[j])) fresh = true;
                if (elev[j].Height >= ElevationGenerationStage.SeaLevel) nextToLand = true;
            }

            capacity[i] = LandCapacity.Evaluate(new LandSite(
                local[i].Biome, h,
                hasClimate ? climate[i].Temperature : 0.5,
                hasClimate ? climate[i].Precipitation : 0.5,
                slope, fresh, onFresh,
                hasHydro && hydro[i].Saline != 0,
                hasClimate ? climate[i].OceanHeat : 0,
                nextToLand));
        }
    }

    private static bool IsFresh(in HydrologyInfo h) => h.IsRiver != 0 || h.LakeDepth > 0f;
}

/// <summary>
/// The last stage of a land-use pipeline: applies hand-painted pins (<see cref="UserEditFlags.PinLandUse"/>)
/// over whatever the allocation stages decided, so an edit always wins and a plugin never has to know about
/// the editor. Order 99.
/// </summary>
[Preserve]
public sealed class LandUseResolveStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed
{
    public const int StageOrder = 99;

    public string Name => "LandUseResolve";
    public int Order => StageOrder;
    public Type[] Reads => new[] { typeof(LandUseInfo), typeof(UserEditInfo) };
    public Type[] ReadsOptional => Array.Empty<Type>();
    public Type[] Writes => new[] { typeof(LandUseInfo) };

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var use = store.GetSpan<LandUseInfo>();
        var pins = store.GetSpan<UserEditInfo>();
        for (int i = 0; i < use.Length; i++)
        {
            if ((pins[i].Flags & UserEditFlags.PinLandUse) == 0) continue;
            use[i] = new LandUseInfo
            {
                Use = pins[i].LandUse,
                Activities = pins[i].LandActivities,
                Intensity = pins[i].LandUse == LandUseId.None && pins[i].LandActivities == 0 ? (byte)0 : (byte)255,
            };
        }
    }
}
