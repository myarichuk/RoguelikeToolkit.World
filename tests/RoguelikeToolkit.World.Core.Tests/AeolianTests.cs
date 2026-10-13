using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Wind erosion (<see cref="ErosionGenerationStage.AeolianIterations"/>), pinned on a hand-built map: gently
/// rolling land, a constant easterly wind, dryness set per tile. The stage runs alone so the only mover is the wind.
/// </summary>
public class AeolianTests
{
    private static WorldMap Map(float precip, float temperature = 0.6f, bool edits = false, double windSpeed = 1.0)
    {
        var map = new WorldMap(3);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        if (edits) map.RegisterLayer<UserEditInfo>(new UserEditLayer(map.DataStore));
        map.DataStore.Allocate();
        var v = map.DataStore.GetTileVectors();
        var elev = map.DataStore.GetSpan<ElevationInfo>();
        var clim = map.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < elev.Length; i++)
        {
            elev[i].Height = 0.2f + 0.05f * (float)Math.Sin(i * 0.37);
            var east = new Vector3D(-v[i].Y, v[i].X, 0.0);
            double len = Math.Sqrt(east.X * east.X + east.Y * east.Y);
            if (len > 1e-9) east = new Vector3D(east.X / len, east.Y / len, 0.0);
            clim[i] = new ClimateInfo { Precipitation = precip, Temperature = temperature, WindX = (float)(east.X * windSpeed), WindY = (float)(east.Y * windSpeed), WindZ = 0f };
        }
        return map;
    }

    private static float[] Run(WorldMap map, int aeolian)
    {
        new ErosionGenerationStage { Iterations = 1, DiffusionIterations = 0, GlacialIterations = 0, HydraulicIterations = 0, AeolianIterations = aeolian }.Execute(map);
        var e = map.DataStore.GetSpan<ElevationInfo>();
        var h = new float[e.Length];
        for (int i = 0; i < h.Length; i++) h[i] = e[i].Height;
        return h;
    }

    private static double Sum(float[] h) { double s = 0; foreach (var x in h) s += x; return s; }

    [Fact]
    public void Off_LeavesTheSurfaceExactlyAsItWas_AndOnDoesNot()
    {
        using var a = Map(0.05f); using var c = Map(0.05f);
        foreach (var m in new[] { a, c }) { var e = m.DataStore.GetSpan<ElevationInfo>(); for (int i = 0; i < e.Length; i++) e[i].Height = 0.2f; }   // dead flat: nothing but the wind can move it
        var start = new float[a.DataStore.TileCount];
        Array.Fill(start, 0.2f);
        Assert.Equal(start, Run(a, 0));
        Assert.NotEqual(start, Run(c, 4));
    }

    [Fact]
    public void DryLand_LosesSomeGroundAndGainsSomeElsewhere_AndMassIsConserved()
    {
        using var off = Map(0.05f); using var on = Map(0.05f);
        var before = Run(off, 0); var after = Run(on, 6);
        double lowered = 0, raised = 0;
        for (int i = 0; i < before.Length; i++) { double d = after[i] - before[i]; if (d < 0) lowered -= d; else raised += d; }
        Assert.True(lowered > 0.01 && raised > 0.01, $"lowered {lowered}, raised {raised}");
        Assert.Equal(Sum(before), Sum(after), 3);
        Assert.Equal(lowered, raised, 3);
    }

    [Fact]
    public void NoTileIsLoweredPastTheDeflationCap()
    {
        using var off = Map(0.05f); using var on = Map(0.05f);
        var before = Run(off, 0);
        var after = Run(on, 50);
        for (int i = 0; i < before.Length; i++) Assert.True(before[i] - after[i] <= 0.03 + 1e-5, $"tile {i} lost {before[i] - after[i]}");
    }

    [Theory]
    [InlineData(0.9f, 0.6f)]   // wet: vegetation holds the soil
    [InlineData(0.05f, 0.0f)]  // frozen: the ground is not loose
    public void WetOrFrozenGround_IsLeftAlone(float precip, float temperature)
    {
        using var off = Map(precip, temperature); using var on = Map(precip, temperature);
        Assert.Equal(Run(off, 0), Run(on, 6));
    }

    [Fact]
    public void IsDeterministic()
    {
        using var a = Map(0.05f); using var b = Map(0.05f);
        Assert.Equal(Run(a, 6), Run(b, 6));
    }

    [Fact]
    public void APinnedTile_NeitherDeflatesNorKeepsDeposits()
    {
        using var map = Map(0.05f, edits: true);
        var edits = map.DataStore.GetSpan<UserEditInfo>();
        for (int i = 0; i < edits.Length; i += 3) { edits[i].Flags = UserEditFlags.PinHeight; edits[i].PinHeight = 0.25f; }
        var h = Run(map, 8);
        for (int i = 0; i < h.Length; i += 3) Assert.Equal(0.25f, h[i]);
    }

    private static double Lowered(float[] before, float[] after)
    {
        double lowered = 0;
        for (int i = 0; i < before.Length; i++) if (after[i] < before[i]) lowered += before[i] - after[i];
        return lowered;
    }

    /// <summary>
    /// The wind vector's length is the lift: a calm (what <see cref="ClimateStage.WindCellBlend"/> makes of the horse
    /// latitudes) strips less, and below the 0.15 carrying threshold nothing moves at all. Measured on full worlds,
    /// a full blend cuts the sand moved by 39-56%, so the toggle is not free for the dune belts.
    /// </summary>
    [Fact]
    public void ACalmerWind_LiftsLess_AndBelowTheCarryingThresholdNothing()
    {
        using var off = Map(0.05f); using var full = Map(0.05f); using var half = Map(0.05f, windSpeed: 0.5); using var calm = Map(0.05f, windSpeed: 0.1);
        var before = Run(off, 0);
        double strong = Lowered(before, Run(full, 6));
        double slow = Lowered(before, Run(half, 6));
        Assert.True(strong > 0.01, $"positive control: unit wind lowered {strong}");
        Assert.True(slow > 0 && slow < strong, $"half-speed wind lowered {slow}, unit wind {strong}");
        Assert.Equal(before, Run(calm, 6));
    }
}
