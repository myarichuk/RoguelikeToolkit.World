using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.4A: population is stored per settlement and a polity's stock is the sum of its standing settlements.</summary>
public class SettlementPopulationTests
{
    private static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6 };

    private static World Build(int seed)
        => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().Build();

    [Fact]
    public void PolityPopulation_IsTheSumOfItsStandingSettlements()
    {
        int multi = 0;
        foreach (int seed in Seeds)
        {
            using var w = Build(seed);
            var h = w.History;
            foreach (var polity in h.Polities)
            {
                var standing = h.Settlements.Where(s => s.PolityId == polity.Id && !s.Ended).ToList();
                if (standing.Count == 0) continue;
                if (standing.Count > 1) multi++;
                Assert.Equal(standing.Sum(s => s.Population), polity.Population, 6);
            }
        }
        Assert.True(multi > 0, "No polity grew a second settlement across seeds 1-6.");
    }

    [Fact]
    public void SettlementPopulations_AreNonNegative_AndColoniesStartWithPeople()
    {
        foreach (int seed in Seeds)
        {
            using var w = Build(seed);
            foreach (var s in w.History.Settlements.Where(s => !s.Ended))
                Assert.True(s.Population >= 0, "Negative settlement population.");
            foreach (var s in w.History.Settlements.Where(s => s.Role == SettlementRole.Colony && !s.Ended))
                Assert.True(s.Population > 0, "A standing colony holds nobody.");
        }
    }

    [Fact]
    public void SettlementCapacities_SumToThePolityCapacity()
    {
        foreach (int seed in Seeds)
        {
            using var w = Build(seed);
            var h = w.History;
            var store = w.Map.DataStore;
            double[] polityCaps = HistoryDemography.CarryingCapacities(store, h.Polities.Count, h.Ownership);
            var states = h.Settlements.Select((s, i) => new TurnView.SettlementState(
                i, s.TileIndex, s.PolityId, s.Tier, s.Role, s.Parent, s.FoundedTurn, s.Ended, s.Population)).ToList();
            double[] caps = HistoryDemography.SettlementCapacities(store, h.Ownership, states, polityCaps);
            foreach (var polity in h.Polities)
            {
                double sum = states.Where(s => !s.Ended && s.PolityId == polity.Id).Sum(s => caps[s.Index]);
                if (states.Any(s => !s.Ended && s.PolityId == polity.Id))
                    Assert.Equal(polityCaps[polity.Id], sum, 6);
            }
        }
    }

    [Fact]
    public void SettlementPopulation_RoundTripsAndIsDeterministic()
    {
        using var a = Build(3);
        using var b = Build(3);
        byte[] bytes = a.History.ToBytes();
        Assert.Equal(bytes, b.History.ToBytes());
        HistoryCatalog loaded;
        using (var stream = new MemoryStream(bytes, writable: false))
            loaded = HistoryCatalog.Load(stream);
        Assert.Equal(bytes, loaded.ToBytes());
        for (int i = 0; i < a.History.Settlements.Count; i++)
            Assert.Equal(a.History.Settlements[i].Population, loaded.Settlements[i].Population);
    }
}
