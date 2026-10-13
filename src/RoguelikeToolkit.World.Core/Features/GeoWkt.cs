using System.Globalization;
using System.Text;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Features;

/// <summary>WKT export for feature geometry. Internal storage is tile-id
/// lists; WKT is produced on demand for interop (no geometry dependency).</summary>
public static class GeoWkt
{
    private static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

    /// <summary>Exports <paramref name="river"/>'s path as a WKT <c>LINESTRING</c> through the tile centres, in path order (head to mouth).</summary>
    /// <remarks>Coordinates are <c>lon lat</c> in degrees, fixed to six decimals with the invariant culture. An empty path yields <c>LINESTRING()</c>, which is not valid WKT.</remarks>
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

    /// <summary>Exports <paramref name="body"/> as a WKT <c>POLYGON</c> whose ring runs through the tile centres of <see cref="WaterBody.Boundary"/> (or of <see cref="WaterBody.Tiles"/> when the boundary is empty), closed by repeating the first vertex.</summary>
    /// <remarks>The ring follows list order. Boundary and tile lists are in ascending tile-index order, not traced around the shore, so the result is a vertex set that is generally not a simple polygon outline. An empty body yields <c>POLYGON(())</c>. Coordinates are <c>lon lat</c> in degrees, six decimals, invariant culture.</remarks>
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

    /// <summary>Exports <paramref name="deposit"/> as a WKT <c>POINT</c> at the centre of its tile (<c>lon lat</c> in degrees, six decimals, invariant culture).</summary>
    public static string DepositToWkt(Deposit deposit, WorldDataStore store)
    {
        var c = store.GetGeoCoord(deposit.TileIndex);
        return $"POINT({F(c.Longitude)} {F(c.Latitude)})";
    }

    /// <summary>Exports <paramref name="range"/> as a WKT <c>LINESTRING</c> through its tile centres in <see cref="RangeFeature.Tiles"/> order.</summary>
    /// <remarks>The tiles are a flood-fill component in discovery order, not a traced centreline, so the line zig-zags through the feature rather than following its axis. An empty feature yields <c>LINESTRING()</c>, which is not valid WKT. Coordinates are <c>lon lat</c> in degrees, six decimals, invariant culture.</remarks>
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
