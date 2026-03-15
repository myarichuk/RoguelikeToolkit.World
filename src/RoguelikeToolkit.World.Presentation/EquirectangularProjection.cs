using System;

using RoguelikeToolkit.World.Core;
namespace RoguelikeToolkit.World.Presentation;

public class EquirectangularProjection : IProjection
{
    private readonly double _radius;
    private readonly double _standardParallel;

    public EquirectangularProjection(double radius = 1.0, double standardParallel = 0.0)
    {
        _radius = radius;
        _standardParallel = standardParallel * GeoCoord.Deg2Rad;
    }

    public Vector2D Project(GeoCoord coord)
    {
        double x = _radius * coord.LongitudeRad * Math.Cos(_standardParallel);
        double y = _radius * coord.LatitudeRad;
        return new Vector2D(x, y);
    }

    public GeoCoord Inverse(Vector2D point)
    {
        double lat = point.Y / _radius;
        double lon = point.X / (_radius * Math.Cos(_standardParallel));
        return GeoCoord.FromRadians(lat, lon);
    }
}
