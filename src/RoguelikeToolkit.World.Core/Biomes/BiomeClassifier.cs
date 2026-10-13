using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;

namespace RoguelikeToolkit.World.Core.Biomes;

/// <summary>
/// The single biome decision ladder shared by the planet biome stage and the
/// local-map derivation, so a tile and the local map generated inside it can
/// never disagree because one copy of a threshold was retuned.
/// Glacier and canyon are not decided here: they need hydrology/glaciation
/// context the callers own, and take precedence over everything but ocean.
/// </summary>
public static class BiomeClassifier
{
    /// <summary>Height (<see cref="ElevationInfo.Height"/>) above which land is <see cref="BiomeType.Mountain"/>; strict, and tested right after the ocean cut-off.</summary>
    public const double MountainHeight = 0.5;
    /// <summary>Normalised temperature (0 polar .. 1 equatorial) below which land is <see cref="BiomeType.Tundra"/>; strict. Also the inclusive lower edge of the cold-desert band (<see cref="IsColdArid"/>).</summary>
    public const double TundraMaxTemperature = 0.18;
    /// <summary>Normalised moisture (0..1) ceiling for hot desert at <see cref="DesertMinTemperature"/>; the effective ceiling climbs with heat by <see cref="DesertHeatGain"/> (see <see cref="IsArid"/>).</summary>
    public const double DesertMaxMoisture = 0.32;
    /// <summary>Normalised temperature above which (strict) ground can be hot desert; also the inclusive upper edge of the cold-desert band (<see cref="IsColdArid"/>).</summary>
    public const double DesertMinTemperature = 0.55;

    /// <summary>
    /// How much the desert moisture ceiling rises per unit of temperature above
    /// <see cref="DesertMinTemperature"/>: evaporative demand grows with heat, so the same rainfall that
    /// feeds steppe at 40 degrees latitude is desert at 20. 0 restores the old flat threshold.
    /// </summary>
    public const double DesertHeatGain = 0.15;
    /// <summary>Normalised temperature above which (strict) wet ground is <see cref="BiomeType.Jungle"/>.</summary>
    public const double JungleMinTemperature = 0.72;
    /// <summary>Normalised moisture above which (strict) hot ground is <see cref="BiomeType.Jungle"/>.</summary>
    public const double JungleMinMoisture = 0.55;
    /// <summary>Normalised moisture above which (strict) warm ground is <see cref="BiomeType.Swamp"/>; tested after jungle, Mediterranean and savanna.</summary>
    public const double SwampMinMoisture = 0.65;
    /// <summary>Normalised temperature above which (strict) very wet ground is <see cref="BiomeType.Swamp"/> rather than forest.</summary>
    public const double SwampMinTemperature = 0.35;
    /// <summary>Normalised moisture above which (strict) ground no earlier rule claimed is <see cref="BiomeType.Forest"/>; drier ground is <see cref="BiomeType.Plains"/>.</summary>
    public const double ForestMinMoisture = 0.42;

    /// <summary>Savanna: warm enough that the dry season is a drought, not a winter.</summary>
    public const double SavannaMinTemperature = 0.62;

    /// <summary>Savanna: how unevenly the rain must fall (see <see cref="ClimateModel.RainSeasonality"/>), 0 even .. 1 monsoon.</summary>
    public const double SavannaMinSeasonality = 0.60;

    /// <summary>Savanna: wetter than this and the year-round-wet-enough ground carries forest or jungle instead.</summary>
    public const double SavannaMaxMoisture = 0.60;

    /// <summary>
    /// Mediterranean: how far towards winter the rain must fall (<see cref="ClimateInfo.RainPhase"/>, +1 all in winter,
    /// -1 all in summer). The phase's magnitude is the rain seasonality, so this is "winter-wet and at least this
    /// seasonal": on Earth it holds from 37 to 47 degrees, the basin's own latitudes.
    /// </summary>
    public const double MediterraneanMinPhase = 0.60;

    /// <summary>Mediterranean: cool enough that winter is the growing season, warm enough for olives (about 45 degrees on Earth).</summary>
    public const double MediterraneanMinTemperature = 0.50;

    /// <summary>Mediterranean: wetter than this and the same winter rain feeds temperate forest instead of scrub.</summary>
    public const double MediterraneanMaxMoisture = 0.55;
    /// <summary>Height below which (strict) ocean is deep: base danger 2 instead of 0 in <see cref="Classify"/>.</summary>
    public const double DeepOceanHeight = -0.6;

    /// <summary>A saline site is a salt flat in any climate drier than this (a sump that stays dry is arid by definition).</summary>
    public const double PlayaMaxMoisture = 0.50;

    /// <summary>Cold steppe-desert: dry enough to starve grass, below the hot-desert temperature.</summary>
    public const double ColdDesertMaxMoisture = 0.22;

    /// <summary>Largest height step to a neighbour above which hot arid ground is dissected badlands.</summary>
    public const double BadlandsRelief = 0.12;

    /// <summary>Hot arid ground at or above this height is stony plateau rather than sand.</summary>
    public const double RockDesertHeight = 0.30;

    /// <summary>
    /// Terrain facts the arid sub-types depend on and the three scalar inputs cannot carry.
    /// <paramref name="Relief"/> is the largest height step to a neighbouring tile and
    /// <paramref name="Saline"/> marks ground that holds salt: a dry endorheic basin floor, or the
    /// shore of a lake in dry country, where evaporation leaves the crust behind.
    /// </summary>
    public readonly record struct AridSite(double Relief, bool Saline);

    /// <summary>True for every member of the desert family, hot or cold.</summary>
    public static bool IsDesert(BiomeType biome)
        => biome is BiomeType.Desert or BiomeType.Badlands or BiomeType.RockDesert
            or BiomeType.SaltFlat or BiomeType.ColdDesert;

