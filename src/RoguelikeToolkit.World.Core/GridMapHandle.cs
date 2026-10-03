using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// State and hex-grid behavior shared by the region and local handles: both are
/// size x size axial patches with an address, bounds, the parent context they
/// were derived from, and the threaded river's border cells.
/// </summary>
public abstract class GridMapHandle<TTile> : IHexMap<TTile> where TTile : struct
{
    public int Size { get; }
    public MapAddress Address { get; }
    public MapBounds Bounds { get; }
    public ParentContext Parent { get; }
    public uint Seed { get; }
    /// <summary>Entry border cell of the threaded river, or -1 when there is none.</summary>
    public int RiverEntryCell { get; }
    /// <summary>Exit border cell of the threaded river, or -1 when there is none.</summary>
    public int RiverExitCell { get; }
    /// <summary>Whether a parent river was threaded through this map.</summary>
    public bool HasRiver => RiverEntryCell >= 0;

    private protected GridMapHandle(
        uint seed, int size, MapAddress address, MapBounds bounds, ParentContext parent,
        int riverEntryCell, int riverExitCell)
    {
        Seed = seed;
        Size = size;
        Address = address;
        Bounds = bounds;
        Parent = parent;
        RiverEntryCell = riverEntryCell;
        RiverExitCell = riverExitCell;
    }

    public int TileCount => Size * Size;
    public MapAddress RootAddress => Address;
    public abstract TTile GetTile(int index);
    public int GetAdjacent(int index, Span<int> neighbors) => GridProjection.GetHexAdjacent(index, Size, neighbors);
    public int GetTileAt(int q, int r) => OffsetGrid.ToIndex(q, r, Size);
    public int GetTileAt(CubeCoord cube) => !cube.IsValid ? -1 : GetTileAt(cube.ToAxial().Q, cube.ToAxial().R);

    /// <summary>Geographic center of one cell: the CellUV offset projected onto the tangent plane at Bounds.Center and normalized back to the sphere — the same projection derivation uses.</summary>
    protected GeoCoord CenterOf(int index)
        => GridProjection.GridCellCenter(Bounds.Center, Bounds.RadiusKm, Size, index);

    /// <summary>Index of the cell containing a coordinate (nearest center; deterministic).</summary>
    protected int IndexAt(GeoCoord coord)
        => GridProjection.GridCellAt(Bounds.Center, Bounds.RadiusKm, Size, coord);
}
