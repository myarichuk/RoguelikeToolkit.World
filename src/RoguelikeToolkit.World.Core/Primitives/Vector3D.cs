namespace RoguelikeToolkit.World.Core.Primitives;

/// <summary>An immutable double-precision 3D vector. For planet positions it is the unit vector from the planet's centre: +Z is the north pole (spin axis), +X is latitude 0 / longitude 0 and +Y is longitude 90 degrees east (see <see cref="FromGeoCoord"/>).</summary>
/// <param name="X">Component along +X.</param>
/// <param name="Y">Component along +Y.</param>
/// <param name="Z">Component along +Z (towards the north pole).</param>
public readonly record struct Vector3D(double X, double Y, double Z)
{
    /// <summary>Euclidean length, <c>sqrt(X*X + Y*Y + Z*Z)</c>.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>This vector scaled to unit length; a vector whose length is not positive (zero, or NaN) is returned unchanged rather than throwing.</summary>
    public Vector3D Normalize()
    {
        var len = Length;
        return len > 0 ? new Vector3D(X / len, Y / len, Z / len) : this;
    }

    /// <summary>Right-handed cross product of <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static Vector3D Cross(Vector3D a, Vector3D b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X
    );

    /// <summary>Dot product; for unit vectors it is the cosine of the angle between them.</summary>
    public static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Component-wise sum.</summary>
    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    /// <summary>Component-wise difference.</summary>
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    /// <summary>Scales every component by <paramref name="b"/>.</summary>
    public static Vector3D operator *(Vector3D a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    /// <summary>Divides every component by <paramref name="b"/>; dividing by zero yields infinities or NaN (IEEE), it does not throw.</summary>
    public static Vector3D operator /(Vector3D a, double b) => new(a.X / b, a.Y / b, a.Z / b);

    /// <summary>The latitude and longitude (degrees) of the direction this vector points in; the length is ignored. Uses <see cref="DetMath"/>, so it is bit-identical across platforms. The zero vector maps to (0, 0).</summary>
    public GeoCoord ToGeoCoord()
    {
        var n = Normalize();
        return GeoCoord.FromRadians(DetMath.Asin(n.Z), DetMath.Atan2(n.Y, n.X));
    }

    /// <summary>The unit vector for a coordinate. Uses <see cref="DetMath"/>, so it is bit-identical across platforms. Inverse of <see cref="ToGeoCoord"/> up to rounding.</summary>
    public static Vector3D FromGeoCoord(GeoCoord geo)
    {
        var lat = geo.LatitudeRad;
        var lon = geo.LongitudeRad;
        var cosLat = DetMath.Cos(lat);
        return new Vector3D(
            cosLat * DetMath.Cos(lon),
            cosLat * DetMath.Sin(lon),
            DetMath.Sin(lat)
        );
    }
}
