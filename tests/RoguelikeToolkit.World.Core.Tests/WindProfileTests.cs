using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// <see cref="ClimateStage.WindCellBlend"/>: the legacy wind is unit speed with a hard reversal at the cell edges;
/// the blended one fades to a calm there and keeps each cell's direction.
/// </summary>
public class WindProfileTests
{
    private static World Build(double blend)
        => new WorldBuilder().WithSize(4).WithSeed(42).WithClimate(c => c.WindCellBlend = blend).Build();

    private static (double Lat, double Speed, Vector3D Wind)[] Winds(World w)
    {
        var store = w.Map.DataStore;
        var climate = store.GetSpan<ClimateInfo>();
        var vectors = store.GetTileVectors();
        var r = new (double, double, Vector3D)[store.TileCount];
        for (int i = 0; i < r.Length; i++)
        {
            var v = new Vector3D(climate[i].WindX, climate[i].WindY, climate[i].WindZ);
            r[i] = (Math.Abs(vectors[i].ToGeoCoord().Latitude), v.Length, v);
        }
        return r;
    }

    [Fact]
    public void DefaultKeepsUnitSpeedEverywhere()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        foreach (var (lat, speed, _) in Winds(w))
            Assert.True(Math.Abs(speed - 1.0) < 1e-3, $"lat {lat:F1}: speed {speed}");
    }

    [Fact]
    public void FullBlend_CalmAtTheCellEdges_StrongMidCell()
    {
        using var w = Build(1.0);
        int edges = 0, mids = 0;
        foreach (var (lat, speed, _) in Winds(w))
        {
            if (Math.Abs(lat - 30) < 2 || Math.Abs(lat - 60) < 2) { edges++; Assert.True(speed < 0.25, $"lat {lat:F1}: speed {speed}"); }
            if (Math.Abs(lat - 15) < 3 || Math.Abs(lat - 45) < 3 || Math.Abs(lat - 75) < 3) { mids++; Assert.True(speed > 0.9, $"lat {lat:F1}: speed {speed}"); }
        }
        Assert.True(edges > 0 && mids > 0, "the sampled world has tiles at the edges and mid-cell");
    }

    [Fact]
    public void Blend_KeepsEachCellsDirection_AndScalesBetweenTheTwo()
    {
        using var legacy = Build(0.0);
        using var half = Build(0.5);
        var a = Winds(legacy); var b = Winds(half);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.True(Vector3D.Dot(a[i].Wind.Normalize(), b[i].Wind) >= 0.0, $"tile {i}: direction flipped");
            Assert.InRange(b[i].Speed, 0.5 - 1e-3, 1.0 + 1e-3); // 1 - 0.5 + 0.5 * [0,1]
        }
    }

    [Fact]
    public void Blend_IsDeterministic()
    {
        using var x = Build(0.7); using var y = Build(0.7);
        var a = Winds(x); var b = Winds(y);
        for (int i = 0; i < a.Length; i++) Assert.Equal(a[i].Wind, b[i].Wind);
    }
}
