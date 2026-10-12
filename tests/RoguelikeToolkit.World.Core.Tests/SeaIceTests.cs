using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class SeaIceTests
{
    private static readonly Vector3D Pole = new(0, 0, 1);
    private static readonly float Sea = (float)(ElevationGenerationStage.SeaLevel - 0.1);

    [Theory]
    [InlineData(0.60f, 0.05f, SeaIceKind.None)]       // a warm sea, whatever the season
    [InlineData(0.30f, 0.10f, SeaIceKind.Seasonal)]   // winter 0.20 - 0.03 margin is frozen, summer 0.40 is open
    [InlineData(0.10f, 0.05f, SeaIceKind.Perennial)]  // even the summer stays cold
    [InlineData(0.50f, 0.40f, SeaIceKind.Seasonal)]   // a huge swing freezes a mild sea in winter, and it thaws
    [InlineData(0.27f, 0.00f, SeaIceKind.None)]       // winter 0.27 is just above the freeze line (0.20 + margin): open, pins FreezeTemperature
    [InlineData(0.15f, 0.10f, SeaIceKind.Perennial)]  // summer 0.25 is just under the pack line (0.30 - margin): pins PerennialTemperature
    [InlineData(0.20f, 0.20f, SeaIceKind.Seasonal)]   // summer 0.40 is above it, winter 0.00 is frozen
    public void Classify_FollowsWinterAndSummerTemperature(float temperature, float range, SeaIceKind expected)
        => Assert.Equal(expected, SeaIce.Classify(Pole, Sea, temperature, range));

    [Fact]
    public void Land_IsNeverSeaIce()
        => Assert.Equal(SeaIceKind.None, SeaIce.Classify(Pole, (float)ElevationGenerationStage.SeaLevel, 0.0f, 0.0f));

    [Fact]
    public void ARealWorld_FreezesOnlyAtHighLatitudes_AndThePackIsFurtherPoleward()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(7).Build();
        var store = w.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var vectors = store.GetTileVectors();
        var mask = SeaIce.Mask(store);
        double seasonal = 0, perennial = 0; int ns = 0, np = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i] == SeaIceKind.None) continue;
            Assert.True(elev[i].Height < ElevationGenerationStage.SeaLevel, "ice on land");
            double lat = Math.Abs(vectors[i].ToGeoCoord().Latitude);
            Assert.True(lat > 40, $"sea ice at latitude {lat}");
            if (mask[i] == SeaIceKind.Seasonal) { seasonal += lat; ns++; } else { perennial += lat; np++; }
        }
        Assert.True(ns > 0 && np > 0, $"seasonal {ns}, perennial {np}");
        Assert.True(perennial / np > seasonal / ns);
    }

    [Fact]
    public void Mask_IsDeterministic()
    {
        using var a = new WorldBuilder().WithSize(3).WithSeed(11).Build();
        using var b = new WorldBuilder().WithSize(3).WithSeed(11).Build();
        Assert.Equal(SeaIce.Mask(a.Map.DataStore), SeaIce.Mask(b.Map.DataStore));
    }

    [Fact]
    public void Fish_ShrinkUnderIce()
    {
        LandSite Site(SeaIceKind ice) => new(BiomeType.Ocean, Sea, 0.25, 0.6, 0, false, false, false, 0, true, ice);
        byte open = LandCapacity.Evaluate(Site(SeaIceKind.None)).Fish;
        byte seasonal = LandCapacity.Evaluate(Site(SeaIceKind.Seasonal)).Fish;
        byte pack = LandCapacity.Evaluate(Site(SeaIceKind.Perennial)).Fish;
        Assert.True(open > seasonal && seasonal > pack && pack > 0, $"{open} {seasonal} {pack}");
    }
}
