namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>
/// Generic hex-map view: planet tiles, region cells, or local tiles.
/// Indexing is the map's own flat tile numbering.
/// </summary>
/// <typeparam name="TTile">Tile payload (a struct: LocalTile, RegionCell, ...).</typeparam>
public interface IHexMap<TTile> where TTile : struct
{
    /// <summary>The map's size parameter; for the region and local grid maps, the side of the square patch, so <see cref="TileCount"/> is <c>Size * Size</c>.</summary>
    int Size { get; }
    /// <summary>Number of tiles; valid flat indices are <c>0..TileCount-1</c>.</summary>
    int TileCount { get; }
    /// <summary>Address of this map in the planet, region, local hierarchy.</summary>
    MapAddress RootAddress { get; }
    /// <summary>Geographic extent of the map and its border tiles.</summary>
    MapBounds Bounds { get; }

    /// <summary>The tile at a flat index, which must be in <c>0..TileCount-1</c>.</summary>
    TTile GetTile(int index);

    /// <summary>Writes neighbor indices into <paramref name="neighbors"/>; returns the count.</summary>
    int GetAdjacent(int index, Span<int> neighbors);

    /// <summary>Flat index for axial (q, r), or -1 when outside the map.</summary>
    int GetTileAt(int q, int r);

    /// <summary>Flat index for cube coordinates, or -1 when outside/invalid.</summary>
    int GetTileAt(CubeCoord cube);
}
