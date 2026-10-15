using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class HolySiteOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    private static void Run(World w)
        => new HistoryDriver().AddOperator(new HolySiteOperator()).Apply(w.Map, w.History, new HistoryContext());

    [Fact]
    public void OneSitePerPolityAtMost_OwnedAndOffCapital_SameSeedSameResult()
    {
        int sites = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = Build(seed);
            using var twin = Build(seed);
            Run(w);
            Run(twin);
            var h = w.History;
            Assert.Equal(h.HolySites.Select(s => s.TileIndex), twin.History.HolySites.Select(s => s.TileIndex));
            Assert.Equal(h.HolySites.Count, h.Events.Count(e => e.Kind == HistoryEventKind.HolyFound));
            Assert.Equal(h.HolySites.Count, h.HolySites.Select(s => s.PolityId).Distinct().Count());
            foreach (var site in h.HolySites)
            {
                sites++;
                Assert.Equal(1, site.FoundedTurn);
                Assert.NotEqual(h.Polities[site.PolityId].CapitalTile, site.TileIndex);
                Assert.Equal(site.PolityId, h.Ownership[site.TileIndex]);
            }
        }
        Assert.True(sites > 0);
    }
}
