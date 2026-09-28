using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// E6 smoothing guardrails (backlog E6): the pre-orographic base blur must
/// reduce same-side neighbor speckle without flattening the rain shadow,
/// and stay deterministic. Absolute acceptance numbers (shadow Δ > 0 on the
/// matrix, determinism) live in the probe-verified TODO log; these tests pin
/// the behavior on fast builds.
/// </summary>
public class ClimateSmoothingTests
{
    private static double SameSideSpeckle(World world)
    {
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var clim = store.GetSpan<ClimateInfo>();
        var vec = store.GetTileVectors();
        var side = new int[store.TileCount];
        Span<int> nb = stackalloc int[6];
        for (int i = 0; i < store.TileCount; i++)
        {
            side[i] = -9;
            if (elev[i].Height < 0f) continue;
            var wind = new Vector3D(clim[i].WindX, clim[i].WindY, clim[i].WindZ);
            int adj = store.GetAdjacent(i, nb);
            int upwind = -1;
            double bestUp = double.NegativeInfinity;
            for (int k = 0; k < adj; k++)
            {
                int j = nb[k];
                var tang = (vec[j] - vec[i]).Normalize();
                double up = -Vector3D.Dot(tang, wind);
                if (up > bestUp) { bestUp = up; upwind = j; }
            }
            if (upwind < 0 || bestUp <= 0.15) { side[i] = 0; continue; }
            double dh = elev[i].Height - elev[upwind].Height;
            side[i] = dh > 0.08 ? 1 : dh < -0.08 ? -1 : 0;
        }
        double sum = 0;
        long n = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            if (side[i] == -9) continue;
            int adj = store.GetAdjacent(i, nb);
            for (int k = 0; k < adj; k++)
            {
                int j = nb[k];
                if (j <= i || side[j] == -9 || side[i] != side[j]) continue;
                sum += Math.Abs(clim[i].Precipitation - clim[j].Precipitation);
                n++;
            }
        }
        return sum / n;
    }

    [Fact]
    public void Smoothing_ReducesSameSideSpeckle()
    {
        using var smooth = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        using var rough = new WorldBuilder().WithSize(3).WithSeed(42)
            .WithClimate(c => c.PrecipSmoothingPasses = 0).Build();
        Assert.True(SameSideSpeckle(smooth) < SameSideSpeckle(rough),
            "base blur must reduce same-side neighbor precip deltas");
    }

    [Fact]
    public void Smoothing_PreservesRainShadowWithMargin()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var clim = store.GetSpan<ClimateInfo>();
        var vec = store.GetTileVectors();
        Span<int> nb = stackalloc int[6];
        double leeSum = 0, windSum = 0;
        int leeN = 0, windN = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            if (elev[i].Height < 0f) continue;
            var wind = new Vector3D(clim[i].WindX, clim[i].WindY, clim[i].WindZ);
            int adj = store.GetAdjacent(i, nb);
            int upwind = -1;
            double bestUp = double.NegativeInfinity;
            for (int k = 0; k < adj; k++)
            {
                int j = nb[k];
                var tang = (vec[j] - vec[i]).Normalize();
                double up = -Vector3D.Dot(tang, wind);
                if (up > bestUp) { bestUp = up; upwind = j; }
            }
            if (upwind < 0 || bestUp <= 0.15) continue;
            double dh = elev[i].Height - elev[upwind].Height;
            if (dh > 0.08) { windSum += clim[i].Precipitation; windN++; }
            else if (dh < -0.08) { leeSum += clim[i].Precipitation; leeN++; }
        }
        Assert.True(windN > 0 && leeN > 0, "need both faces");
        Assert.True(windSum / windN - leeSum / leeN > 0.05,
            "smoothed shadow must keep a comfortable margin, not just Δ > 0");
    }

    [Fact]
    public void Smoothing_IsDeterministic()
    {
        using var first = new WorldBuilder().WithSize(2).WithSeed(42).Build();
        using var second = new WorldBuilder().WithSize(2).WithSeed(42).Build();
        var p1 = first.Map.DataStore.GetSpan<ClimateInfo>();
        var p2 = second.Map.DataStore.GetSpan<ClimateInfo>();
        Assert.Equal(p1.Length, p2.Length);
        for (int i = 0; i < p1.Length; i++)
            Assert.Equal(p1[i].Precipitation, p2[i].Precipitation);
    }
}
