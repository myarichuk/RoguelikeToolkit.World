using Xunit;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class BiomePaletteTests
{
    [Fact]
    public void ColorFor_MapsEveryBiomeType()
    {
        foreach (BiomeType biome in System.Enum.GetValues<BiomeType>())
        {
            var c = BiomePalette.ColorFor(biome);
            Assert.InRange(c.X, 0f, 1f);
            Assert.InRange(c.Y, 0f, 1f);
            Assert.InRange(c.Z, 0f, 1f);
        }
    }

    [Fact]
    public void ColorFor_OceanIsBluish()
    {
        var c = BiomePalette.ColorFor(BiomeType.Ocean);
        Assert.True(c.Z > c.X, "Ocean should be blue-dominant");
    }

    [Fact]
    public void ColorFor_TundraDistinctFromGlacier()
    {
        // Backlog E5: tundra (cold khaki land) must not read as near-white ice.
        var tundra = BiomePalette.ColorFor(BiomeType.Tundra);
        var glacier = BiomePalette.ColorFor(BiomeType.Glacier);
        Assert.True(System.Numerics.Vector3.Distance(tundra, glacier) > 0.25,
            $"Tundra {tundra} too close to Glacier {glacier}");
    }
}
