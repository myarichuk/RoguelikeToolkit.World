using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Planet parameters (tilt, flux, spin, custom insolation). The central guarantee is that an Earth
/// planet changes nothing: every other test here is about the *direction* and *shape* of a deviation,
/// because there is no ground truth for a made-up planet, only physics to stay consistent with.
/// </summary>
public class PlanetTests
{
    private const float Land = 0.3f;

    /// <summary>A flat all-land planet with no ocean and no hydrology: climate depends on latitude alone.</summary>
    private static WorldMap Climate(PlanetParameters? planet = null, double meridional = 0.0, int size = 3)
    {
        var map = new WorldMap(size);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.DataStore.Allocate();
        var elev = map.DataStore.GetSpan<ElevationInfo>();
        for (int i = 0; i < elev.Length; i++) elev[i] = new ElevationInfo { Height = Land };
        var stage = new ClimateStage(7) { MeridionalWind = meridional };
        if (planet != null) stage.Planet = planet;
        stage.Execute(map);
        return map;
    }

    private static double AbsLat(WorldMap map, int i) => Math.Abs(map.DataStore.GetTileVectors()[i].ToGeoCoord().Latitude);

    /// <summary>Mean of a per-tile value over the tiles whose |latitude| lies in [lo, hi).</summary>
    private static double BandMean(WorldMap map, double lo, double hi, Func<ClimateInfo, double> f)
    {
        var climate = map.DataStore.GetSpan<ClimateInfo>();
        double sum = 0; int n = 0;
        for (int i = 0; i < climate.Length; i++)
        {
            double lat = AbsLat(map, i);
            if (lat < lo || lat >= hi) continue;
            sum += f(climate[i]); n++;
        }
        Assert.True(n > 0, $"no tiles in {lo}..{hi}");
        return sum / n;
    }

    [Fact]
    public void Earth_IsTheLegacyField()
    {
        // The reference is the pre-planet arithmetic written out again: any drift in the default path
        // shows up as a tile that differs.
        using var map = Climate(new PlanetParameters());
        var climate = map.DataStore.GetSpan<ClimateInfo>();
        var vectors = map.DataStore.GetTileVectors();
        for (int i = 0; i < climate.Length; i++)
        {
            var geo = vectors[i].ToGeoCoord();
            double lat = Math.Abs(geo.Latitude);
            double t = 1.0 - lat / 90.0;
            t -= Math.Max(0.0, Land) * 0.35;
            Assert.Equal((float)Math.Clamp(t, 0.0, 1.0), climate[i].Temperature);

            var east = new Vector3D(-DetMath.Sin(geo.LongitudeRad), DetMath.Cos(geo.LongitudeRad), 0).Normalize();
            double dir = lat < 30.0 ? -1.0 : (lat < 60.0 ? 1.0 : -1.0);
            Assert.Equal((float)(east * dir).X, climate[i].WindX);
            Assert.Equal((float)(east * dir).Y, climate[i].WindY);
            Assert.Equal(0f, climate[i].WindZ);
        }
        Assert.True(new PlanetParameters().IsEarthBaseline);
    }

    [Fact]
    public void ZonalFactor_DefaultScaleIsTheOriginal()
    {
        for (double lat = 0; lat <= 90; lat += 0.5)
            Assert.Equal(ClimateStage.ZonalFactor(lat, 1.0), ClimateStage.ZonalFactor(lat, 1.0, 1.0));
    }

    [Fact]
    public void Insolation_MatchesEarthAndConservesEnergy()
    {
        Assert.Equal(1.2385, ClimateModel.AnnualInsolation(0.0, PlanetParameters.EarthObliquityDegrees), 3);
        Assert.Equal(0.5230, ClimateModel.AnnualInsolation(1.0, PlanetParameters.EarthObliquityDegrees), 3);
        // The sphere-wide mean (area weight = dx for x = sin lat) is 1 whatever the tilt.
        foreach (double eps in new[] { 0.0, 23.44, 54.0, 90.0 })
        {
            double mean = 0; const int n = 4000;
            for (int k = 0; k < n; k++) mean += ClimateModel.AnnualInsolation(-1.0 + (k + 0.5) * 2.0 / n, eps) / n;
            Assert.Equal(1.0, mean, 3);
        }
    }

