using System.Runtime.InteropServices.JavaScript;
using RoguelikeToolkit.World.Core;

namespace WebMap.Engine;

/// <summary>
/// The worker-facing surface. Generation runs once; the JS side then pulls flat arrays
/// (topology + fields) and does all rasterization/rendering itself. Nothing here knows
/// about pixels: Core hands over its native tile graph, the map editor owns the rest.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public static partial class WorldExports
{
    private static World? _world;
    private static TriangleIndices[]? _faces;

    [JSExport]
    public static int Generate(int seed, int size)
    {
        _world?.Dispose();
        _world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        // The store discards faces after deriving adjacency; regenerate them (deterministic, same vertex order).
        IcosphereGenerator.Generate(size, out _, out _faces);
        return _world.TileCount;
    }

    /// <summary>Unit vectors of tile centers, xyz interleaved.</summary>
    [JSExport]
    public static double[] TileVectors()
    {
        var v = World.Map.DataStore.GetTileVectors();
        var result = new double[v.Length * 3];
        for (int i = 0; i < v.Length; i++)
        {
            result[i * 3] = v[i].X;
            result[i * 3 + 1] = v[i].Y;
            result[i * 3 + 2] = v[i].Z;
        }
        return result;
    }

    /// <summary>Icosphere triangles over tile indices, three per face.</summary>
    [JSExport]
    public static int[] Faces()
    {
        var f = _faces ?? throw new InvalidOperationException("Generate first.");
        var result = new int[f.Length * 3];
        for (int i = 0; i < f.Length; i++)
        {
            result[i * 3] = f[i].v1;
            result[i * 3 + 1] = f[i].v2;
            result[i * 3 + 2] = f[i].v3;
        }
        return result;
    }

    /// <summary>Per-tile height; sea level is 0.</summary>
    [JSExport]
    public static double[] Heights()
    {
        var h = World.Map.DataStore.GetSpan<ElevationInfo>();
        var result = new double[h.Length];
        for (int i = 0; i < h.Length; i++) result[i] = h[i].Height;
        return result;
    }

    /// <summary>Per-tile <see cref="BiomeType"/>.</summary>
    [JSExport]
    public static byte[] Biomes()
    {
        var b = World.Map.DataStore.GetSpan<LocalMapInfo>();
        var result = new byte[b.Length];
        for (int i = 0; i < b.Length; i++) result[i] = (byte)b[i].Biome;
        return result;
    }

    private static World World => _world ?? throw new InvalidOperationException("Generate first.");
}
