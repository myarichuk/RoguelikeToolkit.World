using RoguelikeToolkit.World.Core.Biomes;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// Per-tile override applied on top of generated geography. Replacement (not
/// additive): a set field wins over the generated value at query time.
/// </summary>
public sealed class TileOverride
{
    /// <summary>Biome that replaces the generated one, or null to keep it.</summary>
    public BiomeType? Biome { get; init; }
    /// <summary>Danger level that replaces the generated one, or null to keep it. Consumers clamp it into their own range (byte for tile features and city scoring, non-negative for biome sampling).</summary>
    public int? DangerLevel { get; init; }
}
