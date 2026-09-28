namespace RoguelikeToolkit.World.Core;

/// <summary>Cube coordinates for a hex (x + y + z == 0); axial q = x, r = z.</summary>
public readonly record struct CubeCoord(int X, int Y, int Z)
{
    public bool IsValid => X + Y + Z == 0;

    public (int Q, int R) ToAxial() => (X, Z);

    public static CubeCoord FromAxial(int q, int r) => new(q, -q - r, r);
}

/// <summary>Offset-grid helpers for square (size x size) region/local maps in row-major order.</summary>
public static class OffsetGrid
{
    /// <summary>Row-major index for axial (q, r), or -1 when outside the grid.</summary>
    public static int ToIndex(int q, int r, int size)
        => (uint)q >= (uint)size || (uint)r >= (uint)size ? -1 : r * size + q;

    public static (int Q, int R) FromIndex(int index, int size) => (index % size, index / size);
}

/// <summary>
/// Generic hex-map view: planet tiles, region cells, or local tiles.
/// Indexing is the map's own flat tile numbering.
/// </summary>
/// <typeparam name="TTile">Tile payload (a struct: LocalTile, RegionCell, ...).</typeparam>
public interface IHexMap<TTile> where TTile : struct
{
    int Size { get; }
    int TileCount { get; }
    MapAddress RootAddress { get; }
    MapBounds Bounds { get; }

    TTile GetTile(int index);

    /// <summary>Writes neighbor indices into <paramref name="neighbors"/>; returns the count.</summary>
    int GetAdjacent(int index, Span<int> neighbors);

    /// <summary>Flat index for axial (q, r), or -1 when outside the map.</summary>
    int GetTileAt(int q, int r);

    /// <summary>Flat index for cube coordinates, or -1 when outside/invalid.</summary>
    int GetTileAt(CubeCoord cube);
}
