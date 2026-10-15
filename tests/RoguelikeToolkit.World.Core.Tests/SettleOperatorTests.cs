using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.3C: colonization by pressure overflow.</summary>
public class SettleOperatorTests
{
    private static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6 };

    private static World Build(int seed, Action<HistoryContext>? configure = null)
    {
        var builder = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits();
        if (configure != null) builder.WithHistory(configure);
        return builder.Build();
    }

    [Fact]
    public void StageOrder_SitsAfterPopulation()
    {
        Assert.Equal(34, Generation.StageOrder.Settle);
        Assert.True(Generation.StageOrder.Population < Generation.StageOrder.Settle);
    }

    [Fact]
    public void Colonies_AreOwnedSpacedAndParentedWithinThePolity()
    {
        int colonies = 0;
        foreach (int seed in Seeds)
        {
            using var w = Build(seed);
            var h = w.History;
            var vectors = w.Map.DataStore.GetTileVectors();
            double minDot = Core.Primitives.DetMath.Cos(SettleOperator.MinSpacingDegrees * Core.Primitives.GeoCoord.Deg2Rad);
            for (int i = 0; i < h.Settlements.Count; i++)
            {
                var s = h.Settlements[i];
                if (s.Role != SettlementRole.Colony) continue;
                colonies++;
                Assert.Equal(SettlementTier.Hamlet, s.Tier);
                Assert.InRange(s.FoundedTurn, 1, 29);
                Assert.InRange(s.Parent, 0, i - 1);
                // A war may have carried either settlement off to its captor.
                bool captured = h.Events.Any(e => e.Kind == HistoryEventKind.SettlementCaptured
                    && (e.Tags.Contains($"settlement:{i}") || e.Tags.Contains($"settlement:{s.Parent}")));
                if (!captured) Assert.Equal(s.PolityId, h.Settlements[s.Parent].PolityId);
                for (int j = 0; j < i; j++)
                {
                    // Land of a settlement already abandoned (e.g. by a shock) may be resettled.
                    if (h.Settlements[j].Ended && h.Settlements[j].EndedTurn < s.FoundedTurn) continue;
                    Assert.True(Core.Primitives.Vector3D.Dot(vectors[s.TileIndex], vectors[h.Settlements[j].TileIndex]) < minDot,
                        $"Settlement {i} sits within the spacing of {j}.");
                }
            }
        }
        Assert.True(colonies > 0, "No colony was founded across seeds 1-6.");
    }

    [Fact]
    public void Colonies_RespectThePerPolityCap_AndLogOneEventEach()
    {
        foreach (int seed in Seeds)
        {
            using var w = Build(seed, ctx => ctx.MaxSettlementsPerPolity = 2);
            var h = w.History;
            foreach (var polity in h.Polities)
                Assert.True(h.Settlements.Count(s => s.PolityId == polity.Id && !s.Ended) <= 2);
            Assert.Equal(h.Settlements.Count(s => s.Role == SettlementRole.Colony),
                h.Events.Count(e => e.Kind == HistoryEventKind.SettlementFounded && e.Tags.Contains("role:Colony")));
        }
    }

    [Fact]
    public void ZeroCapOrZeroEmergence_FoundsNothing()
    {
        foreach (int seed in Seeds)
        {
            using var capped = Build(seed, ctx => ctx.MaxSettlementsPerPolity = 1);
            Assert.DoesNotContain(capped.History.Settlements, s => s.Role == SettlementRole.Colony);
            using var starved = Build(seed, ctx => ctx.MaxEmergencesPerTurn = 0);
            Assert.DoesNotContain(starved.History.Settlements, s => s.Role == SettlementRole.Colony);
        }
    }

    [Fact]
    public void Colonization_ShedsPressure()
    {
        (double Total, int Colonies) Run(int seed, bool settle)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();
            var driver = new HistoryDriver().AddOperator(new PopulationOperator()).AddOperator(new WarOperator());
            if (settle) driver.AddOperator(new SettleOperator());
            driver.Apply(w.Map, w.History, new HistoryContext());
            return (w.History.Polities.Sum(p => p.Pressure), w.History.Settlements.Count(s => s.Role == SettlementRole.Colony));
        }
        int checkedWorlds = 0;
        foreach (int seed in Seeds)
        {
            var with = Run(seed, settle: true);
            if (with.Colonies == 0) continue;
            checkedWorlds++;
            Assert.True(with.Total < Run(seed, settle: false).Total, $"Seed {seed}: colonization shed no pressure.");
        }
        Assert.True(checkedWorlds > 0, "No seed colonized.");
    }
}
