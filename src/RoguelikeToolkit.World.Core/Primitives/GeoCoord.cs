namespace RoguelikeToolkit.World.Core.Primitives;

public readonly record struct GeoCoord(double Latitude, double Longitude)
{
    public const double Deg2Rad = Math.PI / 180.0;
    public const double Rad2Deg = 180.0 / Math.PI;

    public double LatitudeRad => Latitude * Deg2Rad;
    public double LongitudeRad => Longitude * Deg2Rad;

    public static GeoCoord FromRadians(double latRad, double lonRad)
    {
        return new GeoCoord(latRad * Rad2Deg, lonRad * Rad2Deg);
    }
}
