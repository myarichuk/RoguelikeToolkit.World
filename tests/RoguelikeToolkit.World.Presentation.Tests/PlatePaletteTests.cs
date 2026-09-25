using Xunit;
using System.Numerics;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class PlatePaletteTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    public void ColorFor_SameIdReturnsSameColor(int plateId)
    {
        Assert.Equal(PlatePalette.ColorFor(plateId), PlatePalette.ColorFor(plateId));
    }

    [Fact]
    public void ColorFor_ChannelsStayInVisibleRange()
    {
        for (int id = -5; id < 200; id++)
        {
            Vector3 c = PlatePalette.ColorFor(id);
            Assert.InRange(c.X, 0.2f, 1.0f);
            Assert.InRange(c.Y, 0.2f, 1.0f);
            Assert.InRange(c.Z, 0.2f, 1.0f);
        }
    }

    [Fact]
    public void ColorFor_DistinctIdsProduceVariedPalette()
    {
        var seen = new System.Collections.Generic.HashSet<Vector3>();
        for (int id = 0; id < 100; id++)
            seen.Add(PlatePalette.ColorFor(id));

        Assert.True(seen.Count >= 90, $"Only {seen.Count}/100 distinct colors");
    }
}
