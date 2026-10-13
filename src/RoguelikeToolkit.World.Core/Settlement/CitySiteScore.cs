using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Settlement;

public sealed class CitySiteScore
{
    public int TileIndex { get; init; }
    public GeoCoord Coord { get; init; }
    public double Score { get; init; }
    public BiomeType Biome { get; init; }
    public float Elevation { get; init; }
    public string Reasons { get; init; } = string.Empty;
}
