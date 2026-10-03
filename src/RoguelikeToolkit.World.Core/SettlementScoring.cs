using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

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

    /// <summary>Score for one tile. When <paramref name="reasons"/> is given, the terms that applied are appended.</summary>
    public static double Score(
        bool water, BiomeType biome, float elevation, int effectiveDanger, float habitabilityDelta,
        List<string>? reasons = null)
    {
        double score = Base;
        if (water) { score += FreshWater; reasons?.Add("water"); }
        if (biome is BiomeType.Plains or BiomeType.Forest) { score += Fertile; reasons?.Add("fertile"); }
        if (biome is BiomeType.Tundra or BiomeType.Desert or BiomeType.Mountain or BiomeType.Canyon) { score += Harsh; reasons?.Add("harsh"); }
        if (elevation > HighGroundHeight) { score += HighGround; reasons?.Add("high"); }
        score -= effectiveDanger * PerDanger;
        score += habitabilityDelta;
        return score;
    }
}
