using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Lithosphere;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Tuning properties fail where they are assigned: NaN would poison every tile, a negative count would skip a pass silently.</summary>
public sealed class StageKnobTests
{
    public static TheoryData<string, Action<double>> DoubleKnobs => new()
    {
        { "Erosion.Rate", v => new ErosionGenerationStage().Rate = v },
        { "Erosion.HydraulicRate", v => new ErosionGenerationStage().HydraulicRate = v },
        { "Erosion.Talus", v => new ErosionGenerationStage().Talus = v },
        { "Climate.MeridionalWind", v => new ClimateStage().MeridionalWind = v },
        { "Climate.LapseRate", v => new ClimateStage().LapseRate = v },
        { "Elevation.NoiseAmplitude", v => new ElevationGenerationStage().NoiseAmplitude = v },
        { "IslandChain.GapDepth", v => new IslandChainOptions().GapDepth = v },
    };

    [Theory]
    [MemberData(nameof(DoubleKnobs))]
    public void NonFiniteDoubles_AreRejected(string knob, Action<double> set)
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => set(bad));
            Assert.True(ex.Message.Contains("finite"), $"{knob}: {ex.Message}");
        }
        set(0.5);   // a normal value still goes through
    }

    [Fact]
    public void NonFiniteFloats_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HydrologyStage().RiverThresholdScale = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErosionGenerationStage().RiverThresholdScale = float.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BiomeGenerationStage().RiverThresholdScale = float.NegativeInfinity);
    }

    [Fact]
    public void NegativeCounts_AreRejected_AndZeroStillMeansOff()
    {
        var erosion = new ErosionGenerationStage();
        Assert.Throws<ArgumentOutOfRangeException>(() => erosion.Iterations = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => erosion.HydraulicIterations = -3);
        Assert.Throws<ArgumentOutOfRangeException>(() => erosion.AeolianIterations = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new HydrologyStage().MaxRivers = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ElevationGenerationStage().HotspotCount = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new IslandChainOptions().MinLength = -1);

        erosion.Iterations = 0;
        erosion.GlacialIterations = 0;
        Assert.Equal(0, erosion.Iterations);
    }

    [Fact]
    public void ARejectedAssignment_LeavesThePreviousValue()
    {
        var erosion = new ErosionGenerationStage { Rate = 0.4 };
        Assert.Throws<ArgumentOutOfRangeException>(() => erosion.Rate = double.NaN);
        Assert.Equal(0.4, erosion.Rate);
    }

    [Fact]
    public void TheBuilderConfigureCallbacks_SurfaceTheError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldBuilder().WithSize(1).WithErosion(e => e.Rate = double.NaN).Build());
    }

    [Fact]
    public void Defaults_AreUnchanged()
    {
        var e = new ErosionGenerationStage();
        Assert.Equal(10, e.Iterations);
        Assert.Equal(0.25, e.Rate);
        Assert.Equal(0.03, e.Talus);
        Assert.Equal(1.0f, e.RiverThresholdScale);
        Assert.Equal(0.92f, new HydrologyStage().RiverThresholdScale);
        Assert.Equal(256, new HydrologyStage().MaxRivers);
    }
}
