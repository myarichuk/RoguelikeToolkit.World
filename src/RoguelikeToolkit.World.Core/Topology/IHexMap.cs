namespace RoguelikeToolkit.World.Core.Topology;

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
