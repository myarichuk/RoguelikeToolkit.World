using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Sites;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>
/// Habitability weights shared by the planet-scale <see cref="CitySiteScorer"/>
/// and the region/local <see cref="SettlementInjector"/>, so both rank terrain
/// the same way.
/// </summary>
public static class SettlementScoring
{
    public const double Base = 1.0;
    public const double FreshWater = 1.0;
    public const double Fertile = 0.5;
    public const double Harsh = -0.5;
    public const double HighGround = -0.3;
    public const float HighGroundHeight = 0.35f;
    public const double PerDanger = 0.2;
    /// <summary>
    /// A harbour that the sea closes for part of the year: every sea tile it touches is <see cref="SeaIceKind.Seasonal"/>
    /// ice. Water access still counts (the sea thaws, and the ice is a source of water), so this is a quarter-point
    /// judgement, not a calibration: a fifth of the weight a settlement gives to fresh water would be too little to
    /// reorder anything, and the full weight would erase the coast.
    /// </summary>
    public const double WinterIce = -0.25;

    /// <summary>Score for one tile. When <paramref name="reasons"/> is given, the terms that applied are appended.</summary>
    public static double Score(
        bool water, BiomeType biome, float elevation, int effectiveDanger, float habitabilityDelta,
        List<string>? reasons = null, bool winterIce = false)
    {
        double score = Base;
        if (water) { score += FreshWater; reasons?.Add("water"); }
        if (biome is BiomeType.Plains or BiomeType.Forest or BiomeType.Savanna or BiomeType.Mediterranean) { score += Fertile; reasons?.Add("fertile"); }
        if (biome is BiomeType.Tundra or BiomeType.Mountain or BiomeType.Canyon || BiomeClassifier.IsDesert(biome)) { score += Harsh; reasons?.Add("harsh"); }
        if (winterIce) { score += WinterIce; reasons?.Add("winter-ice"); }
        if (elevation > HighGroundHeight) { score += HighGround; reasons?.Add("high"); }
        score -= effectiveDanger * PerDanger;
        score += habitabilityDelta;
        return score;
    }
}
