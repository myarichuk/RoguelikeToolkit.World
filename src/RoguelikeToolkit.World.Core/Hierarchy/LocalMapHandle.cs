using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Leaf detail: one region cell expands to a local tile grid.</summary>
public sealed class LocalMapHandle : GridMapHandle<LocalTile>
{
    public int WorldTileIndex { get; }
    public int RegionCellIndex { get; }
    public LocalTile[] Tiles { get; }

    internal LocalMapHandle(
        int worldTileIndex, int regionCellIndex, uint seed, int size, LocalTile[] tiles,
        MapAddress address, MapBounds bounds, ParentContext parent,
        int riverEntryCell, int riverExitCell)
        : base(seed, size, address, bounds, parent, riverEntryCell, riverExitCell)
    {
        WorldTileIndex = worldTileIndex;
        RegionCellIndex = regionCellIndex;
        Tiles = tiles;
    }

    public override LocalTile GetTile(int index) => Tiles[index];

    /// <summary>Geographic center of one local tile (see <see cref="GridMapHandle{TTile}"/>).</summary>
    public GeoCoord TileCenter(int tileIndex) => CenterOf(tileIndex);

    /// <summary>Index of the tile containing a coordinate (nearest center; deterministic).</summary>
    public int TileAt(GeoCoord coord) => IndexAt(coord);

    /// <summary>
    /// Materializes injector sites for this local map. Injectors run ascending
    /// by <see cref="ISiteInjector.Order"/>; duplicate Order values throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public SiteCatalog WithInjectors(IEnumerable<ISiteInjector> injectors)
        => SiteInjectorPipeline.MaterializeLocal(this, injectors);
}

public struct LocalTile
{
    public int Index;
    public float Height;
    public BiomeType Biome;
    public byte Danger;
    public bool IsWater;
    public float Temperature;
    public float Precipitation;
    public float WaterDepth;
    public BiomeType ParentBiome;
}
