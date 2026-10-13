using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoguelikeToolkit.World.Core;

/// <summary>Water body kinds for game queries (lake vs sea vs ocean basin).</summary>
public enum WaterBodyKind : byte
{
    Lake = 0,
    Sea = 1,
    Ocean = 2
}

/// <summary>Sparse feature: one connected water region (tiles + boundary loop).</summary>
public sealed class WaterBody
{
    public int Id { get; }
    public WaterBodyKind Kind { get; }
    public List<int> Tiles { get; } = new();
    public List<int> Boundary { get; } = new();

    public WaterBody(int id, WaterBodyKind kind)
    {
        Id = id;
        Kind = kind;
    }
}

/// <summary>Where a river's water ends up.</summary>
public enum RiverTerminal : byte
{
    /// <summary>Reaches the sea (exorheic).</summary>
    Sea = 0,
    /// <summary>Ends in a lake (open or endorheic).</summary>
    Lake = 1,
    /// <summary>Dies inland: desert sink or playa (endorheic).</summary>
    Sink = 2
}

/// <summary>
/// Sparse feature: one river as an ordered downstream tile chain. The path's
/// last tile is the terminal water tile (sea/lake) or the final land tile for
/// sink rivers.
/// </summary>
public sealed class River
{
    public int Id { get; }
    public List<int> Path { get; } = new();
    public RiverTerminal Terminal { get; set; } = RiverTerminal.Sea;
    /// <summary>Water-body id of the terminal sea/lake; -1 for sinks.</summary>
    public int TerminalBody { get; set; } = -1;
    /// <summary>Discharge at the mouth (last river tile).</summary>
    public float MouthFlow { get; set; }

    public River(int id) => Id = id;
}

/// <summary>Sparse feature: a labeled tile segment (ranges, valleys, canyons, playas).</summary>
public enum RangeKind : byte
{
    MountainRange = 0,
    Valley = 1,
    /// <summary>Arid slot-gorge reach: a powerful river cutting relief.</summary>
    Canyon = 2,
    /// <summary>Dry basin floor: salt flat / playa.</summary>
    Playa = 3
}

public sealed class RangeFeature
{
    public int Id { get; }
    public RangeKind Kind { get; }
    public List<int> Tiles { get; } = new();

    public RangeFeature(int id, RangeKind kind)
    {
        Id = id;
        Kind = kind;
    }
}

/// <summary>
/// Managed catalogs for sparse features. Field layers stay in
/// <see cref="WorldDataStore"/>; these catalogs hold tile-id lists plus
/// WKT export. Not part of the memory-mapped store.
/// </summary>
public sealed class RiverCatalog
{
    public List<River> Rivers { get; } = new();
}

/// <summary>Managed catalog of connected water bodies (lakes, seas, ocean).</summary>
public sealed class WaterBodyCatalog
{
    public List<WaterBody> Bodies { get; } = new();

    public WaterBody? FindByTile(int tileIndex, HydrologyInfo[]? fields = null)
    {
        foreach (var b in Bodies)
        {
            if (b.Tiles.Contains(tileIndex)) return b;
        }
        return null;
    }
}

/// <summary>Managed catalog of mountain ranges and valleys.</summary>
public sealed class RangeCatalog
{
    public List<RangeFeature> Features { get; } = new();
}

/// <summary>WKT export for feature geometry. Internal storage is tile-id
/// lists; WKT is produced on demand for interop (no geometry dependency).</summary>
public static class GeoWkt
{
    private static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

    public static string RiverToWkt(River river, WorldDataStore store)
    {
        var sb = new StringBuilder("LINESTRING(");
        for (int i = 0; i < river.Path.Count; i++)
        {
            var c = store.GetGeoCoord(river.Path[i]);
            if (i > 0) sb.Append(", ");
            // WKT order is lon lat.
            sb.Append(F(c.Longitude)).Append(' ').Append(F(c.Latitude));
        }
        sb.Append(')');
        return sb.ToString();
    }

    public static string WaterBodyToWkt(WaterBody body, WorldDataStore store)
    {
        var ring = body.Boundary.Count > 0 ? body.Boundary : body.Tiles;
        var sb = new StringBuilder("POLYGON((");
        for (int i = 0; i < ring.Count; i++)
        {
            var c = store.GetGeoCoord(ring[i]);
            if (i > 0) sb.Append(", ");
            sb.Append(F(c.Longitude)).Append(' ').Append(F(c.Latitude));
        }
        if (ring.Count > 0)
        {
            var c0 = store.GetGeoCoord(ring[0]);
            sb.Append(", ").Append(F(c0.Longitude)).Append(' ').Append(F(c0.Latitude));
        }
        sb.Append("))");
        return sb.ToString();
    }

    public static string DepositToWkt(Deposit deposit, WorldDataStore store)
    {
        var c = store.GetGeoCoord(deposit.TileIndex);
        return $"POINT({F(c.Longitude)} {F(c.Latitude)})";
    }

    public static string RangeToWkt(RangeFeature range, WorldDataStore store)
    {
        var sb = new StringBuilder("LINESTRING(");
        for (int i = 0; i < range.Tiles.Count; i++)
        {
            var c = store.GetGeoCoord(range.Tiles[i]);
            if (i > 0) sb.Append(", ");
            sb.Append(F(c.Longitude)).Append(' ').Append(F(c.Latitude));
        }
        sb.Append(')');
        return sb.ToString();
    }
}
