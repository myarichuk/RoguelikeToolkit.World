namespace RoguelikeToolkit.World.Core.Biomes;

/// <summary>Per-tile biome classification: the biome and how dangerous it is to travel through.</summary>
public struct BiomeInfo
{
    /// <summary>The biome this tile was classified as (after glacier/canyon overrides, smoothing and pins).</summary>
    public BiomeType Biome;
    /// <summary>Danger rating 0..5: the biome's base danger (see <see cref="BiomeClassifier.BaseDanger"/>) plus a deterministic per-tile jitter of 0 or 1, capped at 5.</summary>
    public byte DangerLevel;
}
