using System;
using System.Numerics;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// CPU-side model for the visualizer's Terrain 3D view: radial height
/// displacement plus textured per-tile colors. Keeps the "remove the hexes"
/// look in one testable place; <c>GlControl</c> only uploads the results and
/// the fragment shader adds fine grain on top.
/// </summary>
public static class TerrainShading
{
    public const float DefaultHeightScale = 0.16f;
    public const float SeaLevel = 0f;

    /// <summary>
    /// Sphere radius for one tile. Land rises with height, ocean sinks gently
    /// (bathymetry), river channels carve a V, lake basins bow slightly.
    /// Pure function of its inputs.
    /// </summary>
    public static float DisplacedRadius(
        float height, bool isRiver, float flow, float lakeDepth, float heightScale = DefaultHeightScale)
    {
        float scale = heightScale <= 0f ? DefaultHeightScale : heightScale;
        float r = height < 0f
            ? 1f + height * 0.025f
            : 1f + height * scale;

        if (lakeDepth > 0f)
            r -= MathF.Min(lakeDepth * 0.02f, 0.02f);

        if (isRiver && height >= 0f)
        {
            float t = flow <= 0f ? 0.3f : MathF.Min(1f, MathF.Log10(1f + flow) / 2.2f);
            r -= 0.004f + 0.006f * t;
        }

        return MathF.Min(1.38f, MathF.Max(0.93f, r));
    }

    /// <summary>
    /// Textured terrain color for one tile: biome base with elevation bands
    /// (beach, rock, snow), water/river/lake blues, glacier ice, and a small
    /// deterministic per-tile variation so forests and jungles read as canopy
    /// instead of flat fills.
    /// </summary>
    public static Vector3 ColorFor(
        BiomeType biome,
        float height,
        bool isRiver,
        float flow,
        float lakeDepth,
        WaterBodyKind? waterKind,
        bool isGlacier,
        bool isPlaya,
        float temperature,
        float precipitation,
        int tileIndex)
    {
        float vary = (Hash01(tileIndex) - 0.5f) * 0.10f;
        float canopy = (Hash01(tileIndex * 31 + 7) - 0.5f) * 0.22f;

        if (isGlacier)
            return Shade(new Vector3(0.80f, 0.89f, 0.95f), vary);

        if (isRiver && height >= SeaLevel && lakeDepth <= 0f)
        {
            float t = flow <= 0f ? 0.3f : MathF.Min(1f, MathF.Log10(1f + flow) / 2.2f);
            return Shade(new Vector3(0.20f, 0.55f, 0.90f) * (0.8f + 0.25f * t), vary * 0.5f);
        }

        if (lakeDepth > 0f || waterKind == WaterBodyKind.Lake)
        {
            float depth = MathF.Min(1f, lakeDepth * 2f);
            var lake = Lerp(new Vector3(0.30f, 0.58f, 0.85f), new Vector3(0.10f, 0.32f, 0.66f), depth);
            return Shade(lake, vary * 0.5f);
        }

        if (height < SeaLevel || waterKind == WaterBodyKind.Ocean || waterKind == WaterBodyKind.Sea)
        {
            float depth = MathF.Min(1f, MathF.Max(0f, -height) * 1.4f);
            var ocean = Lerp(new Vector3(0.13f, 0.42f, 0.68f), new Vector3(0.03f, 0.13f, 0.34f), depth);
            return Shade(ocean, vary * 0.5f);
        }

        if (isPlaya)
            return Shade(new Vector3(0.87f, 0.84f, 0.74f), vary);

        Vector3 land = biome switch
        {
            BiomeType.Desert => new Vector3(0.83f, 0.70f, 0.42f),
            BiomeType.Forest => Lerp(new Vector3(0.12f, 0.33f, 0.15f), new Vector3(0.20f, 0.47f, 0.20f), 0.5f + canopy),
            BiomeType.Jungle => Lerp(new Vector3(0.04f, 0.30f, 0.13f), new Vector3(0.10f, 0.46f, 0.18f), 0.5f + canopy),
            BiomeType.Swamp => new Vector3(0.30f, 0.36f, 0.20f),
            BiomeType.Mountain => new Vector3(0.45f, 0.42f, 0.38f),
            BiomeType.Tundra => new Vector3(0.58f, 0.62f, 0.55f),
            BiomeType.Glacier => new Vector3(0.80f, 0.89f, 0.95f),
            BiomeType.Canyon => new Vector3(0.62f, 0.34f, 0.18f),
            BiomeType.Ocean => new Vector3(0.82f, 0.74f, 0.55f),
            _ => Lerp(new Vector3(0.62f, 0.62f, 0.35f), new Vector3(0.40f, 0.60f, 0.27f),
                MathF.Min(1f, MathF.Max(0f, precipitation) * 1.4f)),
        };

        // Dry coasts read as beach.
        if (height < 0.035f)
            land = Lerp(new Vector3(0.82f, 0.74f, 0.55f), land, height / 0.035f);

        // Rock above the treeline, snow above the snowline (hotter = higher).
        float rockT = MathF.Min(1f, MathF.Max(0f, (height - 0.30f) / 0.25f));
        land = Lerp(land, new Vector3(0.45f, 0.42f, 0.38f), rockT * 0.85f);

        float snowline = 0.35f + MathF.Min(1f, MathF.Max(0f, temperature)) * 0.45f;
        float snowT = MathF.Min(1f, MathF.Max(0f, (height - snowline) / 0.08f));
        if (height > 0.80f) snowT = 1f;
        land = Lerp(land, new Vector3(0.90f, 0.92f, 0.95f), snowT);

        return Shade(land, vary);
    }

    private static Vector3 Shade(Vector3 c, float vary) => c * (1f + vary);

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary>Deterministic 0..1 hash for per-tile texture variation.</summary>
    internal static float Hash01(int n)
    {
        uint x = (uint)(n * 2654435761 + 1013904223);
        x ^= x >> 15;
        x *= 2246822519u;
        x ^= x >> 13;
        return (x & 0xFFFFFF) / (float)0x1000000;
    }
}
