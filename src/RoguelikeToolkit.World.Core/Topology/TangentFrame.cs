using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Topology;

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
