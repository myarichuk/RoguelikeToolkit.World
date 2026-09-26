using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class HydrographyTests
{
    [Fact]
    public void FullWorld_ProducesOceanAndDownhillRivers()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;

        Assert.NotEmpty(world.WaterBodies.Bodies);
        var ocean = world.WaterBodies.Bodies[0];
        Assert.Equal(WaterBodyKind.Ocean, ocean.Kind);
        Assert.NotEmpty(ocean.Boundary);

        // Every water tile sits below sea level or is a ponded lake tile.
        var elev = store.GetSpan<ElevationInfo>();
        var hydro = store.GetSpan<HydrologyInfo>();
        foreach (var body in world.WaterBodies.Bodies)
        {
            foreach (int t in body.Tiles)
                Assert.True(elev[t].Height < ElevationGenerationStage.SeaLevel || hydro[t].LakeDepth > 0f,
                    $"Water tile {t} is neither sea nor ponded lake");
        }

        // Rivers flow downhill on the routing surface to sea, lake, or sink.
        Assert.NotEmpty(world.Rivers.Rivers);
        foreach (var river in world.Rivers.Rivers)
        {
            Assert.True(river.Path.Count >= 2, $"River {river.Id} has no flow");
            for (int k = 0; k < river.Path.Count - 1; k++)
            {
                Assert.True(hydro[river.Path[k + 1]].Surface <= hydro[river.Path[k]].Surface + 1e-6f,
                    $"River {river.Id} flows uphill at step {k}");
            }
            int tail = river.Path[^1];
            bool tailSea = elev[tail].Height < ElevationGenerationStage.SeaLevel;
            bool tailLake = hydro[tail].LakeDepth > 0f;
            ClassifyPour(store, hydro, elev, river, tail, out bool pourSea, out bool pourLake, out bool pourRiver);
            if (river.Terminal == RiverTerminal.Sea)
                Assert.True(tailSea || pourSea || pourRiver,
                    $"Sea river {river.Id} ends inland without reaching sea or a river");
            else if (river.Terminal == RiverTerminal.Lake)
                Assert.True(tailLake || pourLake || pourRiver,
                    $"Lake river {river.Id} ends inland without reaching a lake or a river");
            else
                Assert.True(!tailSea && !tailLake && !pourSea && !pourLake && !pourRiver,
                    $"Sink river {river.Id} reaches water or another river");
        }
    }

    /// <summary>
    /// Classifies what the tail pours into past its path end: open sea, lake
    /// water, or another cataloged reach (tributary confluence). Sinks pour
    /// into none of these (their receiver is dry land).
    /// </summary>
    private static void ClassifyPour(
        WorldDataStore store,
        System.Span<HydrologyInfo> hydro,
        System.Span<ElevationInfo> elev,
        River river,
        int tail,
        out bool pourSea,
        out bool pourLake,
        out bool pourRiver)
    {
        pourSea = pourLake = pourRiver = false;
        Span<int> scratch = stackalloc int[6];
        int adjacent = store.GetAdjacent(tail, scratch);
        var path = new HashSet<int>(river.Path);
        for (int k = 0; k < adjacent; k++)
        {
            int nb = scratch[k];
            if (path.Contains(nb)) continue;
            if (hydro[nb].Surface > hydro[tail].Surface + 1e-6f) continue;
            if (elev[nb].Height < ElevationGenerationStage.SeaLevel) pourSea = true;
            else if (hydro[nb].LakeDepth > 0f) pourLake = true;
            else if (hydro[nb].IsRiver == 1) pourRiver = true;
        }
    }

    [Fact]
    public void FeatureWkt_HasExpectedShapes()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();

        string riverWkt = world.RiverToWkt(0);
        Assert.StartsWith("LINESTRING(", riverWkt);
        Assert.Contains(",", riverWkt);

        string waterWkt = world.WaterBodyToWkt(0);
        Assert.StartsWith("POLYGON((", waterWkt);
        Assert.EndsWith("))", waterWkt);

        // Deterministic: same seed reproduces identical WKT.
        using var second = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        Assert.Equal(riverWkt, second.RiverToWkt(0));
        Assert.Equal(waterWkt, second.WaterBodyToWkt(0));
    }

    [Fact]
    public void MountainRanges_DetectedAboveThreshold()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(7).Build();
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();

        Assert.NotEmpty(world.Ranges.Features);
        foreach (var feat in world.Ranges.Features)
        {
            Assert.NotEmpty(feat.Tiles);
            if (feat.Kind == RangeKind.MountainRange)
            {
                foreach (int t in feat.Tiles)
                    Assert.True(elev[t].Height >= 0.5f, $"Range tile {t} below mountain threshold");
            }
        }
    }
}
