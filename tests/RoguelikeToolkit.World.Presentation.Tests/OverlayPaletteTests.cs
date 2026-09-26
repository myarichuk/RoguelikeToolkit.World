using Xunit;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class ClimatePaletteTests
{
    [Fact]
    public void ColorFor_StaysInRange()
    {
        foreach (float t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            foreach (float p in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                var c = ClimatePalette.ColorFor(t, p);
                Assert.InRange(c.X, 0f, 1f);
                Assert.InRange(c.Y, 0f, 1f);
                Assert.InRange(c.Z, 0f, 1f);
            }
        }
    }

    [Fact]
    public void ColorFor_DesertIsReddishAndTropicsGreen()
    {
        var desert = ClimatePalette.ColorFor(0.9f, 0.1f);
        Assert.True(desert.X > desert.Y && desert.X > desert.Z);

        var polar = ClimatePalette.ColorFor(0.05f, 0.1f);
        Assert.True(polar.Z > polar.X);

        var tropics = ClimatePalette.ColorFor(0.8f, 0.9f);
        Assert.True(tropics.Y > 0.5f);
    }
}

public class DepositPaletteTests
{
    [Fact]
    public void ColorFor_MapsEveryDepositType()
    {
        foreach (DepositType type in System.Enum.GetValues<DepositType>())
        {
            var c = DepositPalette.ColorFor(type);
            Assert.InRange(c.X, 0f, 1f);
            Assert.InRange(c.Y, 0f, 1f);
            Assert.InRange(c.Z, 0f, 1f);
        }
    }

    [Fact]
    public void ColorFor_TypesAreVisuallyDistinct()
    {
        var seen = new System.Collections.Generic.HashSet<System.Numerics.Vector3>();
        foreach (DepositType type in System.Enum.GetValues<DepositType>())
            seen.Add(DepositPalette.ColorFor(type));
        Assert.Equal(System.Enum.GetValues<DepositType>().Length, seen.Count);
    }
}

public class HydroPaletteFlowTests
{
    [Fact]
    public void ColorForRiver_BrightensWithDischarge()
    {
        var head = HydroPalette.ColorForRiver(6f);
        var mouth = HydroPalette.ColorForRiver(150f);
        Assert.True(mouth.X + mouth.Y + mouth.Z > head.X + head.Y + head.Z);
        Assert.True(mouth.Z > mouth.X, "River stays blue-dominant");
    }
}
