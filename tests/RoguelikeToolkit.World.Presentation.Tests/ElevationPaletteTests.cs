using Xunit;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class ElevationPaletteTests
{
    [Fact]
    public void ColorFor_SameHeightReturnsSameColor()
    {
        Assert.Equal(ElevationPalette.ColorFor(0.3f), ElevationPalette.ColorFor(0.3f));
    }

    [Fact]
    public void ColorFor_DeepOceanIsBluerThanPeak()
    {
        var ocean = ElevationPalette.ColorFor(-1f);
        var peak = ElevationPalette.ColorFor(1f);
        Assert.True(ocean.Z > ocean.X, "Deep ocean should be blue-dominant");
        Assert.True(peak.X > 0.8f && peak.Y > 0.8f && peak.Z > 0.8f, "Peaks should be near-white");
    }

    [Fact]
    public void ColorFor_ClampsOutOfRangeHeights()
    {
        Assert.Equal(ElevationPalette.ColorFor(-1f), ElevationPalette.ColorFor(-5f));
        Assert.Equal(ElevationPalette.ColorFor(1f), ElevationPalette.ColorFor(5f));
    }
}
