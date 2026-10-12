using System.Numerics;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Fixed debug colors per biome type for the visualizer's biome color mode.
/// Tundra is cold khaki-olive, deliberately far from Glacier ice-white
/// (backlog E5): the two read as land vs ice, not as two snows.
/// </summary>
public static class BiomePalette
{
    public static Vector3 ColorFor(BiomeType biome) => biome switch
    {
        BiomeType.Ocean => new Vector3(0.15f, 0.35f, 0.65f),
        BiomeType.Plains => new Vector3(0.45f, 0.65f, 0.30f),
        BiomeType.Desert => new Vector3(0.85f, 0.75f, 0.45f),
        BiomeType.Forest => new Vector3(0.15f, 0.45f, 0.20f),
        BiomeType.Mountain => new Vector3(0.55f, 0.55f, 0.60f),
        BiomeType.Tundra => new Vector3(0.62f, 0.60f, 0.45f),
        BiomeType.Jungle => new Vector3(0.10f, 0.50f, 0.25f),
        BiomeType.Swamp => new Vector3(0.35f, 0.45f, 0.25f),
        BiomeType.Glacier => new Vector3(0.88f, 0.93f, 0.97f),
        BiomeType.Canyon => new Vector3(0.70f, 0.36f, 0.20f),
        // The desert family stays in the yellow-red-white range but spreads far enough apart to tell at a glance.
        BiomeType.Badlands => new Vector3(0.66f, 0.38f, 0.24f),
        BiomeType.RockDesert => new Vector3(0.63f, 0.56f, 0.48f),
        BiomeType.SaltFlat => new Vector3(0.93f, 0.92f, 0.86f),
        BiomeType.ColdDesert => new Vector3(0.72f, 0.69f, 0.58f),
        _ => new Vector3(0.50f, 0.50f, 0.50f),
    };
}
