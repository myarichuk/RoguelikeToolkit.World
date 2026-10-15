using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class VillainOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory().Build();

    /// <summary>Make every polity a long-lived folk so villains can rise.</summary>
    private static void MakeDwarves(HistoryCatalog h)
    {
        var old = h.Polities.ToList();
        h.Polities.Clear();
        foreach (var p in old)
            h.Polities.Add(new Polity { Id = p.Id, Form = p.Form, CapitalTile = p.CapitalTile, FolkKey = "dwarf", CultureKey = p.CultureKey, FoundedTurn = p.FoundedTurn });
    }

    [Fact]
    public void Villains_Rise_RespectCap_AndFreezeSuccession()
    {
        int risen = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = Build(seed);
            var h = w.History;
            MakeDwarves(h);
            new HistoryDriver()
                .AddOperator(new PopulationOperator())
                .AddOperator(new WarOperator())
                .AddOperator(new VillainOperator())
                .Apply(w.Map, h, new HistoryContext { MaxVillains = 2 });
            Assert.True(h.Villains.Count <= 2);
            Assert.Equal(h.Villains.Count, h.Polities.Count(p => p.HasVillain));
            foreach (var v in h.Villains)
            {
                risen++;
                var polity = h.Polities[v.PolityId];
                Assert.True(polity.HasVillain);
                var ev = h.Events.Single(e => e.Kind == HistoryEventKind.Villain && e.PolityId == v.PolityId);
                Assert.Equal(v.RisenTurn, ev.Turn);
                Assert.Contains($"villain:{v.Id}", ev.Tags);
                var folk = HistoryDemography.ResolveFolk("dwarf", null);
                Assert.Equal(HistoryDemography.SuccessionsBefore(folk.LifespanYears, 10, v.RisenTurn), polity.RulerGeneration);
            }
        }
        Assert.True(risen > 0, "No villain rose across seeds 1-12 with forced dwarves.");
    }
}
