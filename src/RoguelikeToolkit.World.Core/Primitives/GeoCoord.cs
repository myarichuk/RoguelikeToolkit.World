namespace RoguelikeToolkit.World.Core.Primitives;

/// <summary>A position on the planet's surface in degrees: latitude positive north, longitude positive east. Neither component is wrapped or range-checked here; spatial lookups require finite values.</summary>
/// <param name="Latitude">Degrees north of the equator, nominally [-90, 90].</param>
/// <param name="Longitude">Degrees east of the prime meridian, nominally [-180, 180].</param>
public readonly record struct GeoCoord(double Latitude, double Longitude)
{
    /// <summary>Multiplier converting degrees to radians.</summary>
    public const double Deg2Rad = Math.PI / 180.0;
    /// <summary>Multiplier converting radians to degrees.</summary>
    public const double Rad2Deg = 180.0 / Math.PI;

    /// <summary><see cref="Latitude"/> in radians.</summary>
    public double LatitudeRad => Latitude * Deg2Rad;
    /// <summary><see cref="Longitude"/> in radians.</summary>
    public double LongitudeRad => Longitude * Deg2Rad;

    /// <summary>Builds a coordinate from latitude and longitude given in radians (stored as degrees).</summary>
    public static GeoCoord FromRadians(double latRad, double lonRad)
    {
        return new GeoCoord(latRad * Rad2Deg, lonRad * Rad2Deg);
    }
}
