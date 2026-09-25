namespace RoguelikeToolkit.World.Core;

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

public readonly record struct Vector2D(double X, double Y);

public readonly record struct Vector3D(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3D Normalize()
    {
        var len = Length;
        return len > 0 ? new Vector3D(X / len, Y / len, Z / len) : this;
    }

    public static Vector3D Cross(Vector3D a, Vector3D b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X
    );

    public static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3D operator *(Vector3D a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vector3D operator /(Vector3D a, double b) => new(a.X / b, a.Y / b, a.Z / b);

    public GeoCoord ToGeoCoord()
    {
        var n = Normalize();
        return GeoCoord.FromRadians(Math.Asin(n.Z), Math.Atan2(n.Y, n.X));
    }

    public static Vector3D FromGeoCoord(GeoCoord geo)
    {
        var lat = geo.LatitudeRad;
        var lon = geo.LongitudeRad;
        var cosLat = Math.Cos(lat);
        return new Vector3D(
            cosLat * Math.Cos(lon),
            cosLat * Math.Sin(lon),
            Math.Sin(lat)
        );
    }
}

