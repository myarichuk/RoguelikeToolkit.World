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
    /// <remarks>Coordinates are <c>lon lat</c> in degrees, fixed to six decimals with the invariant culture. An empty path yields <c>LINESTRING EMPTY</c>; a single-tile path yields a <c>POINT</c>, since a one-vertex line is not valid WKT.</remarks>
    public static string RiverToWkt(River river, WorldDataStore store)
        => Line(PathPoints(river.Path, store));

    /// <summary>Exports <paramref name="body"/> as a WKT <c>POLYGON</c> whose ring runs through the tile centres of <see cref="WaterBody.Boundary"/> (or of <see cref="WaterBody.Tiles"/> when the boundary is empty), closed by repeating the first vertex.</summary>
    /// <remarks>
    /// The stored tile lists are in tile-index order, so the vertices are re-ordered by angle around their centroid (in a local lon/lat plane unwrapped across the antimeridian,
    /// ties broken by tile index) to form a star-shaped ring; a concave shoreline is approximated, not traced exactly. An empty body yields <c>POLYGON EMPTY</c>, and a body with
    /// fewer than three tiles yields a <c>POINT</c>/<c>MULTIPOINT</c> since no valid ring exists. Coordinates are <c>lon lat</c> in degrees, six decimals, invariant culture.
    /// </remarks>
    public static string WaterBodyToWkt(WaterBody body, WorldDataStore store)
    {
        var ring = body.Boundary.Count > 0 ? body.Boundary : body.Tiles;
        if (ring.Count == 0) return "POLYGON EMPTY";
        var pts = Unwrapped(ring, store);
        if (pts.Count < 3) return MultiPoint(pts);

        double cx = 0, cy = 0;
        foreach (var p in pts) { cx += p.X; cy += p.Y; }
        cx /= pts.Count; cy /= pts.Count;
        double k = Math.Cos(cy * Math.PI / 180.0);
        pts.Sort((a, b) =>
        {
            int c = Math.Atan2(a.Y - cy, (a.X - cx) * k).CompareTo(Math.Atan2(b.Y - cy, (b.X - cx) * k));
            return c != 0 ? c : a.Tile.CompareTo(b.Tile);
        });

        var sb = new StringBuilder("POLYGON((");
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            Append(sb, pts[i]);
        }
        sb.Append(", ");
        Append(sb, pts[0]);
        sb.Append("))");
        return sb.ToString();
    }

    /// <summary>Exports <paramref name="deposit"/> as a WKT <c>POINT</c> at the centre of its tile (<c>lon lat</c> in degrees, six decimals, invariant culture).</summary>
    public static string DepositToWkt(Deposit deposit, WorldDataStore store)
    {
        var c = store.GetGeoCoord(deposit.TileIndex);
        return $"POINT({F(c.Longitude)} {F(c.Latitude)})";
    }

    /// <summary>Exports <paramref name="range"/> as a WKT <c>LINESTRING</c> through its tile centres.</summary>
    /// <remarks>
    /// The stored tiles are a flood-fill component in discovery order, so the vertices are re-ordered by their projection onto the component's principal axis
    /// (local lon/lat plane unwrapped across the antimeridian, ties broken by tile index), which approximates a centreline. An empty feature yields <c>LINESTRING EMPTY</c>
    /// and a one-tile feature a <c>POINT</c>. Coordinates are <c>lon lat</c> in degrees, six decimals, invariant culture.
    /// </remarks>
    public static string RangeToWkt(RangeFeature range, WorldDataStore store)
    {
        var pts = Unwrapped(range.Tiles, store);
        if (pts.Count > 2)
        {
            double mx = 0, my = 0;
            foreach (var p in pts) { mx += p.X; my += p.Y; }
            mx /= pts.Count; my /= pts.Count;
            double k = Math.Cos(my * Math.PI / 180.0);
            double sxx = 0, syy = 0, sxy = 0;
            foreach (var p in pts)
            {
                double dx = (p.X - mx) * k, dy = p.Y - my;
                sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
            }
            // Dominant eigenvector of the 2x2 covariance matrix, in closed form.
            double lambda = (sxx + syy) / 2 + Math.Sqrt((sxx - syy) * (sxx - syy) / 4 + sxy * sxy);
            double ax, ay;
            if (sxy != 0) { ax = sxy; ay = lambda - sxx; }
            else if (sxx >= syy) { ax = 1; ay = 0; }
            else { ax = 0; ay = 1; }
            pts.Sort((a, b) =>
            {
                double ta = (a.X - mx) * k * ax + (a.Y - my) * ay;
                double tb = (b.X - mx) * k * ax + (b.Y - my) * ay;
                int c = ta.CompareTo(tb);
                return c != 0 ? c : a.Tile.CompareTo(b.Tile);
            });
        }
        return Line(pts);
    }

    private readonly record struct Pt(int Tile, double X, double Y);

    private static List<Pt> PathPoints(IReadOnlyList<int> tiles, WorldDataStore store)
    {
        var list = new List<Pt>(tiles.Count);
        foreach (int t in tiles)
        {
            var c = store.GetGeoCoord(t);
            list.Add(new Pt(t, c.Longitude, c.Latitude));
        }
        return list;
    }

    // Longitudes are shifted into [-180, 180) of the first vertex so a shape crossing the antimeridian stays contiguous.
    private static List<Pt> Unwrapped(IReadOnlyList<int> tiles, WorldDataStore store)
    {
        var list = PathPoints(tiles, store);
        if (list.Count == 0) return list;
        double lon0 = list[0].X;
        for (int i = 1; i < list.Count; i++)
        {
            double d = list[i].X - lon0;
            d -= 360.0 * Math.Floor((d + 180.0) / 360.0);
            list[i] = list[i] with { X = lon0 + d };
        }
        return list;
    }

    private static void Append(StringBuilder sb, Pt p) => sb.Append(F(p.X)).Append(' ').Append(F(p.Y));

    private static string Line(List<Pt> pts)
    {
        if (pts.Count == 0) return "LINESTRING EMPTY";
        if (pts.Count == 1) return "POINT(" + F(pts[0].X) + " " + F(pts[0].Y) + ")";
        var sb = new StringBuilder("LINESTRING(");
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            Append(sb, pts[i]);
        }
        return sb.Append(')').ToString();
    }

    private static string MultiPoint(List<Pt> pts)
    {
        if (pts.Count == 1) return "POINT(" + F(pts[0].X) + " " + F(pts[0].Y) + ")";
        var sb = new StringBuilder("MULTIPOINT(");
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('(');
            Append(sb, pts[i]);
            sb.Append(')');
        }
        return sb.Append(')').ToString();
    }
}
