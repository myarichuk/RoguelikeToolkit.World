using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.1: settlements carry lifecycle state that survives the schema v5 round trip.</summary>
public class HistorySettlementLifecycleTests
{
    [Fact]
    public void Capitals_StartAsStandingCapitalCities()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().Build();
        Assert.NotEmpty(w.History.Settlements);
        foreach (var s in w.History.Settlements)
        {
            Assert.False(s.Ended);
            Assert.Equal(-1, s.EndedTurn);
            if (s.Role == SettlementRole.Capital)
            {
                Assert.Equal(SettlementTier.City, s.Tier);
                Assert.Equal(-1, s.Parent);
            }
        }
        Assert.Equal(w.History.Polities.Count, w.History.Settlements.Count(s => s.Role == SettlementRole.Capital));
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
