using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Lazy hierarchical detail: one world hex expands to a region grid.</summary>
public sealed class RegionHandle : GridMapHandle<RegionCell>
{
    public int WorldTileIndex { get; }
    public RegionCell[] Cells { get; }

    internal RegionHandle(
        int worldTileIndex, uint seed, int size, RegionCell[] cells,
        MapAddress address, MapBounds bounds, ParentContext parent,
        int riverEntryCell, int riverExitCell)
        : base(seed, size, address, bounds, parent, riverEntryCell, riverExitCell)
    {
        WorldTileIndex = worldTileIndex;
        Cells = cells;
    }

    public override RegionCell GetTile(int index) => Cells[index];

    /// <summary>Geographic center of one region cell (see <see cref="GridMapHandle{TTile}"/>).</summary>
    public GeoCoord CellCenter(int cellIndex) => CenterOf(cellIndex);

    /// <summary>Index of the cell containing a coordinate (nearest center; deterministic).</summary>
    public int CellAt(GeoCoord coord) => IndexAt(coord);

    public LocalMapHandle GetLocal(int cellIndex, int localSize = RegionMaps.DefaultLocalSize)
    {
        if ((uint)cellIndex >= (uint)Cells.Length) throw new IndexOutOfRangeException();
        return RegionMaps.DeriveLocalMapForCell(this, cellIndex, localSize);
    }

    /// <summary>
    /// Materializes injector sites for this region map. Injectors run ascending
    /// by <see cref="ISiteInjector.Order"/>; duplicate Order values throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public SiteCatalog WithInjectors(IEnumerable<ISiteInjector> injectors)
        => SiteInjectorPipeline.MaterializeRegion(this, injectors);
}

public struct RegionCell
{
    public int CellIndex;
    public uint Seed;
    public float Elevation;
    public BiomeType Biome;
    public float Moisture;
    public bool IsRiver;
    public float WaterDepth;
    /// <summary>Local discharge in cell-count units (1 per cell, routed downhill).</summary>
    public float Flow;
}
