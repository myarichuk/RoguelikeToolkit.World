using System;
using System.Numerics;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Fixed debug colors for the visualizer's water color mode: rivers,
/// glaciers, and water bodies by kind. Land falls back to a dimmed biome
/// color supplied by the caller for context.
/// </summary>
public static class HydroPalette
{
    public static Vector3 ColorFor(WaterBodyKind kind) => kind switch
    {
        WaterBodyKind.Ocean => new Vector3(0.10f, 0.30f, 0.62f),
        WaterBodyKind.Sea => new Vector3(0.18f, 0.45f, 0.75f),
        WaterBodyKind.Lake => new Vector3(0.35f, 0.65f, 0.90f),
        _ => new Vector3(0.50f, 0.50f, 0.50f),
    };

    public static Vector3 ColorForRiver() => new Vector3(0.25f, 0.65f, 1.00f);

    /// <summary>River color brightened by discharge (headwaters dim, mouths bright).</summary>
    public static Vector3 ColorForRiver(float flow)
    {
        float t = MathF.Min(1f, MathF.Log10(1f + MathF.Max(0f, flow)) / 2.2f);
        return ColorForRiver() * (0.65f + 0.35f * t);
    }

    public static Vector3 ColorForGlacier() => new Vector3(0.92f, 0.96f, 1.00f);

    /// <summary>Dimmed land context so water stands out in the water mode.</summary>
    public static Vector3 DimLand(Vector3 biomeColor) => biomeColor * 0.45f;
}
