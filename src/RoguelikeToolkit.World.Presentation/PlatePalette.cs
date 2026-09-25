using System.Numerics;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Deterministic debug colors for tectonic plates. A pure function of the plate id:
/// stable across runs, processes, and tile iteration order (unlike a seeded
/// <see cref="System.Random"/> consumed in enumeration order).
/// </summary>
public static class PlatePalette
{
    public static Vector3 ColorFor(int plateId)
    {
        // 32-bit finalizer (MurmurHash3 fmix32) over the salted id.
        uint h = unchecked((uint)plateId + 0x9E3779B9u);
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;

        // Three channels from non-overlapping bit ranges, biased away from near-black
        // so plates stay visible under the shader's ambient + diffuse lighting.
        float r = 0.2f + 0.8f * ((h & 0xFFu) / 255f);
        float g = 0.2f + 0.8f * (((h >> 8) & 0xFFu) / 255f);
        float b = 0.2f + 0.8f * (((h >> 16) & 0xFFu) / 255f);
        return new Vector3(r, g, b);
    }
}
