using System;
using System.Collections.Generic;
using System.Numerics;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Pure flat-top hex layout for square (size x size) region/local grids:
/// unit-hex pixel coordinates (circumradius 1, y-up), centered on the patch.
/// Same axial orientation as <see cref="RegionMaps.GetHexAdjacent"/>; kept
/// free of UI types so it is unit-testable. The visualizer flips y when
/// drawing (screen space is y-down).
/// </summary>
public static class FlatHexLayout
{
    private const double Sqrt3 = 1.7320508075688772;

    /// <summary>Raw (uncentered) pixel center of axial (q, r) with unit hexes.</summary>
    public static Vector2D RawCenter(int q, int r)
        => new(1.5 * q, Sqrt3 * (r + q * 0.5));

    /// <summary>Centering translation subtracted by <see cref="CellPos"/>.</summary>
    public static Vector2D Origin(int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        double half = 0.75 * (size - 1);
        return new Vector2D(half, Sqrt3 * half);
    }

    /// <summary>Centered layout position of a flat cell index.</summary>
    public static Vector2D CellPos(int index, int size)
    {
        if ((uint)index >= (uint)(size * size)) throw new IndexOutOfRangeException();
        var (q, r) = OffsetGrid.FromIndex(index, size);
        var raw = RawCenter(q, r);
        var origin = Origin(size);
        return new Vector2D(raw.X - origin.X, raw.Y - origin.Y);
    }

    /// <summary>Six flat-top hex vertices (angles 0, 60, ...) around a cell center.</summary>
    public static Vector2D[] Corners(int index, int size)
    {
        var c = CellPos(index, size);
        var corners = new Vector2D[6];
        for (int k = 0; k < 6; k++)
        {
            double a = k * Math.PI / 3.0;
            corners[k] = new Vector2D(c.X + Math.Cos(a), c.Y + Math.Sin(a));
        }
        return corners;
    }

    /// <summary>Index of the cell whose center is nearest (x, y); lowest index wins ties.</summary>
    public static int HitTest(int size, double x, double y)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        int best = 0;
        double bestD = double.PositiveInfinity;
        for (int i = 0; i < size * size; i++)
        {
            var c = CellPos(i, size);
            double dx = c.X - x, dy = c.Y - y;
            double d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Full patch extent (width, height) including hex overhang, for view fitting.</summary>
    public static (double Width, double Height) Extent(int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        return (1.5 * (size - 1) + 2.0, Sqrt3 * 1.5 * (size - 1) + Sqrt3);
    }
}

/// <summary>One cell of a flat plain-map view: layout, B1 coordinate, planet-matching fill.</summary>
public sealed class PlainMapCell
{
    public int Index { get; }
    public Vector2D Layout { get; }
    public Vector2D[] Corners { get; }
    public GeoCoord Geo { get; }
    public Vector3 Fill { get; }
    public string Label { get; }
    public string Detail { get; }

    public PlainMapCell(int index, Vector2D layout, Vector2D[] corners, GeoCoord geo, Vector3 fill, string label, string detail)
    {
        Index = index;
        Layout = layout;
        Corners = corners;
        Geo = geo;
        Fill = fill;
        Label = label;
        Detail = detail;
    }
}

/// <summary>
/// Flat "plain map" of one derived map: per-cell layout + B1 coordinates +
/// fills from <see cref="BiomePalette"/> (the same call the planet Biome
/// color mode uses, so colors match the planet). Backs the visualizer
/// region/local drill-down (backlog B2); navigation state lives in the view.
/// </summary>
public sealed class PlainMap
{
    public string Title { get; }
    public int Size { get; }
    public IReadOnlyList<PlainMapCell> Cells { get; }

    private PlainMap(string title, int size, IReadOnlyList<PlainMapCell> cells)
    {
        Title = title;
        Size = size;
        Cells = cells;
    }

    public static PlainMap FromRegion(RegionHandle region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var cells = new PlainMapCell[region.TileCount];
        for (int i = 0; i < cells.Length; i++)
        {
            var c = region.Cells[i];
            string detail = $"elev {c.Elevation:F2} moist {c.Moisture:F2}";
            if (c.IsRiver) detail += " river";
            cells[i] = new PlainMapCell(
                i,
                FlatHexLayout.CellPos(i, region.Size),
                FlatHexLayout.Corners(i, region.Size),
                region.CellCenter(i),
                BiomePalette.ColorFor(c.Biome),
                $"cell {i} {c.Biome}",
                detail);
        }
        return new PlainMap($"Region of planet tile #{region.WorldTileIndex} ({region.Size}x{region.Size})", region.Size, cells);
    }

    public static PlainMap FromLocal(LocalMapHandle local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var tiles = new PlainMapCell[local.TileCount];
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = local.Tiles[i];
            string detail = $"h {t.Height:F2} temp {t.Temperature:F2} precip {t.Precipitation:F2}";
            if (t.IsWater) detail += " water";
            if (t.WaterDepth >= RegionMaps.LocalRiverMark) detail += " river";
            tiles[i] = new PlainMapCell(
                i,
                FlatHexLayout.CellPos(i, local.Size),
                FlatHexLayout.Corners(i, local.Size),
                local.TileCenter(i),
                BiomePalette.ColorFor(t.Biome),
                $"tile {i} {t.Biome}",
                detail);
        }
        return new PlainMap($"Local of region cell {local.RegionCellIndex} ({local.Size}x{local.Size})", local.Size, tiles);
    }

    /// <summary>Local plain map for one cell of a region (throws <see cref="IndexOutOfRangeException"/> when out of range).</summary>
    public static PlainMap FromLocal(RegionHandle region, int cellIndex)
    {
        ArgumentNullException.ThrowIfNull(region);
        return FromLocal(region.GetLocal(cellIndex));
    }

    /// <summary>Index of the cell nearest layout point (x, y).</summary>
    public int HitTest(double x, double y) => FlatHexLayout.HitTest(Size, x, y);
}
