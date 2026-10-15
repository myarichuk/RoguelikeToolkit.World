using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2 exit: determinism, save/load round-trip and event caps on full standard runs under heavy hazard.</summary>
public class HistoryExitChecksTests
{
    private static World Build(int seed)
        => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory(c => c.HazardRate = 3).Build();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SameSeed_YieldsIdenticalHistoryBytes(int seed)
    {
        using var a = Build(seed);
        using var b = Build(seed);
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
    }

    [Fact]
    public void DifferentSeeds_YieldDifferentHistory()
    {
        using var a = Build(1);
        using var b = Build(2);
        Assert.NotEqual(a.History.ToBytes(), b.History.ToBytes());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FullRun_RoundTripsByteForByte_WithScarsCapturesAndRefuges(int seed)
    {
        using var w = Build(seed);
        var h = w.History;
        byte[] bytes = h.ToBytes();
        using var stream = new MemoryStream(bytes, writable: false);
        var loaded = HistoryCatalog.Load(stream);
        Assert.Equal(bytes, loaded.ToBytes());
        Assert.Equal(h.Scars.Count, loaded.Scars.Count);
        Assert.Equal(h.Settlements.Count, loaded.Settlements.Count);
        Assert.Equal(h.Settlements.Count(s => s.Ended), loaded.Settlements.Count(s => s.Ended));
        Assert.Equal(h.Settlements.Count(s => s.Role == SettlementRole.Refuge), loaded.Settlements.Count(s => s.Role == SettlementRole.Refuge));
        Assert.Equal(h.Displaced.Sum(g => g.Amount), loaded.Displaced.Sum(g => g.Amount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FullRun_RespectsEventCaps(int seed)
    {
        using var w = Build(seed);
        var h = w.History;
        Assert.True(h.Events.Count <= h.MaxEvents, $"{h.Events.Count} > {h.MaxEvents}");
        foreach (var turn in h.Events.GroupBy(e => e.Turn))
            Assert.True(turn.Count() <= h.MaxEventsPerTurn + 8, $"turn {turn.Key}: {turn.Count()} events");
    }
}
