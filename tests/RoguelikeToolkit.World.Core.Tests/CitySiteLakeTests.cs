using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Settlement;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>City sites must never sit on lake water: ponded tiles read as water on the map.</summary>
public class CitySiteLakeTests
{
    [Fact]
    public void ScoredSites_AvoidPondedLakeTiles()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(12).Build();
        var store = w.Map.DataStore;
        var hydro = store.GetSpan<HydrologyInfo>();

        // Positive control: this world actually ponds lakes (see LakeTests.RealWorld_LakeRiversExistAndAreDeterministic).
        Assert.Contains(hydro.ToArray(), h => h.LakeDepth > 0f);

        var sites = w.ScoreCitySites(new CitySiteFilter { TopN = 100 });
        Assert.NotEmpty(sites);

        var lakeTiles = new HashSet<int>();
        foreach (var b in w.WaterBodies.Bodies)
            if (b.Kind == WaterBodyKind.Lake)
                foreach (int t in b.Tiles)
                    lakeTiles.Add(t);
        Assert.NotEmpty(lakeTiles);

        foreach (var s in sites)
        {
            Assert.True(hydro[s.TileIndex].LakeDepth <= 0f,
                $"Site {s.TileIndex} sits on a ponded lake tile (depth {hydro[s.TileIndex].LakeDepth}).");
            Assert.DoesNotContain(s.TileIndex, lakeTiles);
        }
    }
}
