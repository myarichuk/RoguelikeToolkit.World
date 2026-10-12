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

    private static readonly BiomeType[] DesertFamily =
        { BiomeType.Desert, BiomeType.Badlands, BiomeType.RockDesert, BiomeType.SaltFlat, BiomeType.ColdDesert };

    [Fact]
    public void ColorFor_NoBiomeFallsThroughToTheGreyDefault()
    {
        // The in-range test above passes for the fallback too, so a biome added without a color goes unnoticed.
        var fallback = new System.Numerics.Vector3(0.5f, 0.5f, 0.5f);
        foreach (BiomeType biome in System.Enum.GetValues<BiomeType>())
            Assert.NotEqual(fallback, BiomePalette.ColorFor(biome));
    }

    [Fact]
    public void ColorFor_DesertTypesAreTellableApart_OnTheBiomeMap()
    {
        for (int a = 0; a < DesertFamily.Length; a++)
            for (int b = a + 1; b < DesertFamily.Length; b++)
            {
                var d = System.Numerics.Vector3.Distance(BiomePalette.ColorFor(DesertFamily[a]), BiomePalette.ColorFor(DesertFamily[b]));
                Assert.True(d > 0.12, $"{DesertFamily[a]} and {DesertFamily[b]} are only {d:F3} apart");
            }
    }

    [Fact]
    public void TerrainShading_DesertTypesAreDistinct_AndNoneIsTheGenericLandFallback()
    {
        System.Numerics.Vector3 Shade(BiomeType biome)
            => TerrainShading.ColorFor(biome, 0.2f, false, 0f, 0f, null, false, false, 0.2f, 0.8f, 21);
        var generic = Shade(BiomeType.Plains);
        for (int a = 0; a < DesertFamily.Length; a++)
        {
            Assert.True(System.Numerics.Vector3.Distance(Shade(DesertFamily[a]), generic) > 0.1, $"{DesertFamily[a]} shades like plains");
            for (int b = a + 1; b < DesertFamily.Length; b++)
                Assert.True(System.Numerics.Vector3.Distance(Shade(DesertFamily[a]), Shade(DesertFamily[b])) > 0.05,
                    $"{DesertFamily[a]} and {DesertFamily[b]} shade alike");
        }
    }
}
