using System.Numerics;
using Xunit;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests;

/// <summary>
/// Relief tests (backlog D1-D4): displaced face normals, two-sided
/// hillshade, depression exaggeration / bathymetry parity, umber cue.
/// </summary>
public class ReliefTests
{
    [Fact]
    public void FaceNormal_UnitTrianglePointsUp()
    {
        var n = TerrainNormals.FaceNormal(
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(0, 0, 1));
        Assert.Equal(0f, n.X, 5);
        Assert.Equal(0f, n.Y, 5);
        Assert.Equal(1f, n.Z, 5);
    }

    [Fact]
    public void FaceNormal_DisplacedPeakTiltsAwayFromHighCorner()
    {
        var n = TerrainNormals.FaceNormal(
            new Vector3(0, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 1, 0),
            new Vector3(0, 0, 1));
        Assert.Equal(1f, n.Length(), 4);
        Assert.True(n.Z > 0.5f, "relief normal keeps an outward component");
        Assert.True(n.X < -0.1f, "relief normal tilts away from the raised corner");
    }

    [Fact]
    public void FaceNormal_DegenerateReturnsFallbackWithoutNaN()
    {
        var fallback = new Vector3(0, 0, 1);
        var p = new Vector3(1, 2, 3);
        var n = TerrainNormals.FaceNormal(p, p, p, fallback);
        Assert.Equal(fallback, n);
        Assert.False(float.IsNaN(n.X + n.Y + n.Z));
    }

    [Fact]
    public void Outward_FlipsInwardNormalKeepsOutward()
    {
        var centroid = new Vector3(0, 0, 1);
        Assert.Equal(new Vector3(0, 0, 1),
            TerrainNormals.Outward(new Vector3(0, 0, -1), centroid));
        Assert.Equal(new Vector3(0, 0, 1),
            TerrainNormals.Outward(new Vector3(0, 0, 1), centroid));
    }

    [Fact]
    public void ReliefShadeFactor_FlatIsOne()
    {
        Assert.Equal(1f, TerrainShading.ReliefShadeFactor(0f));
    }

    [Fact]
    public void ReliefShadeFactor_PitDarkensPeakBrightens()
    {
        Assert.True(TerrainShading.ReliefShadeFactor(-0.2f) < 1f, "pit must darken");
        Assert.True(TerrainShading.ReliefShadeFactor(0.2f) > 1f, "peak must brighten");
    }

    [Fact]
    public void ReliefShadeFactor_ClampsBothSides()
    {
        Assert.Equal(0.5f, TerrainShading.ReliefShadeFactor(-10f));
        Assert.Equal(1.25f, TerrainShading.ReliefShadeFactor(10f));
    }

    [Fact]
    public void DepressionCue_FlatGroundUnchanged()
    {
        var green = new Vector3(0.2f, 0.5f, 0.2f);
        Assert.Equal(green, TerrainShading.DepressionCue(green, 0f));
        Assert.Equal(green, TerrainShading.DepressionCue(green, 0.1f));
    }

    [Fact]
    public void DepressionCue_PitShiftsTowardUmberNotCanyon()
    {
        var green = new Vector3(0.2f, 0.5f, 0.2f);
        var umber = new Vector3(0.35f, 0.22f, 0.12f);
        var canyon = new Vector3(0.62f, 0.34f, 0.18f);
        var cued = TerrainShading.DepressionCue(green, -0.1f);
        Assert.NotEqual(green, cued);
        Assert.True((cued - umber).Length() < (green - umber).Length(),
            "cue must move the color toward umber");
        Assert.True((cued - canyon).Length() > 0.15f,
            "cue must stay distinct from the Canyon biome color");
    }

    [Fact]
    public void DisplacedRadius_DefaultsPreserveOldFormula()
    {
        Assert.Equal(1f + (-0.5f) * 0.025f,
            TerrainShading.DisplacedRadius(-0.5f, false, 0f, 0f));
        Assert.Equal(1f + 0.5f * TerrainShading.DefaultHeightScale,
            TerrainShading.DisplacedRadius(0.5f, false, 0f, 0f));
    }

    [Fact]
    public void DisplacedRadius_BathymetryParityDeepensOcean()
    {
        float def = TerrainShading.DisplacedRadius(-0.5f, false, 0f, 0f);
        float parity = TerrainShading.DisplacedRadius(-0.5f, false, 0f, 0f,
            TerrainShading.DefaultHeightScale, 1f, TerrainShading.DefaultHeightScale);
        Assert.True(parity < def, "parity slope must sink the ocean floor further");
    }

    [Fact]
    public void DisplacedRadius_DepressionScaleExaggerates()
    {
        float def = TerrainShading.DisplacedRadius(-0.5f, false, 0f, 0f);
        float ex = TerrainShading.DisplacedRadius(-0.5f, false, 0f, 0f,
            TerrainShading.DefaultHeightScale, 2f);
        Assert.True(ex < def, "exaggeration must deepen below-sea displacement");
        float land = TerrainShading.DisplacedRadius(0.5f, false, 0f, 0f,
            TerrainShading.DefaultHeightScale, 2f);
        Assert.Equal(1f + 0.5f * TerrainShading.DefaultHeightScale, land, 6);
    }
}
