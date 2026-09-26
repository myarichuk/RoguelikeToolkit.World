using System.Numerics;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Fixed debug colors per biome type for the visualizer's biome color mode.
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
        BiomeType.Tundra => new Vector3(0.80f, 0.85f, 0.90f),
        BiomeType.Jungle => new Vector3(0.10f, 0.50f, 0.25f),
        BiomeType.Swamp => new Vector3(0.35f, 0.45f, 0.25f),
        BiomeType.Glacier => new Vector3(0.88f, 0.93f, 0.97f),
        BiomeType.Canyon => new Vector3(0.70f, 0.36f, 0.20f),
        _ => new Vector3(0.50f, 0.50f, 0.50f),
    };
}
