using Xunit;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class TerrainShadingTests
{
    [Fact]
    public void DisplacedRadius_ReliefOrdersOceanPlainMountain()
    {
        float ocean = TerrainShading.DisplacedRadius(-0.5f, false, 0f, 0f, 0.16f);
        float plain = TerrainShading.DisplacedRadius(0.2f, false, 0f, 0f, 0.16f);
        float mountain = TerrainShading.DisplacedRadius(0.8f, false, 0f, 0f, 0.16f);

        Assert.True(ocean < 1f, "ocean floor should sit below the unit sphere");
        Assert.True(plain > 1f, "land should rise above the unit sphere");
        Assert.True(mountain > plain, "higher ground should displace further");
    }

    [Fact]
    public void DisplacedRadius_RiverCarvesChannel()
    {
        float dry = TerrainShading.DisplacedRadius(0.3f, false, 0f, 0f, 0.16f);
        float river = TerrainShading.DisplacedRadius(0.3f, true, 10f, 0f, 0.16f);
        Assert.True(river < dry, "river channel should carve below the banks");
    }

    [Fact]
    public void DisplacedRadius_IsDeterministicAndBounded()
    {
        float a = TerrainShading.DisplacedRadius(0.6f, true, 5f, 0.2f, 0.16f);
        float b = TerrainShading.DisplacedRadius(0.6f, true, 5f, 0.2f, 0.16f);
        Assert.Equal(a, b);
        Assert.InRange(a, 0.93f, 1.38f);
        Assert.InRange(TerrainShading.DisplacedRadius(-1.2f, false, 0f, 0f, 0.16f), 0.93f, 1.38f);
    }

    [Fact]
    public void ColorFor_OceanDepthRampGoesDeepBlue()
    {
        var shallow = TerrainShading.ColorFor(BiomeType.Ocean, -0.05f, false, 0f, 0f,
            WaterBodyKind.Ocean, false, false, 0.7f, 0.5f, 11);
        var deep = TerrainShading.ColorFor(BiomeType.Ocean, -1.0f, false, 0f, 0f,
            WaterBodyKind.Ocean, false, false, 0.7f, 0.5f, 11);

        Assert.True(shallow.Z > 0.4f, "ocean should be blue");
        Assert.True(deep.X + deep.Y + deep.Z < shallow.X + shallow.Y + shallow.Z,
            "deep water should be darker than shallow");
    }

    [Fact]
    public void ColorFor_RiverTileIsWaterBlueOnLand()
    {
        var river = TerrainShading.ColorFor(BiomeType.Plains, 0.3f, true, 20f, 0f,
            null, false, false, 0.6f, 0.6f, 21);
        var land = TerrainShading.ColorFor(BiomeType.Plains, 0.3f, false, 0f, 0f,
            null, false, false, 0.6f, 0.6f, 21);

        Assert.True(river.Z > river.X, "river should read blue");
        Assert.NotEqual(land, river);
    }

    [Fact]
    public void ColorFor_JungleIsLushGreenDistinctFromPlains()
    {
        var jungle = TerrainShading.ColorFor(BiomeType.Jungle, 0.25f, false, 0f, 0f,
            null, false, false, 0.9f, 0.9f, 31);
        var plains = TerrainShading.ColorFor(BiomeType.Plains, 0.25f, false, 0f, 0f,
            null, false, false, 0.6f, 0.4f, 32);

        Assert.True(jungle.Y > jungle.X, "jungle should be green-dominant");
        Assert.NotEqual(plains, jungle);
    }

    [Fact]
    public void ColorFor_HighElevationIsSnow()
    {
        var snow = TerrainShading.ColorFor(BiomeType.Mountain, 0.9f, false, 0f, 0f,
            null, false, false, 0.4f, 0.4f, 41);
        Assert.True(snow.X > 0.8f && snow.Y > 0.8f && snow.Z > 0.8f);
    }

    [Fact]
    public void ColorFor_GlacierIsIce()
    {
        var ice = TerrainShading.ColorFor(BiomeType.Tundra, 0.4f, false, 0f, 0f,
            null, true, false, 0.05f, 0.3f, 51);
        Assert.True(ice.Z >= ice.X && ice.Y > 0.8f);
    }

    [Fact]
    public void ColorFor_LakeIsBlue()
    {
        var lake = TerrainShading.ColorFor(BiomeType.Plains, 0.2f, false, 0f, 0.5f,
            WaterBodyKind.Lake, false, false, 0.5f, 0.5f, 61);
        Assert.True(lake.Z > lake.X, "lake should read blue");
    }

    [Fact]
    public void ColorFor_IsDeterministic()
    {
        var a = TerrainShading.ColorFor(BiomeType.Forest, 0.3f, false, 0f, 0f,
            null, false, false, 0.6f, 0.7f, 71);
        var b = TerrainShading.ColorFor(BiomeType.Forest, 0.3f, false, 0f, 0f,
            null, false, false, 0.6f, 0.7f, 71);
        Assert.Equal(a, b);
    }
}
