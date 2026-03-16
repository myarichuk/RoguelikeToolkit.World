using System;

namespace RoguelikeToolkit.World.Geometry;

public class GnomonicProjection : IProjection
{
    private readonly double _radius;
    private readonly GeoCoord _center;
    private readonly double _centerLat;
    private readonly double _centerLon;
    private readonly double _sinCenterLat;
    private readonly double _cosCenterLat;

    public GnomonicProjection(double radius = 1.0, GeoCoord? center = null)
    {
        _radius = radius;
        _center = center ?? new GeoCoord(0, 0);
        _centerLat = _center.LatitudeRad;
        _centerLon = _center.LongitudeRad;
        _sinCenterLat = Math.Sin(_centerLat);
        _cosCenterLat = Math.Cos(_centerLat);
    }

    public Vector2D Project(GeoCoord coord)
    {
        double lat = coord.LatitudeRad;
        double lon = coord.LongitudeRad;

        double sinLat = Math.Sin(lat);
        double cosLat = Math.Cos(lat);
        double dLon = lon - _centerLon;
        double cosDLon = Math.Cos(dLon);

        double cosC = _sinCenterLat * sinLat + _cosCenterLat * cosLat * cosDLon;

        // Prevent projection of points on the back half of the sphere
        if (cosC <= 0)
        {
            return new Vector2D(double.NaN, double.NaN);
        }

        double x = _radius * (cosLat * Math.Sin(dLon)) / cosC;
        double y = _radius * (_cosCenterLat * sinLat - _sinCenterLat * cosLat * cosDLon) / cosC;

        return new Vector2D(x, y);
    }

    public GeoCoord Inverse(Vector2D point)
    {
        double x = point.X / _radius;
        double y = point.Y / _radius;
        double rho = Math.Sqrt(x * x + y * y);

        if (rho == 0) return _center;

        double c = Math.Atan(rho);
        double sinC = Math.Sin(c);
        double cosC = Math.Cos(c);

        double lat = Math.Asin(cosC * _sinCenterLat + (y * sinC * _cosCenterLat) / rho);
        double lon = _centerLon + Math.Atan2(x * sinC, rho * _cosCenterLat * cosC - y * _sinCenterLat * sinC);

        return GeoCoord.FromRadians(lat, lon);
    }
}
