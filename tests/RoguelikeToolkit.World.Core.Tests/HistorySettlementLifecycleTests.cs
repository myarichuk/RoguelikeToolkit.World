using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.1: settlements carry lifecycle state that survives the schema v5 round trip.</summary>
public class HistorySettlementLifecycleTests
{
    [Fact]
    public void Capitals_StartAsStandingCapitalCities()
    {
        // Shocks abandon settlements; pin them off to check the founding state (wars never end capitals).
        using var w = new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().WithHistory(c => c.HazardRate = 0).Build();
        Assert.NotEmpty(w.History.Settlements);
        foreach (var s in w.History.Settlements.Where(s => s.Role == SettlementRole.Capital && w.History.Polities[s.PolityId].Alive && w.History.Polities[s.PolityId].CapitalTile == s.TileIndex && w.History.Polities[s.PolityId].FoundedTurn == 0))
        {
            Assert.False(s.Ended);
            Assert.Equal(-1, s.EndedTurn);
            if (s.Role == SettlementRole.Capital)
            {
                // Founded as a city; only the tier operator moves it (never below the capital floor), and it logs each step.
                int index = w.History.Settlements.IndexOf(s);
                if (!w.History.Events.Any(e => e.Kind == HistoryEventKind.SettlementTierChanged && e.Tags.Contains($"settlement:{index}")))
                    Assert.Equal(SettlementTier.City, s.Tier);
                else Assert.True(s.Tier >= TierOperator.CapitalFloor);
                Assert.Equal(-1, s.Parent);
            }
        }
        // Collapse successors may take over a standing settlement instead of founding a new seat.
        Assert.True(w.History.Settlements.Count(s => s.Role == SettlementRole.Capital) <= w.History.Polities.Count);
        Assert.All(w.History.Polities.Where(p => p.Alive), p => Assert.Contains(w.History.Settlements, s => !s.Ended && s.TileIndex == p.CapitalTile));
    }

    [Fact]
    public void Lifecycle_RoundTrips()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().Build();
        var s = w.History.Settlements[0];
        s.EndedTurn = 12; s.EndCause = "razed"; s.Tier = SettlementTier.Town; s.Role = SettlementRole.Colony; s.Parent = 0;
        byte[] bytes = w.History.ToBytes();
        HistoryCatalog loaded;
        using (var stream = new MemoryStream(bytes, writable: false)) loaded = HistoryCatalog.Load(stream);
        var t = loaded.Settlements[0];
        Assert.True(t.Ended);
        Assert.Equal(12, t.EndedTurn);
        Assert.Equal("razed", t.EndCause);
        Assert.Equal(SettlementTier.Town, t.Tier);
        Assert.Equal(SettlementRole.Colony, t.Role);
        Assert.Equal(bytes, loaded.ToBytes());
    }

    [Fact]
    public void CorruptParent_IsRejected()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().Build();
        w.History.Settlements[0].Parent = 9999;
        byte[] bytes = w.History.ToBytes();
        using var stream = new MemoryStream(bytes, writable: false);
        Assert.Throws<InvalidDataException>(() => HistoryCatalog.Load(stream));
    }

    [Fact]
    public void CapitalSettlements_ExistBeforeAnyTurnRuns()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();
        Assert.NotEmpty(w.History.Polities);
        Assert.Equal(w.History.Polities.Count, w.History.Settlements.Count);
        foreach (var polity in w.History.Polities)
        {
            var settlement = w.History.Settlements[polity.Id];
            Assert.Equal(polity.CapitalTile, settlement.TileIndex);
            Assert.Equal(polity.Id, settlement.PolityId);
            Assert.Equal(polity.FoundedTurn, settlement.FoundedTurn);
            Assert.Equal(SettlementRole.Capital, settlement.Role);
        }
    }
}
