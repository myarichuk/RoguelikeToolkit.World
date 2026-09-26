using System.Numerics;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;
using Xunit;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class HydroPaletteTests
{
    [Fact]
    public void ColorFor_MapsEveryWaterBodyKind()
    {
        foreach (WaterBodyKind kind in System.Enum.GetValues<WaterBodyKind>())
        {
            var c = HydroPalette.ColorFor(kind);
            Assert.InRange(c.X, 0f, 1f);
            Assert.InRange(c.Y, 0f, 1f);
            Assert.InRange(c.Z, 0f, 1f);
        }
    }

    [Fact]
    public void WaterColors_AreBlueDominant()
    {
        foreach (WaterBodyKind kind in System.Enum.GetValues<WaterBodyKind>())
        {
            var c = HydroPalette.ColorFor(kind);
            Assert.True(c.Z > c.X, $"{kind} should be blue-dominant");
        }
        var river = HydroPalette.ColorForRiver();
        Assert.True(river.Z > river.X, "River should be blue-dominant");
    }

    [Fact]
    public void Kinds_AreVisuallyDistinct()
    {
        var ocean = HydroPalette.ColorFor(WaterBodyKind.Ocean);
        var sea = HydroPalette.ColorFor(WaterBodyKind.Sea);
        var lake = HydroPalette.ColorFor(WaterBodyKind.Lake);
        Assert.NotEqual(ocean, sea);
        Assert.NotEqual(sea, lake);
        // Depth ordering: ocean darkest, lake lightest.
        Assert.True(ocean.Z < sea.Z && sea.Z < lake.Z);
    }

    [Fact]
    public void DimLand_DarkensButKeepsHue()
    {
        var land = new Vector3(0.45f, 0.65f, 0.30f);
        var dim = HydroPalette.DimLand(land);
        Assert.Equal(land * 0.45f, dim);
    }
}
