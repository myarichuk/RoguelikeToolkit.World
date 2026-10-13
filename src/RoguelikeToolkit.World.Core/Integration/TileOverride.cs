using RoguelikeToolkit.World.Core.Biomes;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// Per-tile override applied on top of generated geography. Replacement (not
/// additive): a set field wins over the generated value at query time.
/// </summary>
public sealed class TileOverride
{
    public BiomeType? Biome { get; init; }
    public int? DangerLevel { get; init; }
}
