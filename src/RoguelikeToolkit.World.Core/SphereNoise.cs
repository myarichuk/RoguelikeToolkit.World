using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Seamless 3D value noise over unit-sphere positions. Operating on 3D vectors
/// (rather than lat/lon) avoids pole and dateline seams by construction.
/// Deterministic across runs and platforms for the same inputs.
/// </summary>
public static class SphereNoise
{
    public static double Value(Vector3D p, int seed)
    {
        long xi = (long)Math.Floor(p.X);
        long yi = (long)Math.Floor(p.Y);
        long zi = (long)Math.Floor(p.Z);

        double xf = p.X - xi;
        double yf = p.Y - yi;
        double zf = p.Z - zi;

        double u = Fade(xf);
        double v = Fade(yf);
        double w = Fade(zf);

        double c000 = Hash01(xi, yi, zi, seed);
        double c100 = Hash01(xi + 1, yi, zi, seed);
        double c010 = Hash01(xi, yi + 1, zi, seed);
        double c110 = Hash01(xi + 1, yi + 1, zi, seed);
        double c001 = Hash01(xi, yi, zi + 1, seed);
        double c101 = Hash01(xi + 1, yi, zi + 1, seed);
        double c011 = Hash01(xi, yi + 1, zi + 1, seed);
        double c111 = Hash01(xi + 1, yi + 1, zi + 1, seed);

        double x00 = Lerp(c000, c100, u);
        double x10 = Lerp(c010, c110, u);
        double x01 = Lerp(c001, c101, u);
        double x11 = Lerp(c011, c111, u);

        double y0 = Lerp(x00, x10, v);
        double y1 = Lerp(x01, x11, v);

        return Lerp(y0, y1, w) * 2.0 - 1.0;
    }

    public static double Fbm(Vector3D p, int seed, int octaves = 4, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0.0;
        double amp = 1.0;
        double norm = 0.0;
        var q = p;

        for (int o = 0; o < octaves; o++)
        {
            sum += Value(q, seed + o * 101) * amp;
            norm += amp;
            amp *= gain;
            q = q * lacunarity;
        }

        return norm > 0 ? sum / norm : 0.0;
    }

    private static double Fade(double t) => t * t * (3.0 - 2.0 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Hash01(long x, long y, long z, int seed)
    {
        unchecked
        {
            ulong h = (ulong)(x * 73856093L ^ y * 19349663L ^ z * 83492791L)
                + (ulong)(uint)seed * 0x9E3779B97F4A7C15ul;
            h ^= h >> 30;
            h *= 0xBF58476D1CE4E5B9ul;
            h ^= h >> 27;
            h *= 0x94D049BB133111EBul;
            h ^= h >> 31;
            return (h >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
