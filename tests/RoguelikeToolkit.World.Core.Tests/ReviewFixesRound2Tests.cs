using System;
using System.IO;
using System.Linq;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Second review round: plugin probing, danger re-basing, GridCellAt and sort replacements.</summary>
public class ReviewFixesRound2Tests
{
    [Fact]
    public void MayContainStages_SkipsValidDependencies_ButNotUnparseableFiles()
    {
        Assert.False(PluginLoadContext.MayContainStages(typeof(object).Assembly.Location));

        // A tests assembly references Core; Core itself does not reference itself.
        Assert.True(PluginLoadContext.MayContainStages(typeof(ReviewFixesRound2Tests).Assembly.Location));
        Assert.False(PluginLoadContext.MayContainStages(typeof(IWorldGeneratorStage).Assembly.Location));

        var junk = Path.Combine(Path.GetTempPath(), $"junk-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4, 5 });
        try { Assert.True(PluginLoadContext.MayContainStages(junk)); } // surfaced by the real load
        finally { File.Delete(junk); }
    }

    [Fact]
    public void Discover_IgnoresDependencyDlls_WithoutDiagnostics()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.Copy(typeof(object).Assembly.Location, Path.Combine(dir, "Framework.dll"));

            var diagnostics = new System.Collections.Generic.List<StageDiscoveryDiagnostic>();
            var pipeline = new WorldGenerationPipeline();
            pipeline.Discover(dir, diagnostics.Add);
            Assert.Empty(diagnostics);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Smoothing_RebasesDanger_ToTheNewBiome()
    {
        using var store = new WorldDataStore(2);
        store.RegisterLayer<LocalMapInfo>();
        store.Allocate();
        var span = store.GetSpan<LocalMapInfo>();
        for (int i = 0; i < span.Length; i++)
            span[i] = new LocalMapInfo { Biome = BiomeType.Plains, DangerLevel = 0 };

        // One Jungle tile (base 2, jitter +1) ringed by Plains must become Plains with base 0 + jitter 1.
        span[5] = new LocalMapInfo { Biome = BiomeType.Jungle, DangerLevel = 3 };
        LocalMapGenerationStage.SmoothMoistureBiomes(store, span, 1);

        Assert.Equal(BiomeType.Plains, span[5].Biome);
        Assert.Equal(1, span[5].DangerLevel);
    }

    [Theory]
    [InlineData(8, 40.0, 10.0, 10.0)]
    [InlineData(16, 120.0, -45.0, 170.0)]
    [InlineData(25, 600.0, 89.0, 0.0)]
    [InlineData(40, 5.0, 0.0, 180.0)]
    [InlineData(12, 3000.0, 20.0, 20.0)]
    public void GridCellAt_MatchesExhaustiveScan(int size, double radiusKm, double lat, double lon)
    {
        var center = new GeoCoord(lat, lon);
        var rng = new Random(1234 + size);
        double spanDeg = Math.Min(60.0, radiusKm / World.EarthRadiusKm * 180.0 / Math.PI * 1.5);
        for (int k = 0; k < 400; k++)
        {
            var q = new GeoCoord(
                Math.Clamp(lat + (rng.NextDouble() * 2 - 1) * spanDeg, -90, 90),
                lon + (rng.NextDouble() * 2 - 1) * spanDeg);
            int fast = RegionMaps.GridCellAt(center, radiusKm, size, q);
            int exact = RegionMaps.GridCellAtExact(center, radiusKm, size, q);
            if (fast == exact) continue;
            // Allow only exact geometric ties.
            var t = Vector3D.FromGeoCoord(q);
            double df = Vector3D.Dot(t, Vector3D.FromGeoCoord(RegionMaps.GridCellCenter(center, radiusKm, size, fast)));
            double de = Vector3D.Dot(t, Vector3D.FromGeoCoord(RegionMaps.GridCellCenter(center, radiusKm, size, exact)));
            Assert.True(Math.Abs(df - de) < 1e-12, $"size={size} q={q} fast={fast} exact={exact}");
        }
    }

    [Fact]
    public void DescendingByValue_MatchesComparisonSort()
    {
        var rng = new Random(7);
        var values = new float[5000];
        for (int i = 0; i < values.Length; i++)
            values[i] = (rng.Next(6)) switch
            {
                0 => 0f, 1 => -0f, 2 => (float)Math.Round(rng.NextDouble(), 1),
                3 => float.NegativeInfinity, 4 => float.NaN, _ => (float)(rng.NextDouble() * 4 - 2),
            };

        var expected = Enumerable.Range(0, values.Length).ToArray();
        Array.Sort(expected, (a, b) =>
        {
            int c = values[b].CompareTo(values[a]);
            return c != 0 ? c : a.CompareTo(b);
        });
        Assert.Equal(expected, TileOrdering.DescendingByValue(values));
    }
}