    [Fact]
    public void Tilt_MovesWarmthFromEquatorToPoles()
    {
        double Raw(double eps, double lat)
        {
            var v = new Vector3D(Math.Cos(lat * Math.PI / 180), 0, Math.Sin(lat * Math.PI / 180));
            return ClimateModel.Temperature(v, lat, 0.0, 0.35, new PlanetParameters { ObliquityDegrees = eps });
        }
        double prevPole = double.NegativeInfinity, prevEq = double.PositiveInfinity;
        foreach (double eps in new[] { 0.0, 10.0, 23.44, 45.0, 70.0, 90.0 })
        {
            double pole = Raw(eps, 89.0), eq = Raw(eps, 0.0);
            Assert.True(pole > prevPole, $"pole must warm with tilt (eps {eps})");
            Assert.True(eq < prevEq, $"equator must cool with tilt (eps {eps})");
            prevPole = pole; prevEq = eq;
        }
        Assert.Equal(1.0 - 89.0 / 90.0, Raw(PlanetParameters.EarthObliquityDegrees, 89.0), 9);
    }

    [Fact]
    public void Tilt_ChangesTheStagesTemperatureField()
    {
        using var earth = Climate();
        using var upright = Climate(new PlanetParameters { ObliquityDegrees = 0 });
        using var lying = Climate(new PlanetParameters { ObliquityDegrees = 85 });
        double polarEarth = BandMean(earth, 70, 91, c => c.Temperature);
        Assert.True(BandMean(upright, 70, 91, c => c.Temperature) <= polarEarth, "no tilt: the poles are no warmer");
        Assert.True(BandMean(lying, 70, 91, c => c.Temperature) > polarEarth + 0.1, "lying on its side: the poles are much warmer");
        Assert.True(BandMean(upright, 0, 10, c => c.Temperature) >= BandMean(earth, 0, 10, c => c.Temperature));
        Assert.True(BandMean(lying, 0, 10, c => c.Temperature) < BandMean(earth, 0, 10, c => c.Temperature));
    }

