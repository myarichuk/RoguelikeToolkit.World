using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.6 acceptance: the physics-driven shock producer.</summary>
public class HazardOperatorTests
{
    private static World Build(int seed, Action<HistoryContext>? configure = null)
    {
        var builder = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits();
        if (configure != null) builder.WithHistory(configure);
        return builder.Build();
    }

    private static readonly int[] Seeds = { 1, 2, 3, 5, 8, 13, 21, 34, 42, 55 };

    [Fact]
    public void Maps_ArePureAndBounded()
    {
        using var w = Build(42);
        var a = HazardMaps.Build(w.Map, 42);
        var b = HazardMaps.Build(w.Map, 42);
        foreach (string kind in HazardMaps.Kinds)
        {
            Assert.Equal(a.Total(kind), b.Total(kind));
            for (int t = 0; t < w.Map.DataStore.TileCount; t++)
                Assert.InRange(a.Of(kind, t), 0, 1);
        }
        Assert.True(a.Total(HazardMaps.Earthquake) > 0, "No seismic land.");
        Assert.True(a.Total(HazardMaps.Flood) > 0, "No flood plains.");
        Assert.Equal(0, a.Of("plague", 0));
    }

    [Fact]
    public void Shocks_StrikeAndLeaveALedger()
    {
        var kinds = new HashSet<string>();
        int shocks = 0, ended = 0;
        foreach (int seed in Seeds)
        {
            using var w = Build(seed);
            var h = w.History;
            foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.Shock))
            {
                shocks++;
                string kind = e.Tags.First(t => t.StartsWith("shock:", StringComparison.Ordinal)).Substring(6);
                kinds.Add(kind);
                Assert.Contains(kind, HazardOperator.Kinds);
                Assert.Contains(e.Tags, t => t.StartsWith("struck:", StringComparison.Ordinal));
            }
            ended += h.Settlements.Count(s => s.Ended);
            Assert.All(h.Displaced, g => Assert.True(g.Amount > 0));
            Assert.All(h.Settlements, s => Assert.True(s.Population >= 0));
            Assert.True(h.PopulationLost >= 0);
        }
        Assert.True(shocks > 0, "No shock fired in ten worlds.");
        Assert.Contains(HazardOperator.Famine, kinds);
        Assert.Contains(HazardOperator.Plague, kinds);
        Assert.True(kinds.Count >= 3, "Shock kinds: " + string.Join(",", kinds));
        Assert.True(ended > 0, "No settlement was ever abandoned.");
    }

    [Fact]
    public void HazardRateZero_SwitchesShocksOff()
    {
        foreach (int seed in new[] { 2, 13, 55 })
        {
            using var w = Build(seed, ctx => ctx.HazardRate = 0);
            Assert.DoesNotContain(w.History.Events, e => e.Kind == HistoryEventKind.Shock);
        }
    }

    [Fact]
    public void SameSeed_SameShocks()
    {
        using var a = Build(13);
        using var b = Build(13);
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
        Assert.Equal(
            a.History.Events.Count(e => e.Kind == HistoryEventKind.Shock),
            b.History.Events.Count(e => e.Kind == HistoryEventKind.Shock));
    }

    [Fact]
    public void HazardRate_RejectsNegative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoryContext { HazardRate = -1 });
}
