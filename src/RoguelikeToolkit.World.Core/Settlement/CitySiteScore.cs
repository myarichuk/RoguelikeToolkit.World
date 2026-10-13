using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>One scored candidate returned by <see cref="CitySiteScorer.Score"/>.</summary>
public sealed class CitySiteScore
{
    /// <summary>Index of the candidate tile in the store.</summary>
    public int TileIndex { get; init; }
    /// <summary>Tile centre; latitude and longitude in degrees.</summary>
    public GeoCoord Coord { get; init; }
    /// <summary><see cref="SettlementScoring.Score"/> result; higher is better, and it can be negative.</summary>
    public double Score { get; init; }
    /// <summary>The biome the score used, after any tile override.</summary>
    public BiomeType Biome { get; init; }
    /// <summary>Stored tile height (0 without an elevation layer); history and overrides do not change it.</summary>
    public float Elevation { get; init; }
    /// <summary>Comma-separated tags for the terms that applied: <c>water</c>, <c>fertile</c>, <c>harsh</c>, <c>winter-ice</c>, <c>high</c>, then <c>history</c> and <c>past-events</c> for non-zero history deltas. Empty when none.</summary>
    public string Reasons { get; init; } = string.Empty;
}
