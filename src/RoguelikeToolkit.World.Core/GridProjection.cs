using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Local tangent frame at a map center: the up (radial) vector, the east/north
/// basis, and the angular half-extent of the map. Offsets in the normalized
/// (u, v) plane are projected onto the tangent plane and normalized back to the
/// sphere, which is the one projection every child-map derivation uses.
/// </summary>
internal readonly struct TangentFrame
{
    public Vector3D Up { get; }
    public Vector3D East { get; }
    public Vector3D North { get; }
    /// <summary>Angular radius (radians) of the map, floored at <c>minAngle</c>.</summary>
    public double Angle { get; }

    public TangentFrame(GeoCoord center, double radiusKm, double minAngle)
    {
        Up = Vector3D.FromGeoCoord(center);
        (East, North) = GridProjection.EastNorth(center, Up);
        Angle = Math.Max(radiusKm / World.EarthRadiusKm, minAngle);
    }

    /// <summary>Unit vector of the point at normalized offset (u, v).</summary>
    public Vector3D Offset(double u, double v)
        => (Up + East * (u * Angle) + North * (v * Angle)).Normalize();
}

/// <summary>
/// Flat-top hex-grid geometry of the square axial patches that back region and
/// local maps: normalized cell coordinates, adjacency, and the gnomonic mapping
/// between cell indices and geographic coordinates.
/// </summary>
internal static class GridProjection
{
    public const double Sqrt3 = 1.7320508075688772;

    /// <summary>Normalized flat-top hex coords (u, v in [-1, 1]) for axial (q, r).</summary>
    public static (double U, double V) CellUV(int q, int r, int size)
    {
        if (size <= 1) return (0.0, 0.0);
        double x = 1.5 * q;
        double y = Sqrt3 * (r + q * 0.5);
        double half = 0.75 * (size - 1);
        double cx = half;
        double cy = Sqrt3 * half;
        return ((x - cx) / half, (y - cy) / (Sqrt3 * half));
    }

    /// <summary>Hex neighbors of a cell on a size x size axial patch (clipped to the patch).</summary>
    public static int GetHexAdjacent(int index, int size, Span<int> neighbors)
    {
        var (q, r) = OffsetGrid.FromIndex(index, size);
        int count = 0;
        // Axial hex neighborhood on the square patch.
        if (q + 1 < size && count < neighbors.Length) neighbors[count++] = index + 1;
        if (q - 1 >= 0 && count < neighbors.Length) neighbors[count++] = index - 1;
        if (r + 1 < size && count < neighbors.Length) neighbors[count++] = index + size;
        if (r - 1 >= 0 && count < neighbors.Length) neighbors[count++] = index - size;
        if (q + 1 < size && r - 1 >= 0 && count < neighbors.Length) neighbors[count++] = index + 1 - size;
        if (q - 1 >= 0 && r + 1 < size && count < neighbors.Length) neighbors[count++] = index - 1 + size;
        return count;
    }

    public static int HexDistance(int a, int b, int size)
    {
        var (aq, ar) = OffsetGrid.FromIndex(a, size);
        var (bq, br) = OffsetGrid.FromIndex(b, size);
        int dq = aq - bq, dr = ar - br;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    public static (Vector3D East, Vector3D North) EastNorth(GeoCoord center, Vector3D up)
    {
        double lonR = center.Longitude * GeoCoord.Deg2Rad;
        var east = new Vector3D(-DetMath.Sin(lonR), DetMath.Cos(lonR), 0);
        if (east.Length < 1e-9) east = new Vector3D(1, 0, 0);
        east = east.Normalize();
        var north = Vector3D.Cross(up, east).Normalize();
        return (east, north);
    }

    public static GeoCoord OffsetToGeo(GeoCoord center, double radiusKm, double u, double v)
        => new TangentFrame(center, radiusKm, 1e-9).Offset(u, v).ToGeoCoord();

    public static GeoCoord GridCellCenter(GeoCoord mapCenter, double radiusKm, int size, int index)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if ((uint)index >= (uint)(size * size)) throw new IndexOutOfRangeException();
        var (q, r) = OffsetGrid.FromIndex(index, size);
        var (u, v) = CellUV(q, r, size);
        return OffsetToGeo(mapCenter, radiusKm, u, v);
    }

    public static int GridCellAt(GeoCoord mapCenter, double radiusKm, int size, GeoCoord coord)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var target = Vector3D.FromGeoCoord(coord);

        // Fast path: invert the gnomonic projection analytically to get the
        // fractional (q, r), then take the exact nearest of a small window
        // around it. The window is exact for realistic region spans; wide maps
        // (strong projection distortion), far-off/back-hemisphere targets and
        // tiny grids take the exhaustive scan.
        var frame = new TangentFrame(mapCenter, radiusKm, 1e-9);
        if (size > 3 && frame.Angle < 0.35)
        {
            double depth = Vector3D.Dot(target, frame.Up);
            if (depth > 0.5)
            {
                double u = Vector3D.Dot(target, frame.East) / depth / frame.Angle;
                double v = Vector3D.Dot(target, frame.North) / depth / frame.Angle;
                double half = 0.75 * (size - 1);
                double qf = (u * half + half) / 1.5;
                double rf = half * (v + 1.0) - qf * 0.5;
                if (qf > -2 && qf < size + 1 && rf > -2 && rf < size + 1)
                {
                    int q0 = (int)Math.Floor(qf), r0 = (int)Math.Floor(rf);
                    int best = -1;
                    double bestDot = double.NegativeInfinity;
                    // Row-major ascending so ties resolve to the lowest index, as in the full scan.
                    for (int r = Math.Max(0, r0 - 2); r <= Math.Min(size - 1, r0 + 3); r++)
                        for (int q = Math.Max(0, q0 - 2); q <= Math.Min(size - 1, q0 + 3); q++)
                        {
                            int i = r * size + q;
                            double dot = Vector3D.Dot(target, Vector3D.FromGeoCoord(GridCellCenter(mapCenter, radiusKm, size, i)));
                            if (dot > bestDot) { bestDot = dot; best = i; }
                        }
                    if (best >= 0) return best;
                }
            }
        }
        return GridCellAtExact(mapCenter, radiusKm, size, coord);
    }

    public static int GridCellAtExact(GeoCoord mapCenter, double radiusKm, int size, GeoCoord coord)
    {
        var target = Vector3D.FromGeoCoord(coord);
        int best = 0;
        double bestDot = double.NegativeInfinity;
        for (int i = 0; i < size * size; i++)
        {
            double dot = Vector3D.Dot(target, Vector3D.FromGeoCoord(GridCellCenter(mapCenter, radiusKm, size, i)));
            if (dot > bestDot) { bestDot = dot; best = i; }
        }
        return best;
    }
}
