using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Hand-brushed climate: <see cref="UserEditInfo.TemperatureDelta"/> and <see cref="UserEditInfo.PrecipitationDelta"/> are
/// inputs the climate stage adds last, in both passes. The geometry tests use a synthetic fixed-relief world
/// (no erosion, no hydrology), so every untouched tile must come out bit for bit as it was.
/// </summary>
public class ClimateEditTests
{
    [WorldGeneratorStage(15, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class FixedRelief : IWorldGeneratorStage
    {
        public void Execute(WorldMap map)
        {
            var store = map.DataStore;
            var elev = store.GetSpan<ElevationInfo>();
            var vectors = store.GetTileVectors();
            for (int i = 0; i < store.TileCount; i++)
            {
                var g = vectors[i].ToGeoCoord();
                bool sea = g.Longitude >= 0 && g.Longitude <= 60 && Math.Abs(g.Latitude) <= 70;
                elev[i] = new ElevationInfo { Height = sea ? -0.5f : 0.2f };
            }
        }
    }

    private static World Build()
    {
        var b = new WorldBuilder().WithSize(4).WithSeed(7).WithoutDefaultStages().WithUserEdits();
        b.AddStage(new FixedRelief());
        b.AddStage(new ClimateStage(7));
        return b.Build();
    }

    private static ClimateInfo[] Copy(World w) => w.Map.DataStore.GetSpan<ClimateInfo>().ToArray();

    /// <summary>A land tile and a sea tile in the mid latitudes, where neither temperature nor rain sits on a clamp.</summary>
    private static (int land, int sea) Pick(World w, ClimateInfo[] c)
    {
        var s = w.Map.DataStore;
        var e = s.GetSpan<ElevationInfo>();
        int land = -1, sea = -1;
        for (int i = 0; i < s.TileCount; i++)
        {
            if (c[i].Temperature < 0.25f || c[i].Temperature > 0.75f || c[i].Precipitation < 0.25f || c[i].Precipitation > 0.75f) continue;
            if (e[i].Height > 0 && land < 0) land = i;
            if (e[i].Height < 0 && sea < 0) sea = i;
        }
        Assert.True(land >= 0 && sea >= 0);
        return (land, sea);
    }

    [Fact]
    public void Deltas_AreAddedLiterally_AndNothingElseMoves()
    {
        using var w = Build();
        var before = Copy(w);
        var (land, sea) = Pick(w, before);

        var edits = w.Edits;
        edits[land] = new UserEditInfo { TemperatureDelta = 0.15f, PrecipitationDelta = -0.1f };
        edits[sea] = new UserEditInfo { TemperatureDelta = -0.1f, PrecipitationDelta = 0.05f };
        w.Recompute();
        var after = Copy(w);

        Assert.Equal(before[land].Temperature + 0.15f, after[land].Temperature, 6);
        Assert.Equal(before[land].Precipitation - 0.1f, after[land].Precipitation, 6);
        Assert.Equal(before[sea].Temperature - 0.1f, after[sea].Temperature, 6);
        Assert.Equal(before[sea].Precipitation + 0.05f, after[sea].Precipitation, 6);

        // A literal local offset: no neighbour feels it, and the gyres and the heat they carry do not move.
        for (int i = 0; i < after.Length; i++)
        {
            if (i == land || i == sea) continue;
            Assert.Equal(before[i], after[i]);
        }
        Assert.Equal(before[sea].OceanHeat, after[sea].OceanHeat);
        Assert.Equal((before[sea].CurrentX, before[sea].CurrentY, before[sea].CurrentZ), (after[sea].CurrentX, after[sea].CurrentY, after[sea].CurrentZ));
    }

    [Fact]
    public void Deltas_ClampToTheUnitRange()
    {
        using var w = Build();
        var (land, sea) = Pick(w, Copy(w));
        w.Edits[land] = new UserEditInfo { TemperatureDelta = 5f, PrecipitationDelta = 5f };
        w.Edits[sea] = new UserEditInfo { TemperatureDelta = -5f, PrecipitationDelta = -5f };
        w.Recompute();
        var c = Copy(w);
        Assert.Equal((1f, 1f), (c[land].Temperature, c[land].Precipitation));
        Assert.Equal((0f, 0f), (c[sea].Temperature, c[sea].Precipitation));
    }

    [Fact]
    public void ClearingTheDeltas_RestoresTheGeneratedClimateExactly()
    {
        using var w = Build();
        var before = Copy(w);
        var (land, sea) = Pick(w, before);
        w.Edits[land] = new UserEditInfo { TemperatureDelta = 0.3f };
        w.Edits[sea] = new UserEditInfo { PrecipitationDelta = -0.3f };
        w.Recompute();
        w.Edits.Clear();
        w.Recompute();
        Assert.Equal(before, Copy(w));
    }

    [Fact]
    public void ARecompute_IsIdempotent()
    {
        using var w = Build();
        var (land, _) = Pick(w, Copy(w));
        w.Edits[land] = new UserEditInfo { TemperatureDelta = 0.2f, PrecipitationDelta = 0.2f };
        w.Recompute();
        var once = Copy(w);
        w.Recompute();
        Assert.Equal(once, Copy(w));
    }

    [Fact]
    public void IsEmpty_CountsTheClimateDeltas()
    {
        Assert.True(default(UserEditInfo).IsEmpty);
        Assert.False(new UserEditInfo { TemperatureDelta = 0.01f }.IsEmpty);
        Assert.False(new UserEditInfo { PrecipitationDelta = -0.01f }.IsEmpty);
    }

    [Fact]
    public void TheRefreshPass_KeepsTheBrushedOffset()
    {
        // The refresh pass rebuilds the climate layer wholesale after erosion; a delta applied only in the
        // first pass would vanish there. Erosion may move a tile's height a little (lapse rate), hence the tolerance.
        using var plain = new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().Build();
        var a = Copy(plain);
        var e = plain.Map.DataStore.GetSpan<ElevationInfo>();
        int t = -1;
        for (int i = 0; i < a.Length; i++)
            if (e[i].Height > 0.05f && a[i].Temperature is > 0.3f and < 0.6f && a[i].Precipitation is > 0.3f and < 0.7f) { t = i; break; }
        Assert.True(t >= 0);
        using var edited = new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().Build();
        edited.Edits[t] = new UserEditInfo { TemperatureDelta = 0.1f, PrecipitationDelta = -0.1f };
        edited.Recompute();
        var b = Copy(edited);
        Assert.InRange(b[t].Temperature - a[t].Temperature, 0.06f, 0.14f);
        Assert.InRange(b[t].Precipitation - a[t].Precipitation, -0.14f, -0.06f);
    }
}
