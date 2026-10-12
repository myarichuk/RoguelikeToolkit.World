using System;
using RoguelikeToolkit.World.MapEditor.Engine;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

/// <summary>
/// The exports are a wire format: flat typed arrays whose layout the TypeScript side indexes by hand
/// (<c>c[i * 12 + 6]</c>). A length or stride drifting here is silent corruption in the editor, so pin them.
/// </summary>
public class WorldExportsLayoutTests
{
    private static int Tiles() => WorldExports.Generate(7, 2, 0, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4);

    [Fact]
    public void PerTileArrays_HaveTheirDocumentedStride()
    {
        int n = Tiles();
        Assert.Equal(n * 3, WorldExports.TileVectors().Length);
        Assert.Equal(n, WorldExports.Heights().Length);
        Assert.Equal(n, WorldExports.Biomes().Length);
        Assert.Equal(n, WorldExports.Plates().Length);
        Assert.Equal(n, WorldExports.Flow().Length);
        Assert.Equal(n * 2, WorldExports.Climate().Length);
        Assert.Equal(n * 12, WorldExports.ClimateField().Length);
        Assert.Equal(n * 5, WorldExports.LandCapacity().Length);
        Assert.Equal(n * 3, WorldExports.LandUse().Length);
    }

    [Fact]
    public void TileVectors_AreUnitLength()
    {
        Tiles();
        var v = WorldExports.TileVectors();
        for (int i = 0; i < v.Length; i += 3)
            Assert.InRange(Math.Sqrt(v[i] * v[i] + v[i + 1] * v[i + 1] + v[i + 2] * v[i + 2]), 0.999, 1.001);
    }

    [Fact]
    public void Faces_AreTriplesOfValidTileIndices()
    {
        int n = Tiles();
        var f = WorldExports.Faces();
        Assert.True(f.Length > 0);
        Assert.Equal(0, f.Length % 3);
        foreach (var t in f) Assert.InRange(t, 0, n - 1);
    }

    [Fact]
    public void SeaIce_IsOneByteOfZeroToTwoPerTile_AndNeverOnLand()
    {
        int tiles = Tiles();
        var ice = WorldExports.SeaIce();
        var heights = WorldExports.Heights();
        Assert.Equal(tiles, ice.Length);
        for (int i = 0; i < ice.Length; i++)
        {
            Assert.InRange(ice[i], (byte)0, (byte)2);
            if (heights[i] >= 0.0) Assert.Equal(0, ice[i]);
        }
    }

    [Fact]
    public void Climate_IsTheLeadingPairOfClimateField()
    {
        int n = Tiles();
        var c = WorldExports.Climate();
        var f = WorldExports.ClimateField();
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(c[i * 2], f[i * 12]);
            Assert.Equal(c[i * 2 + 1], f[i * 12 + 1]);
        }
    }

    [Fact]
    public void ClimateField_EndsWithTheRainPhase_AndItsMagnitudeIsTheRainSeasonality()
    {
        int n = Tiles();
        var f = WorldExports.ClimateField();
        double widest = 0;
        bool sawWinter = false, sawSummer = false;
        for (int i = 0; i < n; i++)
        {
            double seasonality = f[i * 12 + 6], phase = f[i * 12 + 11];
            Assert.InRange(phase, -1.0, 1.0);
            Assert.Equal(seasonality, Math.Abs(phase), 5); // float32 storage on both columns
            widest = Math.Max(widest, Math.Abs(phase));
            sawWinter |= phase > 0.3; sawSummer |= phase < -0.3;
        }
        Assert.True(widest > 0.5, "an Earth-tilt world has strongly seasonal rain somewhere");
        Assert.True(sawWinter && sawSummer, "and both regimes: monsoon (summer) and Mediterranean (winter)");
    }

    [Fact]
    public void ClimateField_IsFinite()
    {
        Tiles();
        foreach (var x in WorldExports.ClimateField()) Assert.True(double.IsFinite(x));
    }

    [Fact]
    public void Rivers_CountMatchesTheRecordsThatFollow()
    {
        int n = Tiles();
        var a = WorldExports.Rivers();
        int pos = 1;
        for (int r = 0; r < a[0]; r++)
        {
            int len = a[pos + 3];
            for (int k = 0; k < len; k++) Assert.InRange(a[pos + 4 + k], 0, n - 1);
            pos += 4 + len;
        }
        Assert.Equal(a.Length, pos);
    }

    [Fact]
    public void WaterBodiesAndRanges_CountMatchesTheRecordsThatFollow()
    {
        int n = Tiles();
        foreach (var a in new[] { WorldExports.WaterBodies(), WorldExports.Ranges() })
        {
            int pos = 1;
            for (int r = 0; r < a[0]; r++)
            {
                int len = a[pos + 2];
                for (int k = 0; k < len; k++) Assert.InRange(a[pos + 3 + k], 0, n - 1);
                pos += 3 + len;
            }
            Assert.Equal(a.Length, pos);
        }
    }

    [Fact]
    public void Deposits_AreFourIntsEachAndPointAtRealTiles()
    {
        int n = Tiles();
        var d = WorldExports.Deposits();
        Assert.Equal(1 + d[0] * 4, d.Length);
        for (int i = 0; i < d[0]; i++) Assert.InRange(d[3 + i * 4], 0, n - 1);
    }

    [Fact]
    public void PlateDrift_IsFourDoublesPerPlate()
    {
        Tiles();
        Assert.Equal(0, WorldExports.PlateDrift().Length % 4);
        Assert.True(WorldExports.PlateDrift().Length > 0);
    }

    [Fact]
    public void CitySites_AreCappedSortedAndInRange()
    {
        int n = Tiles();
        var s = WorldExports.CitySites(5);
        Assert.Equal(0, s.Length % 2);
        Assert.True(s.Length / 2 <= 5);
        for (int i = 0; i < s.Length; i += 2)
        {
            Assert.InRange(s[i], 0, n - 1);
            if (i >= 2) Assert.True(s[i + 1] <= s[i - 1], "best first");
        }
    }

    [Fact]
    public void NameStyles_AreTabSeparatedTriples()
    {
        foreach (var s in WorldExports.NameStyles()) Assert.Equal(3, s.Split('\t').Length);
    }
}
