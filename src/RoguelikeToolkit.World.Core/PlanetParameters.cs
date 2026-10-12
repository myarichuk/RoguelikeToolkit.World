using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Supplies the annual-mean stellar energy reaching a point of the surface, relative to the mean over
/// the whole sphere (1.0 = the planet-wide average). Implement it for worlds whose heating is not the
/// textbook "spinning, tilted ball around one sun": a tidally locked planet, a binary star.
/// Must be a pure function of the position (determinism).
/// </summary>
public interface IInsolationModel
{
    /// <param name="unitPosition">Unit vector from the planet's centre to the surface point (+Z is the spin axis).</param>
    double RelativeInsolation(Vector3D unitPosition);
}

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

/// <summary>
/// The astronomical knobs of the climate model. Every default is Earth, and an Earth-like set takes a
/// code path that adds exactly nothing, so existing worlds are bit-for-bit what they were before this
/// type existed. All three scalars act as <em>differences against Earth</em>: the biome thresholds
/// are calibrated on Earth's 0..1 temperature field, and a delta keeps that calibration valid.
/// <list type="bullet">
/// <item><see cref="ObliquityDegrees"/>: axial tilt. Sets the latitude distribution of annual-mean
/// insolation (0° = hot equator and frozen poles, 90° = the poles out-heat the equator) and the size of
/// the seasons.</item>
/// <item><see cref="StellarFlux"/>: the star's flux at the planet, 1 = Earth's. Raises or lowers every
/// temperature.</item>
/// <item><see cref="RotationRate"/>: spin relative to Earth. A circulation cell reaches a latitude
/// proportional to 1/rotation (Held &amp; Hou), so a fast planet packs the dry belts and storm tracks
/// towards the equator and breaks the winds into more bands; a slow one stretches them to the poles.</item>
/// </list>
/// </summary>
public sealed class PlanetParameters
{
    public const double EarthObliquityDegrees = 23.44;
    public const double MinFlux = 0.3, MaxFlux = 2.5;
    public const double MinRotation = 0.25, MaxRotation = 4.0;

    private double _obliquity = EarthObliquityDegrees, _flux = 1.0, _rotation = 1.0;

    /// <summary>Axial tilt in degrees, 0..90.</summary>
    public double ObliquityDegrees
    {
        get => _obliquity;
        set => _obliquity = value >= 0.0 && value <= 90.0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Obliquity must be in [0, 90] degrees.");
    }

    /// <summary>Stellar flux at the planet relative to Earth's, <see cref="MinFlux"/>..<see cref="MaxFlux"/>.</summary>
    public double StellarFlux
    {
        get => _flux;
        set => _flux = value >= MinFlux && value <= MaxFlux ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"Flux must be in [{MinFlux}, {MaxFlux}].");
    }

    /// <summary>Spin rate relative to Earth's, <see cref="MinRotation"/>..<see cref="MaxRotation"/>.</summary>
    public double RotationRate
    {
        get => _rotation;
        set => _rotation = value >= MinRotation && value <= MaxRotation ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"Rotation must be in [{MinRotation}, {MaxRotation}].");
    }

    /// <summary>
    /// Replaces the analytic (tilt-driven) insolation with a custom field, e.g.
    /// <see cref="TidallyLockedInsolation"/>. When set, obliquity no longer shapes the annual mean (it
    /// still sizes the seasons); flux still scales it.
    /// </summary>
    public IInsolationModel? Insolation { get; set; }

    /// <summary>Band-position multiplier for the circulation: 1 on Earth, &lt;1 for fast spinners, &gt;1 for slow ones.</summary>
    public double BandScale => _rotation == 1.0 ? 1.0 : 1.0 / _rotation;

    /// <summary>True when no parameter departs from Earth, so the climate takes the exact legacy arithmetic.</summary>
    public bool IsEarthBaseline => _obliquity == EarthObliquityDegrees && _flux == 1.0 && _rotation == 1.0 && Insolation == null;

    public PlanetParameters Clone() => new()
    {
        _obliquity = _obliquity, _flux = _flux, _rotation = _rotation, Insolation = Insolation,
    };
}
