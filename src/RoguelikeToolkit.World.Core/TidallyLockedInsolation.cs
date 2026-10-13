using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// A planet always keeps one face to its star: the substellar point is hot, the far side only warm
/// through atmospheric heat transport. <c>NightFloor</c> is the share of the mean energy the far side
/// still receives that way; the sphere-wide mean stays 1 (day side = <c>NightFloor + (1-NightFloor) * 4 cos</c>).
/// </summary>
public sealed class TidallyLockedInsolation : IInsolationModel
{
    /// <summary>Unit vector from the planet's centre to the substellar point (the constructor normalises the direction it is given).</summary>
    public Vector3D Substellar { get; }
    /// <summary>Insolation of the far side relative to the sphere-wide mean, in [0, 1].</summary>
    public double NightFloor { get; }

    /// <summary>Creates the model for a given substellar direction (any non-zero length). Throws <see cref="ArgumentException"/> for a zero-length direction and <see cref="ArgumentOutOfRangeException"/> for a <paramref name="nightFloor"/> outside [0, 1].</summary>
    public TidallyLockedInsolation(Vector3D substellarDirection, double nightFloor = 0.35)
    {
        if (substellarDirection.Length <= 0.0) throw new ArgumentException("Substellar direction must be non-zero.", nameof(substellarDirection));
        if (nightFloor < 0.0 || nightFloor > 1.0) throw new ArgumentOutOfRangeException(nameof(nightFloor), nightFloor, "NightFloor must be in [0, 1].");
        Substellar = substellarDirection.Normalize();
        NightFloor = nightFloor;
    }

    /// <summary>Relative insolation at a surface point: <c>NightFloor + (1 - NightFloor) * 4 * max(0, cos)</c>, where <c>cos</c> is the cosine of the angle to <see cref="Substellar"/>. Ranges from <see cref="NightFloor"/> on the night side to <c>4 - 3 * NightFloor</c> at the substellar point; <paramref name="unitPosition"/> is assumed to be unit length.</summary>
    public double RelativeInsolation(Vector3D unitPosition)
    {
        double cos = Math.Max(0.0, Vector3D.Dot(unitPosition, Substellar));
        return NightFloor + (1.0 - NightFloor) * 4.0 * cos;
    }
}
