using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Ocean currents: the wind-driven gyres, western intensification, the heat they carry, and what that does to
/// the coasts they run along. The geometry tests use a synthetic world, one rectangular ocean basin in a
/// planet of land, so the expected circulation is the textbook one and not whatever noise made.
/// </summary>
public class OceanCurrentTests
{
    private const double BasinWest = 0.0, BasinEast = 60.0, BasinLat = 70.0;

    [WorldGeneratorStage(15, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class BasinStage : IWorldGeneratorStage
    {
        public void Execute(WorldMap map)
        {
            var store = map.DataStore;
            var elev = store.GetSpan<ElevationInfo>();
            var vectors = store.GetTileVectors();
            for (int i = 0; i < store.TileCount; i++)
            {
                var g = vectors[i].ToGeoCoord();
                bool basin = g.Longitude >= BasinWest && g.Longitude <= BasinEast && Math.Abs(g.Latitude) <= BasinLat;
                elev[i] = new ElevationInfo { Height = basin ? -0.5f : 0.4f };
            }
        }
    }

    private static World BuildBasin(Action<ClimateStage>? configure = null, PlanetParameters? planet = null, int size = 5)
    {
        var climate = new ClimateStage(1);
        configure?.Invoke(climate);
        if (planet != null) climate.Planet = planet;
        var builder = new WorldBuilder().WithSize(size).WithSeed(1).WithoutDefaultStages();
        builder.AddStage(new BasinStage());
        builder.AddStage(climate);
        return builder.Build();
    }

    private static OceanCurrentField SolveBasin(World w, PlanetParameters? planet = null, OceanCurrentSettings? settings = null)
    {
        var store = w.Map.DataStore;
        return OceanCurrents.Solve(store, store.GetSpan<ElevationInfo>(), store.GetTileVectors(), planet ?? new PlanetParameters(), settings ?? OceanCurrentSettings.Default);
    }

    /// <summary>Mean and peak of |northward current| in a window, over ocean tiles.</summary>
    private static (double mean, double peak) Meridional(World w, OceanCurrentField f, double latLo, double latHi, double lonLo, double lonHi)
    {
        var vectors = w.Map.DataStore.GetTileVectors();
        double sum = 0, peak = 0; int n = 0;
        for (int i = 0; i < w.TileCount; i++)
        {
            var g = vectors[i].ToGeoCoord();
            if (g.Latitude < latLo || g.Latitude > latHi || g.Longitude < lonLo || g.Longitude > lonHi || f.Velocity[i].Length == 0.0) continue;
            var v = vectors[i];
            double hz = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            var north = Vector3D.Cross(v, new Vector3D(-v.Y / hz, v.X / hz, 0)).Normalize();
            double vn = Math.Abs(Vector3D.Dot(f.Velocity[i], north));
            sum += vn; peak = Math.Max(peak, vn); n++;
        }
        Assert.True(n > 0);
        return (sum / n, peak);
    }

    /// <summary>Tiles in a lat/lon window: (index, east component, north component) of the current, and the anomaly.</summary>
    private static (double east, double north, double anomaly, double speed, int n) Window(World w, OceanCurrentField f,
        double latLo, double latHi, double lonLo, double lonHi)
    {
        var vectors = w.Map.DataStore.GetTileVectors();
        double ue = 0, vn = 0, a = 0, sp = 0; int n = 0;
        for (int i = 0; i < w.TileCount; i++)
        {
            var g = vectors[i].ToGeoCoord();
            if (g.Latitude < latLo || g.Latitude > latHi || g.Longitude < lonLo || g.Longitude > lonHi) continue;
            if (f.Velocity[i].Length == 0.0) continue; // land
            var v = vectors[i];
            double hz = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            var east = new Vector3D(-v.Y / hz, v.X / hz, 0);
            var north = Vector3D.Cross(v, east).Normalize();
            ue += Vector3D.Dot(f.Velocity[i], east);
            vn += Vector3D.Dot(f.Velocity[i], north);
            a += f.Anomaly[i];
            sp += f.Velocity[i].Length;
            n++;
        }
        Assert.True(n > 0, $"no ocean tiles in lat [{latLo}, {latHi}] lon [{lonLo}, {lonHi}]");
        return (ue / n, vn / n, a / n, sp / n, n);
    }

    [Fact]
    public void SubtropicalGyre_FlowsWestAtTheTropicsAndEastAtItsPolewardEdge()
    {
        using var w = BuildBasin();
        var f = SolveBasin(w);
        var tropics = Window(w, f, 12, 25, 8, 52);
        var edge = Window(w, f, 35, 45, 8, 52);
        Assert.True(tropics.east < -0.3, $"north equatorial current should run west, got {tropics.east:F2}");
        Assert.True(edge.east > 0.3, $"the gyre's poleward flank should run east, got {edge.east:F2}");
    }

    [Fact]
    public void WesternBoundaryCurrent_IsPolewardWarmAndFast_EasternIsEquatorwardColdAndSlow()
    {
        using var w = BuildBasin();
        var f = SolveBasin(w);
        // Subtropical gyre, northern hemisphere: 28..40 degrees. West = beside lon 0, east = beside lon 60.
        var west = Window(w, f, 28, 40, 1, 8);
        var east = Window(w, f, 28, 40, 52, 59);
        Assert.True(west.north > 0.05, $"western boundary current should run poleward, got v={west.north:F2}");
        Assert.True(east.north < -0.05, $"eastern side should return equatorward, got v={east.north:F2}");
        Assert.True(west.anomaly > 0.02, $"western boundary current should be warm, got {west.anomaly:F3}");
        Assert.True(east.anomaly < -0.02, $"eastern boundary current should be cold, got {east.anomaly:F3}");
        Assert.True(west.anomaly > east.anomaly + 0.06, $"warm {west.anomaly:F3} vs cold {east.anomaly:F3}");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void WesternIntensification_TheWestSideJetIsNarrowerAndFaster(int size)
    {
        using var w = BuildBasin(size: size);
        var f = SolveBasin(w);
        var west = Meridional(w, f, 22, 40, 0, 12);
        var east = Meridional(w, f, 22, 40, 48, 60);
        Assert.True(west.mean > 1.4 * east.mean, $"size {size}: mean |v| west {west.mean:F2} vs east {east.mean:F2}");
        Assert.True(west.peak > 2.5 * east.peak, $"size {size}: peak |v| west {west.peak:F2} vs east {east.peak:F2}");
    }

    [Fact]
    public void WithoutTheBetaEffect_TheGyreIsSymmetric()
    {
        // The twin of the test above: the asymmetry is the beta term's doing and nothing else's. Without it
        // (a boundary layer a hundred radians wide) west and east are mirror images.
        using var w = BuildBasin();
        var f = SolveBasin(w, settings: new OceanCurrentSettings { ScreeningLength = 0.4, BoundaryWidth = 100.0, Reach = 0.45 });
        var west = Meridional(w, f, 22, 40, 0, 12);
        var east = Meridional(w, f, 22, 40, 48, 60);
        Assert.InRange(west.mean / east.mean, 0.8, 1.25);
        Assert.InRange(west.peak / east.peak, 0.7, 1.4);
    }

    [Fact]
    public void TheCirculation_IsTheSameWorldAtEveryResolution()
    {
        // The boundary layer is a physical width (radians), not a number of tiles; before that, the jet got
        // faster and the reference speed smaller with every extra detail level.
        using var coarse = BuildBasin(size: 4);
        using var fine = BuildBasin(size: 5);
        var a = SolveBasin(coarse);
        var b = SolveBasin(fine);
        Assert.InRange(b.ReferenceSpeed / a.ReferenceSpeed, 0.88, 1.12);
        var wa = Window(coarse, a, 28, 40, 1, 8);
        var wb = Window(fine, b, 28, 40, 1, 8);
        Assert.InRange(wb.anomaly - wa.anomaly, -0.03, 0.03);
    }

    [Fact]
    public void SouthernHemisphere_MirrorsTheNorth()
    {
        using var w = BuildBasin();
        var f = SolveBasin(w);
        var north = Window(w, f, 28, 40, 1, 8);
        var south = Window(w, f, -40, -28, 1, 8);
        Assert.True(south.north < -0.05, $"southern western boundary current should run poleward (south), got {south.north:F2}");
        Assert.Equal(north.anomaly, south.anomaly, 2);
        Assert.Equal(north.speed, south.speed, 1);
    }

    [Fact]
    public void SubpolarGyre_TurnsTheOtherWay()
    {
        using var w = BuildBasin();
        var f = SolveBasin(w);
        // Cyclonic: the western side carries cold polar water south, the eastern side warm water north.
        var west = Window(w, f, 50, 60, 1, 10);
        var east = Window(w, f, 50, 60, 50, 59);
        Assert.True(west.north < east.north, $"subpolar west v={west.north:F2} should be more equatorward than east v={east.north:F2}");
        Assert.True(west.anomaly < east.anomaly, $"subpolar west should be colder ({west.anomaly:F3}) than the east ({east.anomaly:F3})");
    }

    [Fact]
    public void NoFlowOnLand_AndTheStreamFunctionVanishesOnShores()
    {
        using var w = BuildBasin();
        var f = SolveBasin(w);
        var elev = w.Map.DataStore.GetSpan<ElevationInfo>();
        for (int i = 0; i < w.TileCount; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            Assert.Equal(0.0, f.Psi[i]);
            Assert.Equal(0.0, f.Velocity[i].Length);
            Assert.Equal(0.0, f.Anomaly[i]);
        }
    }

    [Fact]
    public void Solver_IsDeterministic()
    {
        using var w = BuildBasin();
        var a = SolveBasin(w);
        var b = SolveBasin(w);
        Assert.Equal(a.Psi, b.Psi);
        Assert.Equal(a.Anomaly, b.Anomaly);
        Assert.Equal(a.Velocity, b.Velocity);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    public void Solver_StaysFiniteAndBounded_OnEveryBandScaleAndOceanShape(double rotation)
    {
        // Over-relaxation diverged on ordinary worlds at the first attempt (the operator is not symmetric), so
        // pin the property that matters: whatever the planet and coastline, the answer is a finite, bounded field.
        var planet = new PlanetParameters { RotationRate = rotation };
        foreach (var (size, seed) in new[] { (4, 42), (5, 7), (5, 1234) })
        {
            var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithoutDefaultStages();
            builder.AddStage(new TectonicPlateGenerationStage(12, seed));
            builder.AddStage(new ElevationGenerationStage(seed));
            using var w = builder.Build();
            var f = SolveBasin(w, planet);
            Assert.True(f.ReferenceSpeed > 0.0 && double.IsFinite(f.ReferenceSpeed), $"s{size}/{seed} rot {rotation}: reference speed {f.ReferenceSpeed}");
            for (int i = 0; i < w.TileCount; i++)
            {
                Assert.True(double.IsFinite(f.Psi[i]) && double.IsFinite(f.Anomaly[i]), $"tile {i} not finite");
                Assert.InRange(f.Anomaly[i], -0.6, 0.6); // a whole equator-to-pole contrast is 1.0; a current never carries all of it
                Assert.InRange(f.Velocity[i].Length, 0.0, 200.0);
            }
        }
    }

    [Fact]
    public void WindStressCurl_HasTheGyreSigns()
    {
        double Curl(double latDeg) => OceanCurrents.WindStressCurl(Math.Sin(latDeg * Math.PI / 180.0), 1.0);
        Assert.True(Curl(30) < 0, "anticyclonic at the subtropical highs");
        Assert.True(Curl(60) > 0, "cyclonic at the subpolar lows");
        Assert.True(Curl(-30) > 0 && Curl(-60) < 0, "antisymmetric across the equator");
        Assert.True(Math.Abs(Curl(0)) < 1e-9, "no response on the equator");
        // A fast spinner's cells are narrower: its subtropical gyre sits nearer the equator.
        double fast = OceanCurrents.WindStressCurl(Math.Sin(15.0 * Math.PI / 180.0), 0.5);
        Assert.True(fast < 0, "at 2x rotation the subtropical gyre is centred on 15 degrees");
    }

    [Fact]
    public void WarmCurrentCoast_IsWarmerThanColdCurrentCoast_AtTheSameLatitude()
    {
        using var w = BuildBasin();
        var vectors = w.Map.DataStore.GetTileVectors();
        var elev = w.Map.DataStore.GetSpan<ElevationInfo>();
        var climate = w.Map.DataStore.GetSpan<ClimateInfo>();
        double westSum = 0, eastSum = 0; int westN = 0, eastN = 0;
        for (int i = 0; i < w.TileCount; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            var g = vectors[i].ToGeoCoord();
            if (g.Latitude < 25 || g.Latitude > 40) continue;
            if (g.Longitude < -6 && g.Longitude >= -12) { westSum += climate[i].OceanHeat; westN++; }   // beside the warm western current
            if (g.Longitude > 66 && g.Longitude <= 72) { eastSum += climate[i].OceanHeat; eastN++; }   // beside the cold eastern one
        }
        Assert.True(westN > 5 && eastN > 5);
        // The coast just beside the current catches a good share of it; a few tiles inland it is weaker.
        var coastWest = CoastHeat(w, -6.0, 0.0);
        var coastEast = CoastHeat(w, 60.0, 66.0);
        Assert.True(coastWest > coastEast + 0.01, $"warm coast {coastWest:F3} vs cold coast {coastEast:F3}");
    }

    private static double CoastHeat(World w, double lonLo, double lonHi)
    {
        var vectors = w.Map.DataStore.GetTileVectors();
        var elev = w.Map.DataStore.GetSpan<ElevationInfo>();
        var climate = w.Map.DataStore.GetSpan<ClimateInfo>();
        double sum = 0; int n = 0;
        for (int i = 0; i < w.TileCount; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            var g = vectors[i].ToGeoCoord();
            if (g.Latitude < 25 || g.Latitude > 40 || g.Longitude < lonLo || g.Longitude > lonHi) continue;
            sum += climate[i].OceanHeat; n++;
        }
        Assert.True(n > 3, $"only {n} coastal tiles in [{lonLo}, {lonHi}]");
        return sum / n;
    }

    [Fact]
    public void ColdCurrentCoast_UnderTheSubtropicalHigh_IsDrierThanWithoutCurrents()
    {
        using var on = BuildBasin();
        using var off = BuildBasin(c => c.OceanRainGain = 0.0);
        var vectors = on.Map.DataStore.GetTileVectors();
        var elev = on.Map.DataStore.GetSpan<ElevationInfo>();
        var pOn = on.Map.DataStore.GetSpan<ClimateInfo>();
        var pOff = off.Map.DataStore.GetSpan<ClimateInfo>();
        double sumOn = 0, sumOff = 0; int n = 0;
        for (int i = 0; i < on.TileCount; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            var g = vectors[i].ToGeoCoord();
            if (g.Latitude < 18 || g.Latitude > 38 || g.Longitude < 60 || g.Longitude > 70) continue; // the eastern (cold-current) shore
            sumOn += pOn[i].Precipitation; sumOff += pOff[i].Precipitation; n++;
        }
        Assert.True(n > 5);
        Assert.True(sumOn / n < sumOff / n - 0.005, $"cold-current coast should be drier: {sumOn / n:F3} vs {sumOff / n:F3} without");
    }

    [Fact]
    public void SwitchedOff_LeavesNoTraceInTheLayer()
    {
        using var w = BuildBasin(c => { c.OceanHeatStrength = 0.0; c.OceanRainGain = 0.0; });
        var climate = w.Map.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < climate.Length; i++)
        {
            Assert.Equal(0f, climate[i].OceanHeat);
            Assert.Equal(0f, climate[i].CurrentX);
            Assert.Equal(0f, climate[i].CurrentY);
            Assert.Equal(0f, climate[i].CurrentZ);
        }
    }

    [Fact]
    public void Heat_IsExactlyTheOceanHeatStoredOnEveryTile()
    {
        // Temperature = clamp(model + OceanHeat): the stored OceanHeat is the whole contribution, so a tool can
        // subtract it to see the climate without the currents.
        using var w = new WorldBuilder().WithSize(4).WithSeed(11).Build();
        var store = w.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        var vectors = store.GetTileVectors();
        int touched = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            double model = ClimateModel.Temperature(vectors[i], Math.Abs(vectors[i].ToGeoCoord().Latitude), elev[i].Height, ClimateModel.DefaultLapseRate, w.Planet);
            Assert.InRange(Math.Abs(Math.Clamp(model + climate[i].OceanHeat, 0.0, 1.0) - climate[i].Temperature), 0.0, 2e-6); // OceanHeat is stored as a float
            if (elev[i].Height >= ElevationGenerationStage.SeaLevel && Math.Abs(climate[i].OceanHeat) > 0.02f) touched++;
        }
        Assert.True(touched > 5, "a default world should have some coast under a current's influence");
    }

    [Theory]
    [InlineData(4, 42)]
    [InlineData(5, 1234)]
    public void TheMultilevelSolve_MatchesAFlatReference(int size, int seed)
    {
        // The nested-iteration solver is only worth having if it lands where plain Gauss-Seidel would, given
        // time: 20000 flat sweeps is the slow, obviously-correct answer.
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithoutDefaultStages();
        builder.AddStage(new TectonicPlateGenerationStage(12, seed));
        builder.AddStage(new ElevationGenerationStage(seed));
        using var w = builder.Build();
        var fast = SolveBasin(w);
        var reference = SolveBasin(w, settings: new OceanCurrentSettings { ScreeningLength = 0.4, BoundaryWidth = 0.06, Reach = 0.45, ReferenceSweeps = 20000 });
        double num = 0, den = 0, anomaly = 0; int ocean = 0;
        var elev = w.Map.DataStore.GetSpan<ElevationInfo>();
        for (int i = 0; i < w.TileCount; i++)
        {
            num += (fast.Psi[i] - reference.Psi[i]) * (fast.Psi[i] - reference.Psi[i]);
            den += reference.Psi[i] * reference.Psi[i];
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) { anomaly += Math.Abs(fast.Anomaly[i] - reference.Anomaly[i]); ocean++; }
        }
        Assert.InRange(Math.Sqrt(num / den), 0.0, 0.02);   // relative L2 error of the stream function
        Assert.InRange(anomaly / ocean, 0.0, 0.001);       // mean error of the carried heat, 0.05 K
        Assert.InRange(fast.ReferenceSpeed / reference.ReferenceSpeed, 0.98, 1.02);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheMeshHierarchy_Nests_SoCoarseLevelsAreIndexPrefixes(int size)
    {
        // The multilevel solve relies on an icosphere appending each level's midpoint vertices after the previous
        // level's: the first N(size-1) tiles are the coarser mesh, and each new tile bisects exactly one coarse edge.
        using var fine = new WorldMap(size);
        using var coarse = new WorldMap(size - 1);
        var fv = fine.DataStore.GetTileVectors();
        var cv = coarse.DataStore.GetTileVectors();
        for (int i = 0; i < cv.Length; i++)
            Assert.True((fv[i] - cv[i]).Length < 1e-12, $"tile {i} moved between levels");
        Span<int> nb = stackalloc int[6];
        for (int m = cv.Length; m < fv.Length; m++)
        {
            int count = fine.DataStore.GetAdjacent(m, nb), below = 0;
            for (int k = 0; k < count; k++) if (nb[k] < cv.Length) below++;
            Assert.Equal(2, below);
        }
    }

    [Fact]
    public void ATinySea_StaysFiniteAndBounded()
    {
        // A pond a few tiles wide: the solve must neither fail nor blow up on a basin it can barely resolve.
        var builder = new WorldBuilder().WithSize(4).WithSeed(1).WithoutDefaultStages();
        builder.AddStage(new PondStage());
        using var w = builder.Build();
        var f = SolveBasin(w);
        double max = 0;
        for (int i = 0; i < w.TileCount; i++) max = Math.Max(max, f.Velocity[i].Length);
        Assert.True(f.ReferenceSpeed > 0.0);
        Assert.InRange(max, 0.0, 200.0);
        for (int i = 0; i < w.TileCount; i++) Assert.True(double.IsFinite(f.Anomaly[i]));
    }

    [WorldGeneratorStage(15, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class PondStage : IWorldGeneratorStage
    {
        public void Execute(WorldMap map)
        {
            var store = map.DataStore;
            var elev = store.GetSpan<ElevationInfo>();
            var vectors = store.GetTileVectors();
            for (int i = 0; i < store.TileCount; i++)
            {
                var g = vectors[i].ToGeoCoord();
                bool pond = Math.Abs(g.Latitude - 35) < 6 && Math.Abs(g.Longitude) < 8; // a few tiles of sea in a planet of land
                elev[i] = new ElevationInfo { Height = pond ? -0.5f : 0.4f };
            }
        }
    }

    [Fact]
    public void TheFirstClimatePass_SkipsTheCurrents_WhenARefreshPassFollows()
    {
        World Staged(bool deferred)
        {
            var climate = new ClimateStage(7) { CurrentsInRefreshPassOnly = deferred };
            var builder = new WorldBuilder().WithSize(4).WithSeed(7).WithoutDefaultStages();
            builder.AddStage(new TectonicPlateGenerationStage(12, 7));
            builder.AddStage(new ElevationGenerationStage(7));
            builder.AddStage(climate);
            return builder.Build();
        }
        using var deferred = Staged(true);
        using var solved = Staged(false);
        var skipped = deferred.Map.DataStore.GetSpan<ClimateInfo>();
        var withCurrents = solved.Map.DataStore.GetSpan<ClimateInfo>();
        double a = 0, b = 0;
        for (int i = 0; i < skipped.Length; i++) { a += Math.Abs(skipped[i].OceanHeat); b += Math.Abs(withCurrents[i].OceanHeat); }
        Assert.Equal(0.0, a);
        Assert.True(b > 1.0, "a single-pass climate has to solve the currents itself");
    }

    [Fact]
    public void WithoutTheRefreshPass_TheSinglePassClimateStillHasCurrents()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(42).WithClimateRefresh(false).Build();
        var climate = w.Map.DataStore.GetSpan<ClimateInfo>();
        double heat = 0;
        for (int i = 0; i < climate.Length; i++) heat += Math.Abs(climate[i].OceanHeat);
        Assert.True(heat > 1.0);
    }

    [Fact]
    public void TheRefreshPass_SeesTheCurrentsToo()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var climate = w.Map.DataStore.GetSpan<ClimateInfo>();
        double maxSpeed = 0;
        for (int i = 0; i < climate.Length; i++)
            maxSpeed = Math.Max(maxSpeed, new Vector3D(climate[i].CurrentX, climate[i].CurrentY, climate[i].CurrentZ).Length);
        Assert.True(maxSpeed > 0.5, "the final climate layer carries the current field");
    }

    // --- The reference-speed normalisation: degenerate seas -------------------------------------------------------
    // Velocity is divided by the 75th-percentile speed of the sea. A sea that barely moves has two ways to make that
    // zero: nothing moves at all (the wind-stress curl is exactly zero on the equator), or most of it is still and
    // only a few tiles move. Neither may divide by zero or leave a field that is not a unit-scale current.

    private static (WorldDataStore store, Vector3D[] vec, ElevationInfo[] land) EmptySea(World w)
    {
        var store = w.Map.DataStore;
        var vec = store.GetTileVectors().ToArray();
        var land = new ElevationInfo[store.TileCount];
        for (int i = 0; i < land.Length; i++) land[i] = new ElevationInfo { Height = 0.4f };
        return (store, vec, land);
    }

    private static OceanCurrentField SolveWith(WorldDataStore store, Vector3D[] vec, ElevationInfo[] elev)
        => OceanCurrents.Solve(store, elev, vec, new PlanetParameters(), OceanCurrentSettings.Default);

    [Fact]
    public void ALoneEquatorialTile_HasNoCurrent_AndNothingIsNotANumber()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(1).WithoutDefaultStages().Build();
        var (store, vec, elev) = EmptySea(w);
        int t = Array.FindIndex(vec, v => Math.Abs(v.Z) < 1e-12);
        Assert.True(t >= 0, "an icosphere has tiles exactly on the equator");
        elev[t] = new ElevationInfo { Height = -0.5f };
        var f = SolveWith(store, vec, elev);
        Assert.Equal(0.0, f.ReferenceSpeed);
        for (int i = 0; i < vec.Length; i++)
        {
            Assert.Equal(0.0, f.Velocity[i].Length);
            Assert.False(double.IsNaN(f.Psi[i]) || double.IsNaN(f.Anomaly[i]));
        }
    }

    [Fact]
    public void ASeaWhereMostTilesAreStill_IsScaledByItsFastestTile()
    {
        // The equatorial ring has psi = 0 (no forcing there); one tile just north of it is the only thing that moves.
        using var w = new WorldBuilder().WithSize(3).WithSeed(1).WithoutDefaultStages().Build();
        var (store, vec, elev) = EmptySea(w);
        var ring = new System.Collections.Generic.List<int>();
        for (int i = 0; i < vec.Length; i++) if (Math.Abs(vec[i].Z) < 1e-12) ring.Add(i);
        int tried = 0, still = 0;
        for (int north = 0; north < vec.Length; north++)
        {
            if (vec[north].Z <= 1e-3 || vec[north].Z >= 0.12) continue;
            var sea = (ElevationInfo[])elev.Clone();
            foreach (int i in ring) sea[i] = new ElevationInfo { Height = -0.5f };
            sea[north] = new ElevationInfo { Height = -0.5f };
            var f = SolveWith(store, vec, sea);
            var speeds = new System.Collections.Generic.List<double>();
            for (int i = 0; i < sea.Length; i++) if (sea[i].Height < ElevationGenerationStage.SeaLevel) speeds.Add(f.Velocity[i].Length);
            speeds.Sort();
            tried++;
            if (speeds[(int)(speeds.Count * 0.75)] != 0.0) continue; // this one is in the ordinary regime
            still++;
            Assert.True(f.ReferenceSpeed > 0.0, "falls back to the fastest tile, not to zero");
            Assert.Equal(1.0, speeds[speeds.Count - 1], 9);
        }
        Assert.True(tried > 0 && still > 0, $"no mostly-still sea found ({still} of {tried})");
    }
}
