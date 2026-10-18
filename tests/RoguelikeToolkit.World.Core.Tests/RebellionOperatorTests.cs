using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class RebellionOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    /// <summary>Default worlds never produce rebellions (finding A), so force a tight culture on every polity.</summary>
    private static void MakeTight(HistoryCatalog h)
    {
        var old = h.Polities.ToList();
        h.Polities.Clear();
        foreach (var p in old)
        {
            h.Polities.Add(new Polity
            {
                Id = p.Id, Form = p.Form, CapitalTile = p.CapitalTile, FolkKey = p.FolkKey,
                CultureKey = "dwarven_clan", FoundedTurn = p.FoundedTurn,
            });
        }
    }

    private static void Run(World w, int maxPolities, int maxRebellions = 0)
        => new HistoryDriver()
            .AddOperator(new PopulationOperator())
            .AddOperator(new HolySiteOperator())
            .AddOperator(new WarOperator())
            .AddOperator(new RebellionOperator())
            .Apply(w.Map, w.History, new HistoryContext { MaxPolities = maxPolities, MaxRebellions = maxRebellions });

    [Fact]
    public void Rebels_SplitAProvince_WithConsistentOwnershipAndCap()
    {
        int rebellions = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            using var w = Build(seed);
            var h = w.History;
            MakeTight(h);
            Run(w, 16);
            Assert.True(h.Polities.Count <= 16);
            foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.Rebellion))
            {
                rebellions++;
                int parent = int.Parse(e.Tags.First(t => t.StartsWith("parent:")).Substring(7));
                var rebel = h.Polities[e.PolityId];
                Assert.NotEqual(parent, e.PolityId);
                Assert.Equal(parent, rebel.Parent);
                Assert.Equal(h.Polities[parent].FolkKey, rebel.FolkKey);
                Assert.Equal(e.Turn, rebel.FoundedTurn);
                Assert.Equal(rebel.CapitalTile, e.TileIndex);
                Assert.Equal(e.PolityId, h.OwnerOf(rebel.CapitalTile));
            }
            for (int id = 0; id < h.Polities.Count; id++)
                Assert.Equal(id, h.OwnerOf(h.Polities[id].CapitalTile));
            foreach (var site in h.HolySites)
                Assert.Equal(h.OwnerOf(site.TileIndex), site.PolityId);
        }
        Assert.True(rebellions > 0, "No rebellion across seeds 1-20 with tight cultures.");
    }

    [Fact]
    public void PolityCap_HoldsAtCommit()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            using var w = Build(seed);
            MakeTight(w.History);
            int start = w.History.Polities.Count;
            Run(w, start, maxRebellions: 1);
            Assert.True(w.History.Polities.Count <= start + 1);
        }
    }

    [Fact]
    public void DefaultHistory_HasRebellions_WithinTheirOwnAllowance()
    {
        int rebellions = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = new WorldBuilder().WithSize(4).WithSeed(seed).WithHistory().Build();
            var h = w.History;
            int n = h.Events.Count(e => e.Kind == HistoryEventKind.Rebellion);
            Assert.True(n <= new HistoryContext().MaxRebellions);
            rebellions += n;
        }
        Assert.True(rebellions > 0, "no rebellion in 12 default worlds; the founding cap is still blocking them");
    }
}
