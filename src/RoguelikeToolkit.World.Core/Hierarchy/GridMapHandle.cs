using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Hierarchy;

/// <summary>
/// State and hex-grid behavior shared by the region and local handles: both are
/// size x size axial patches with an address, bounds, the parent context they
/// were derived from, and the threaded river's border cells.
/// </summary>
public abstract class GridMapHandle<TTile> : IHexMap<TTile> where TTile : struct
{
    /// <summary>Side length of the axial patch; the map holds <c>Size * Size</c> cells.</summary>
    public int Size { get; }
    /// <summary>Address of this map in the hierarchy (region or local level).</summary>
    public MapAddress Address { get; }
    /// <summary>Geographic extent of the map: center, bounding radius in km, and the border-ring cell indices in this map's own indexing.</summary>
    public MapBounds Bounds { get; }
    /// <summary>Summary of the parent tile or cell this map was derived from; carries the scalars and orientation the child inherited.</summary>
    public ParentContext Parent { get; }
    /// <summary>Seed this map was derived with. The same address, parent context and seed reproduce identical cells.</summary>
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

    /// <summary>Number of cells, <c>Size * Size</c>.</summary>
    public int TileCount => Size * Size;
    /// <summary>Same as <see cref="Address"/>; satisfies <see cref="IHexMap{TTile}"/>.</summary>
    public MapAddress RootAddress => Address;
    /// <summary>Returns the cell at a row-major index (<c>r * Size + q</c>).</summary>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside <c>[0, TileCount)</c>.</exception>
    public abstract TTile GetTile(int index);
    /// <summary>
    /// Writes the hex neighbors of a cell into <paramref name="neighbors"/> and returns how many were written.
    /// Neighbors are clipped to the patch, so border cells have fewer than six; a span shorter than six silently truncates the result.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside <c>[0, TileCount)</c>.</exception>
    public int GetAdjacent(int index, Span<int> neighbors) => GridProjection.GetHexAdjacent(index, Size, neighbors);
    /// <summary>Row-major index of axial (<paramref name="q"/>, <paramref name="r"/>), or -1 when it lies outside the patch.</summary>
    public int GetTileAt(int q, int r) => OffsetGrid.ToIndex(q, r, Size);
    /// <summary>Row-major index of a cube coordinate, or -1 when the coordinate is invalid or outside the patch.</summary>
    public int GetTileAt(CubeCoord cube) => !cube.IsValid ? -1 : GetTileAt(cube.ToAxial().Q, cube.ToAxial().R);

    /// <summary>Geographic center of one cell: the CellUV offset projected onto the tangent plane at Bounds.Center and normalized back to the sphere — the same projection derivation uses.</summary>
    protected GeoCoord CenterOf(int index)
        => GridProjection.GridCellCenter(Bounds.Center, Bounds.RadiusKm, Size, index);

    /// <summary>Index of the cell containing a coordinate (nearest center; deterministic).</summary>
    protected int IndexAt(GeoCoord coord)
        => GridProjection.GridCellAt(Bounds.Center, Bounds.RadiusKm, Size, coord);
}