    [Fact]
    public void Flux_WarmsEverything()
    {
        using var earth = Climate();
        using var bright = Climate(new PlanetParameters { StellarFlux = 1.08 });
        using var dim = Climate(new PlanetParameters { StellarFlux = 0.9 });
        var e = earth.DataStore.GetSpan<ClimateInfo>();
        var b = bright.DataStore.GetSpan<ClimateInfo>();
        var d = dim.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < e.Length; i++)
        {
            Assert.True(b[i].Temperature >= e[i].Temperature, $"tile {i} bright");
            Assert.True(d[i].Temperature <= e[i].Temperature, $"tile {i} dim");
        }
        Assert.True(BandMean(bright, 30, 50, c => c.Temperature) > BandMean(earth, 30, 50, c => c.Temperature) + 0.05);
        Assert.True(BandMean(dim, 30, 50, c => c.Temperature) < BandMean(earth, 30, 50, c => c.Temperature) - 0.05);
    }

    [Fact]
    public void Rotation_SetsWhereTheDryBeltIs()
    {
        double Dry(double scale)
        {
            double best = 0, min = double.MaxValue;
            for (double lat = 5; lat <= 70; lat += 0.25)
            {
                double z = ClimateStage.ZonalFactor(lat, 1.0, scale);
                if (z < min) { min = z; best = lat; }
            }
            return best;
        }
        Assert.InRange(Dry(1.0), 25, 29);
        Assert.InRange(Dry(0.5), 12, 15);   // spinning twice as fast: the cell ends at half the latitude
        Assert.InRange(Dry(2.0), 50, 58);   // half the spin: it reaches twice as far
        Assert.True(Dry(0.5) < Dry(1.0) && Dry(1.0) < Dry(2.0));
    }

    [Fact]
    public void Rotation_ChangesTheNumberOfWindBands()
    {
        // Earth: trades below 30, westerlies 30..60, polar easterlies. A twice-as-fast planet alternates every 15 degrees.
        using var earth = Climate();
        using var fast = Climate(new PlanetParameters { RotationRate = 2.0 });
        double Eastward(WorldMap m, double lo, double hi)
        {
            var vectors = m.DataStore.GetTileVectors();
            var climate = m.DataStore.GetSpan<ClimateInfo>();
            double sum = 0; int n = 0;
            for (int i = 0; i < climate.Length; i++)
            {
                double lat = AbsLat(m, i);
                if (lat < lo || lat >= hi) continue;
                var geo = vectors[i].ToGeoCoord();
                var east = new Vector3D(-DetMath.Sin(geo.LongitudeRad), DetMath.Cos(geo.LongitudeRad), 0).Normalize();
                sum += Vector3D.Dot(new Vector3D(climate[i].WindX, climate[i].WindY, climate[i].WindZ), east); n++;
            }
            Assert.True(n > 0);
            return sum / n;
        }
        Assert.True(Eastward(earth, 5, 25) < -0.9, "Earth lat 5-25: trades");
        Assert.True(Eastward(earth, 35, 55) > 0.9, "Earth lat 35-55: westerlies");
        Assert.True(Eastward(fast, 5, 12) < -0.9);
        Assert.True(Eastward(fast, 18, 27) > 0.9, "fast spinner: westerlies where Earth still has trades");
        Assert.True(Eastward(fast, 33, 42) < -0.9);
    }

    [Fact]
    public void MeridionalWind_FollowsTheCells()
    {
        using var map = Climate(meridional: 0.45);
        var vectors = map.DataStore.GetTileVectors();
        var climate = map.DataStore.GetSpan<ClimateInfo>();
        double trade = 0, ferrel = 0, polar = 0;
        int nt = 0, nf = 0, np = 0;
        for (int i = 0; i < climate.Length; i++)
        {
            var wind = new Vector3D(climate[i].WindX, climate[i].WindY, climate[i].WindZ);
            Assert.Equal(1.0, wind.Length, 5);
            var geo = vectors[i].ToGeoCoord();
            var east = new Vector3D(-DetMath.Sin(geo.LongitudeRad), DetMath.Cos(geo.LongitudeRad), 0).Normalize();
            var north = Vector3D.Cross(vectors[i], east).Normalize();
            double towardPole = Vector3D.Dot(wind, north) * Math.Sign(geo.Latitude);
            double lat = Math.Abs(geo.Latitude);
            if (lat < 3) continue; // the north vector's sign is meaningless at the equator
            if (lat < 30) { trade += towardPole; nt++; }
            else if (lat < 60) { ferrel += towardPole; nf++; }
            else { polar += towardPole; np++; }
        }
        Assert.True(trade / nt < -0.3, "Hadley surface flow runs towards the equator in both hemispheres");
        Assert.True(ferrel / nf > 0.3, "the Ferrel surface flow runs poleward");
        Assert.True(polar / np < -0.3, "the polar easterlies run equatorward");
    }

    [Fact]
    public void MeridionalWind_OffMeansPurelyZonal()
    {
        using var map = Climate(meridional: 0.0);
        var vectors = map.DataStore.GetTileVectors();
        var climate = map.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < climate.Length; i++)
            Assert.Equal(0f, climate[i].WindZ);
    }

    [Fact]
    public void SeasonalRange_GrowsWithTiltAndLatitudeAndShrinksAtSea()
    {
        using var earth = Climate();
        using var upright = Climate(new PlanetParameters { ObliquityDegrees = 0 });
        using var lying = Climate(new PlanetParameters { ObliquityDegrees = 60 });
        Assert.True(BandMean(earth, 40, 50, c => c.SeasonalRange) > BandMean(earth, 0, 10, c => c.SeasonalRange) * 3);
        Assert.True(BandMean(lying, 40, 50, c => c.SeasonalRange) > BandMean(earth, 40, 50, c => c.SeasonalRange));
        Assert.True(BandMean(upright, 40, 50, c => c.SeasonalRange) < 0.05, "no tilt, no seasons to speak of");
        Assert.InRange(BandMean(earth, 40, 50, c => c.SeasonalRange), 0.25, 0.45); // deep continental interior, ~40 K swing
        Assert.Equal(0.4 * 0.25, ClimateModel.Continental(0.4, 1.0), 9);
    }

    [Fact]
    public void RainSeasonality_NeedsTiltAndPeaksBetweenTheBelts()
    {
        Assert.Equal(0.0, ClimateModel.RainSeasonality(20, 0, 1.0, 1.0));
        Assert.True(ClimateModel.RainSeasonality(0, 23.44, 1.0, 1.0) < 0.05, "the equator sits between the two excursions");
        Assert.True(ClimateModel.RainSeasonality(15, 23.44, 1.0, 1.0) > 0.4, "the monsoon margin");
        Assert.True(ClimateModel.RainSeasonality(-15, 23.44, 1.0, 1.0) > 0.4, "and its southern twin");
        Assert.True(ClimateModel.RainSeasonality(35, 23.44, 1.0, 1.0) > 0.2, "the Mediterranean margin");
        Assert.True(ClimateModel.RainSeasonality(15, 45, 1.0, 1.0) != ClimateModel.RainSeasonality(15, 23.44, 1.0, 1.0));
        for (double lat = -90; lat <= 90; lat += 3)
            Assert.InRange(ClimateModel.RainSeasonality(lat, 60, 1.0, 0.7), 0.0, 1.0);
    }

    [Fact]
    public void TidallyLocked_SubstellarPointIsTheHotSpot()
    {
        var lockedTo = new Vector3D(1, 0, 0);
        var locked = new TidallyLockedInsolation(lockedTo);
        double mean = 0; int n = 0;
        using (var map = Climate(new PlanetParameters { Insolation = locked }))
        {
            var vectors = map.DataStore.GetTileVectors();
            var climate = map.DataStore.GetSpan<ClimateInfo>();
            double sub = 0, anti = 0;
            int ns = 0, na = 0;
            for (int i = 0; i < climate.Length; i++)
            {
                double c = Vector3D.Dot(vectors[i], lockedTo);
                if (c > 0.9) { sub += climate[i].Temperature; ns++; }
                if (c < -0.9) { anti += climate[i].Temperature; na++; }
                mean += locked.RelativeInsolation(vectors[i]); n++;
            }
            Assert.True(sub / ns > anti / na + 0.3, "day side must be far warmer than the far side");
        }
        Assert.Equal(1.0, mean / n, 1);
        Assert.Throws<ArgumentException>(() => new TidallyLockedInsolation(new Vector3D(0, 0, 0)));
    }

    [Fact]
    public void Parameters_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetParameters { ObliquityDegrees = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetParameters { ObliquityDegrees = 91 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetParameters { StellarFlux = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetParameters { RotationRate = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetParameters { RotationRate = double.NaN });
    }

    [Fact]
    public void WithPlanet_CopiesAndReports_AndTheWorldIsDeterministic()
    {
        var p = new PlanetParameters { ObliquityDegrees = 50, StellarFlux = 0.95, RotationRate = 1.5 };
        using var a = new WorldBuilder().WithSize(3).WithSeed(11).WithPlanet(p).Build();
        p.ObliquityDegrees = 5; // after the fact: must not leak into the builder's copy
        using var b = new WorldBuilder().WithSize(3).WithSeed(11).WithPlanet(new PlanetParameters { ObliquityDegrees = 50, StellarFlux = 0.95, RotationRate = 1.5 }).Build();
        Assert.Equal(50, a.Planet.ObliquityDegrees);
        Assert.Equal(1.5, a.Planet.RotationRate);
        Assert.True(new WorldBuilder().WithSize(2).Build().Planet.IsEarthBaseline);

        var ca = a.Map.DataStore.GetSpan<ClimateInfo>();
        var cb = b.Map.DataStore.GetSpan<ClimateInfo>();
        var la = a.Map.DataStore.GetSpan<LocalMapInfo>();
        var lb = b.Map.DataStore.GetSpan<LocalMapInfo>();
        for (int i = 0; i < ca.Length; i++)
        {
            Assert.Equal(ca[i], cb[i]);
            Assert.Equal(la[i].Biome, lb[i].Biome);
        }
    }

    [Fact]
    public void ExtremePlanets_StillBuild_AndDifferFromEarth()
    {
        using var earth = new WorldBuilder().WithSize(3).WithSeed(5).Build();
        foreach (var p in new[]
        {
            new PlanetParameters { ObliquityDegrees = 0 },
            new PlanetParameters { ObliquityDegrees = 90, StellarFlux = 0.8 },
            new PlanetParameters { StellarFlux = 1.4, RotationRate = 0.3 },
            new PlanetParameters { RotationRate = 4.0, StellarFlux = 0.6 },
        })
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(5).WithPlanet(p).Build();
            var locals = w.Map.DataStore.GetSpan<LocalMapInfo>();
            var base_ = earth.Map.DataStore.GetSpan<LocalMapInfo>();
            int diff = 0;
            for (int i = 0; i < locals.Length; i++) if (locals[i].Biome != base_[i].Biome) diff++;
            Assert.True(diff > 0, "a different planet must make a different world");
        }
    }
}
