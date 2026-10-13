using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// A planet always keeps one face to its star: the substellar point is hot, the far side only warm
/// through atmospheric heat transport. <c>NightFloor</c> is the share of the mean energy the far side
/// still receives that way; the sphere-wide mean stays 1 (day side = <c>NightFloor + (1-NightFloor) * 4 cos</c>).
/// </summary>
public sealed class TidallyLockedInsolation : IInsolationModel
{
    public Vector3D Substellar { get; }
    public double NightFloor { get; }

    public TidallyLockedInsolation(Vector3D substellarDirection, double nightFloor = 0.35)
    {
        if (substellarDirection.Length <= 0.0) throw new ArgumentException("Substellar direction must be non-zero.", nameof(substellarDirection));
        if (nightFloor < 0.0 || nightFloor > 1.0) throw new ArgumentOutOfRangeException(nameof(nightFloor), nightFloor, "NightFloor must be in [0, 1].");
        Substellar = substellarDirection.Normalize();
        NightFloor = nightFloor;
    }

    public double RelativeInsolation(Vector3D unitPosition)
    {
        double cos = Math.Max(0.0, Vector3D.Dot(unitPosition, Substellar));
        return NightFloor + (1.0 - NightFloor) * 4.0 * cos;
    }
}
