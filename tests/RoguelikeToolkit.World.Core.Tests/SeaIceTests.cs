using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;

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
    [InlineData(0.14f, 0.10f, SeaIceKind.Perennial)]  // summer 0.24 is just under the pack line (0.28 - margin): pins PerennialTemperature
    [InlineData(0.22f, 0.10f, SeaIceKind.Seasonal)]   // summer 0.32 is just over it (0.28 + margin), winter 0.12 frozen
    [InlineData(0.20f, 0.20f, SeaIceKind.Seasonal)]   // summer 0.40 is above it, winter 0.00 is frozen
    public void Classify_FollowsWinterAndSummerTemperature(float temperature, float range, SeaIceKind expected)
        => Assert.Equal(expected, SeaIce.Classify(Pole, Sea, temperature, range));

    // The two lines, pinned tightly. The Theory above stays clear of the +-0.03 ragged margin, so it can only tell
    // the constants apart by more than that; this one computes the margin at the pole from the same noise and
    // straddles each line by 0.004. The numbers are literals on purpose: they are the calibration (see SeaIce's
    // remarks), and moving a constant must mean moving them here too.
    private static readonly double PoleMargin = SphereNoise.Fbm(Pole * 5.0, 7349) * 0.03;

    [Theory]
    [InlineData(-0.004, SeaIceKind.Seasonal)] // winter just under the freeze line: frozen
    [InlineData(+0.004, SeaIceKind.None)]     // winter just over it: open
    public void TheFreezeLine_IsWhereTheCalibrationPutsIt(double offset, SeaIceKind expected)
    {
        const float range = 0.20f; // a big swing keeps the summer (>= 0.6) open, so a frozen tile is seasonal and not the pack
        float winter = (float)(0.20 + PoleMargin + offset);
        Assert.Equal(expected, SeaIce.Classify(Pole, Sea, winter + range, range));
    }

    [Theory]
    [InlineData(-0.004, SeaIceKind.Perennial)] // summer just under the pack line: survives
    [InlineData(+0.004, SeaIceKind.Seasonal)]  // summer just over it: thaws
    public void ThePackLine_IsWhereTheCalibrationPutsIt(double offset, SeaIceKind expected)
    {
        const float range = 0.05f; // winter is 0.1 colder than summer, so it is frozen whatever the offset
        float summer = (float)(0.28 + PoleMargin + offset);
        Assert.Equal(expected, SeaIce.Classify(Pole, Sea, summer - range, range));
    }

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
