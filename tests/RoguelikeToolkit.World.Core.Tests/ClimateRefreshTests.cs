using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// E3 hydro-aware climate refresh (backlog E3): rerunning ClimateStage after
/// hydrology has run picks up lake adjacency (+0.06 water bonus) where the
/// first pass saw un-run zeros. Mechanism-pinned on a hand-built flat
/// all-land map, so the lake bonus isolates exactly (no ocean fallback,
/// no relief, no smoothing).
/// </summary>
public class ClimateRefreshTests
{
    private static WorldMap FlatLandMap()
    {
        var map = new WorldMap(2);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.DataStore.Allocate();
        var elev = map.DataStore.GetSpan<ElevationInfo>();
        for (int i = 0; i < elev.Length; i++)
            elev[i] = new ElevationInfo { Height = 0.3f };
        // Mirror HydrologyStage semantics: dry tiles carry WaterBodyId -1,
        // so only explicitly marked water reads as near-water (a default 0
        // would fake water everywhere, the E1 trap).
        var hydro = map.DataStore.GetSpan<HydrologyInfo>();
        for (int i = 0; i < hydro.Length; i++)
            hydro[i] = new HydrologyInfo { WaterBodyId = -1 };
        return map;
    }

    private static float[] SnapshotPrecip(WorldMap map)
    {
        var clim = map.DataStore.GetSpan<ClimateInfo>();
        var p = new float[clim.Length];
        for (int i = 0; i < p.Length; i++) p[i] = clim[i].Precipitation;
        return p;
    }

    [Fact]
    public void RefreshAfterHydrology_FeedsLakeMoistureOnlyToLakeRing()
    {
        using var map = FlatLandMap();
        var stage = new ClimateStage(7) { PrecipSmoothingPasses = 0 };
        stage.Execute(map);
        float[] before = SnapshotPrecip(map);

        // Mark tile 0 a lake (WaterBodyId set + nonzero flow/surface = ran).
        var hydro = map.DataStore.GetSpan<HydrologyInfo>();
        var lake = hydro[0];
        lake.WaterBodyId = 3;
        lake.Flow = 1f;
        lake.Surface = 1f;
        hydro[0] = lake;

        stage.Execute(map);
        float[] after = SnapshotPrecip(map);

        Span<int> ring = stackalloc int[6];
        int adjacent = map.DataStore.GetAdjacent(0, ring);
        var ringSet = new System.Collections.Generic.HashSet<int>();
        for (int k = 0; k < adjacent; k++) ringSet.Add(ring[k]);

        int exactGains = 0;
        for (int i = 0; i < after.Length; i++)
        {
            if (i == 0 || ringSet.Contains(i))
            {
                double gain = after[i] - before[i];
                if (before[i] <= 0.9f)
                {
                    Assert.InRange(gain, 0.059, 0.061);
                    exactGains++;
                }
                else
                {
                    Assert.True(gain >= 0, $"lake-ring tile {i} lost moisture");
                }
            }
            else
            {
                Assert.Equal(before[i], after[i]);
            }
        }
        Assert.True(exactGains > 0, "expected measurable lake-fed tiles below the clamp");

        // A further refresh is a fixed point: same hydro + heights, same field.
        stage.Execute(map);
        float[] third = SnapshotPrecip(map);
        Assert.Equal(after, third);
    }
}