    /// <summary>Cold steppe-desert test: above the tundra line, below hot desert, and very dry.</summary>
    public static bool IsColdArid(double temperature, double moisture)
        => temperature >= TundraMaxTemperature && temperature <= DesertMinTemperature
           && moisture < ColdDesertMaxMoisture;

    /// <summary>
    /// Aridity test: hot enough to need the rain it lacks. The moisture ceiling is
    /// <see cref="DesertMaxMoisture"/> at the cold edge and climbs with temperature, a crude
    /// precipitation-versus-evaporation balance, so desert follows the heat as well as the dry.
    /// </summary>
    public static bool IsArid(double temperature, double moisture)
        => temperature > DesertMinTemperature
           && moisture < DesertMaxMoisture + DesertHeatGain * (temperature - DesertMinTemperature);

    /// <summary>
    /// Savanna test for ground already known not to be arid: warm, not wet enough for forest-grade rain,
    /// and with a pronounced rainy season that is the summer one (a winter-wet place is Mediterranean, not savanna).
    /// With no seasonality data (0) it never holds. <paramref name="rainPhase"/> is optional; without it the season is not checked.
    /// </summary>
    public static bool IsSavanna(double temperature, double moisture, double rainSeasonality, double rainPhase = 0.0)
        => temperature >= SavannaMinTemperature && moisture <= SavannaMaxMoisture
           && rainSeasonality >= SavannaMinSeasonality && rainPhase <= 0.0;

    /// <summary>
    /// Mediterranean test for ground already known not to be arid or jungle: temperate-warm, not wet enough for forest,
    /// and with rain that falls in winter. With no phase data (0) it never holds.
    /// </summary>
    public static bool IsMediterranean(double temperature, double moisture, double rainPhase)
        => temperature >= MediterraneanMinTemperature && moisture <= MediterraneanMaxMoisture
           && rainPhase >= MediterraneanMinPhase;

    /// <summary>Base danger of the moisture-threshold biomes; <see cref="Classify"/> returns the same values.</summary>
    public static byte MoistureBiomeDanger(BiomeType biome) => biome switch
    {
        BiomeType.Jungle or BiomeType.Swamp => 2,
        BiomeType.Forest or BiomeType.Savanna or BiomeType.Mediterranean => 1,
        _ => 0,
    };

    /// <summary>Base danger of the desert family; <see cref="Classify"/> returns the same values.</summary>
    public static byte AridBiomeDanger(BiomeType biome) => biome switch
    {
        BiomeType.Badlands => 3,
        BiomeType.Desert or BiomeType.RockDesert or BiomeType.SaltFlat => 2,
        BiomeType.ColdDesert => 1,
        _ => 0,
    };

    /// <summary>Base danger of any biome <see cref="Classify"/> can return from moisture or aridity.</summary>
    public static byte BaseDanger(BiomeType biome)
        => IsDesert(biome) ? AridBiomeDanger(biome) : MoistureBiomeDanger(biome);

    /// <summary>
    /// Sub-type of a tile already known to be arid (salt flats are decided earlier, in <see cref="Classify"/>). Hot ground splits by relief (dissected badlands), then height (stony plateau)
    /// and defaults to sand desert, while the cold side is steppe-desert.
    /// </summary>
    public static BiomeType AridType(double height, bool hot, AridSite site)
    {
        if (!hot) return BiomeType.ColdDesert;
        if (site.Relief >= BadlandsRelief) return BiomeType.Badlands;
        if (height >= RockDesertHeight) return BiomeType.RockDesert;
        return BiomeType.Desert;
    }

    /// <summary>Biome and base danger for a tile, before glacier/canyon overrides and jitter.</summary>
    /// <remarks>
    /// <paramref name="rainSeasonality"/> and <paramref name="rainPhase"/> are optional: callers with no seasonal-rain data
    /// pass nothing and get neither savanna nor Mediterranean.
    /// </remarks>
    public static (BiomeType Biome, byte Danger) Classify(double height, double temperature, double moisture, AridSite site = default, double rainSeasonality = 0.0, double rainPhase = 0.0)
    {
        if (height < ElevationGenerationStage.SeaLevel)
            return (BiomeType.Ocean, height < DeepOceanHeight ? (byte)2 : (byte)0);
        if (height > MountainHeight) return (BiomeType.Mountain, 3);
        if (temperature < TundraMaxTemperature) return (BiomeType.Tundra, 1);
        if (site.Saline && moisture < PlayaMaxMoisture) return (BiomeType.SaltFlat, AridBiomeDanger(BiomeType.SaltFlat));
        if (IsArid(temperature, moisture))
        {
            var type = AridType(height, true, site);
            return (type, AridBiomeDanger(type));
        }
        if (IsColdArid(temperature, moisture))
        {
            var type = AridType(height, false, site);
            return (type, AridBiomeDanger(type));
        }
        if (temperature > JungleMinTemperature && moisture > JungleMinMoisture) return (BiomeType.Jungle, 2);
        // Mediterranean (winter rain) and savanna (summer rain) cannot both hold: they need opposite signs of the phase.
        // Neither can overlap jungle, which starts above the moisture ceiling Mediterranean stops at. The order is for the reader.
        if (IsMediterranean(temperature, moisture, rainPhase)) return (BiomeType.Mediterranean, 1);
        if (IsSavanna(temperature, moisture, rainSeasonality, rainPhase)) return (BiomeType.Savanna, 1);
        if (moisture > SwampMinMoisture && temperature > SwampMinTemperature) return (BiomeType.Swamp, 2);
        if (moisture > ForestMinMoisture) return (BiomeType.Forest, 1);
        return (BiomeType.Plains, 0);
    }
}
