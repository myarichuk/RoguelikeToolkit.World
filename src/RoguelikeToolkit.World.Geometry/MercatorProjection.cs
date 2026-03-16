using System;

namespace RoguelikeToolkit.World.Geometry;

public class MercatorProjection : IProjection
{
    private readonly double _radius;
    public MercatorProjection(double radius = 1.0) => _radius = radius;

    public Vector2D Project(GeoCoord coord)
    {
        double lat = coord.LatitudeRad;
        double lon = coord.LongitudeRad;

        // Handle limits
        if (lat > 89.5 * GeoCoord.Deg2Rad) lat = 89.5 * GeoCoord.Deg2Rad;
        if (lat < -89.5 * GeoCoord.Deg2Rad) lat = -89.5 * GeoCoord.Deg2Rad;

        double x = _radius * lon;
        double y = _radius * Math.Log(Math.Tan(Math.PI / 4.0 + lat / 2.0));
        return new Vector2D(x, y);
    }

    public GeoCoord Inverse(Vector2D point)
    {
        double lon = point.X / _radius;
        double lat = 2.0 * Math.Atan(Math.Exp(point.Y / _radius)) - Math.PI / 2.0;
        return GeoCoord.FromRadians(lat, lon);
    }
}
