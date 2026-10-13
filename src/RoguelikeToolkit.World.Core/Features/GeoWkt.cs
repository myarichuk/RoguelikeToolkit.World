using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoguelikeToolkit.World.Core;

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
