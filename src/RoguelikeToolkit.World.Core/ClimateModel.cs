using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// The one place the planet's heat budget lives. Temperature used to be computed in three places (the
/// climate stage, <see cref="Glaciology.EstimateTemperature"/> and the biome stage's no-climate
/// fallback) that had to be kept in step by hand; they now all call <see cref="Temperature"/>.
/// <para>
/// Normalised units: Earth's mean surface temperature runs 0 (polar) to 1 (equatorial) before the lapse
/// rate, which is roughly 55 K per unit. Deviations from Earth enter as an insolation anomaly
/// (<see cref="TemperatureAnomaly"/>).
/// </para>
/// </summary>
public static class ClimateModel
{
    public const double DefaultLapseRate = 0.35;

    /// <summary>
    /// Temperature units per unit of change in the planet-wide mean insolation, i.e. of stellar flux.
    /// A 1% change in flux moves the black-body equilibrium by ~0.7 K; at ~55 K per unit, with
    /// ice-albedo and water-vapour feedback roughly doubling it, that is ~0.025 units per 1% flux, or 2.5
    /// per unit of relative flux. A +-10% flux is therefore a +-0.25 shift everywhere: the habitable range
    /// is narrow, as it is for Earth.
    /// </summary>
    public const double MeanSensitivity = 2.5;

    /// <summary>
    /// Temperature units per unit of *redistribution* of insolation between latitudes (what a different
    /// tilt does, with the same total energy). Smaller than <see cref="MeanSensitivity"/> because winds
    /// and ocean currents carry heat from where there is a surplus to where there is a deficit and
    /// flatten the response; without it a tilt of zero would freeze everything beyond 45 degrees.
    /// </summary>
    public const double GradientSensitivity = 1.0;

    /// <summary>
    /// Annual-mean daily insolation at a latitude, relative to the sphere-wide mean, for a circular
    /// orbit and an axial tilt of <paramref name="obliquityDegrees"/>. The second-order Legendre
    /// expansion S(x) = 1 + S2 P2(x), x = sin(latitude), S2 = -(5/16)(2 - 3 sin^2 obliquity)
    /// (North 1975), which for Earth is -0.477 and carries the whole tilt dependence to within a few
    /// percent. Closed form: no orbit integration, so it is exact, cheap and deterministic.
    /// </summary>
    public static double AnnualInsolation(double sinLatitude, double obliquityDegrees)
    {
        double sinEps = DetMath.Sin(obliquityDegrees * (Math.PI / 180.0));
        double s2 = -(5.0 / 16.0) * (2.0 - 3.0 * sinEps * sinEps);
        double p2 = 0.5 * (3.0 * sinLatitude * sinLatitude - 1.0);
        return 1.0 + s2 * p2;
    }

    /// <summary>Insolation of the Earth baseline at this latitude.</summary>
    public static double EarthInsolation(double sinLatitude) => AnnualInsolation(sinLatitude, PlanetParameters.EarthObliquityDegrees);

    /// <summary>
    /// Temperature anomaly against Earth (normalised units) at a surface point: the change in the
    /// planet-wide mean (flux) at <see cref="MeanSensitivity"/> plus the change in the latitude pattern
    /// (tilt or a custom field, after the flux scaling) at <see cref="GradientSensitivity"/>.
    /// </summary>
    public static double TemperatureAnomaly(PlanetParameters planet, Vector3D unitPosition)
    {
        double earth = EarthInsolation(unitPosition.Z);
        double here = planet.Insolation != null
            ? planet.Insolation.RelativeInsolation(unitPosition)
            : AnnualInsolation(unitPosition.Z, planet.ObliquityDegrees);
        double pattern = planet.StellarFlux * (here - 1.0) - (earth - 1.0);
        return MeanSensitivity * (planet.StellarFlux - 1.0) + GradientSensitivity * pattern;
    }

    /// <summary>
    /// Annual-mean temperature before clamping: latitude gradient, minus the lapse-rate cooling of
    /// height, plus the planet's temperature anomaly. The single source for the climate stage, the
    /// glacier fallback and the biome stage's no-climate path.
    /// </summary>
    public static double Temperature(Vector3D unitPosition, double absLatitudeDegrees, double height, double lapseRate, PlanetParameters? planet)
    {
        double t = 1.0 - absLatitudeDegrees / 90.0;
        t -= Math.Max(0.0, height) * lapseRate;
        if (planet != null && !planet.IsEarthBaseline)
            t += TemperatureAnomaly(planet, unitPosition);
        return t;
    }

    /// <summary>
    /// Half the annual swing of temperature (same units as Temperature) over open ocean at the
    /// latitude, before continentality. Grows with tilt (summer sun is higher, winter sun lower) and
    /// with latitude; a little remains with no tilt at all from the eccentric-orbit-free daily cycle.
    /// Earth at 45 degrees: 0.37 over land far from the sea, ~0.09 under maritime air.
    /// </summary>
    public static double SeasonalAmplitude(double sinLatitude, double obliquityDegrees)
    {
        double tilt = DetMath.Sin(obliquityDegrees * (Math.PI / 180.0)) / DetMath.Sin(PlanetParameters.EarthObliquityDegrees * (Math.PI / 180.0));
        return Math.Min(1.5, 0.03 + 0.48 * tilt * Math.Abs(sinLatitude));
    }

    /// <summary>Continentality: maritime air (1 over the sea) damps the swing to a quarter, deep interiors keep all of it.</summary>
    public static double Continental(double amplitude, double maritime) => amplitude * (0.25 + 0.75 * (1.0 - maritime));

    /// <summary>
    /// How seasonal the rainfall is at a signed latitude, 0 (even all year) .. 1 (a monsoon or a
    /// Mediterranean regime). The rain belts follow the overhead sun, so a place is seasonal when the
    /// belt's seasonal excursion carries it between a wet zone and a dry one: it is the spread of the
    /// zonal rainfall multiplier between the two solstice positions of the belts, over their mean. The
    /// excursion is ~0.65 of the tilt (15 degrees on Earth, as the monsoons reach), so it vanishes with no tilt.
    /// </summary>
    public static double RainSeasonality(double signedLatitudeDegrees, double obliquityDegrees, double zonalStrength, double bandScale)
    {
        double shift = 0.65 * obliquityDegrees;
        if (shift <= 0.0) return 0.0;
        double a = ClimateStage.ZonalFactor(Math.Abs(signedLatitudeDegrees + shift), zonalStrength, bandScale);
        double b = ClimateStage.ZonalFactor(Math.Abs(signedLatitudeDegrees - shift), zonalStrength, bandScale);
        return Math.Clamp(2.0 * Math.Abs(a - b) / (a + b), 0.0, 1.0);
    }
}
