using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// M2 exit: with growth switched off (no <see cref="PopulationOperator"/>), every other operator -
/// shocks, wars, colonies, refuges, asylum, decay - only moves people, so standing + pool + lost
/// is conserved turn by turn across a whole run.
/// </summary>
public class PopulationLedgerTests
{
    private static double Total(HistoryCatalog h)
        => h.Settlements.Where(s => !s.Ended).Sum(s => s.Population) + h.Displaced.Sum(g => g.Amount) + h.PopulationLost;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void FullRun_WithoutGrowth_ConservesPeopleEveryTurn(int seed)
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();
        var h = w.History;
        foreach (var s in h.Settlements) s.Population = 2000;
        foreach (var p in h.Polities) p.Population = h.Settlements.Where(s => !s.Ended && s.PolityId == p.Id).Sum(s => s.Population);

        var driver = new HistoryDriver()
            .AddOperator(new SettleOperator()).AddOperator(new HazardOperator()).AddOperator(new TradeRoadOperator())
            .AddOperator(new HolySiteOperator()).AddOperator(new KhanOperator()).AddOperator(new TreatyOperator())
            .AddOperator(new WarOperator()).AddOperator(new ConquestOperator()).AddOperator(new RebellionOperator())
            .AddOperator(new VillainOperator()).AddOperator(new ExchangeOperator());
        var ctx = new HistoryContext { HazardRate = 3 };
        double expected = Total(h);
        int turns = Math.Max(ctx.TurnCount, 100);
        for (int turn = 1; turn < turns; turn++)
        {
            driver.RunTurn(w.Map, h, ctx, turn);
            Assert.True(Math.Abs(Total(h) - expected) < 1e-6 * expected, $"seed {seed} turn {turn}: {Total(h):F3} != {expected:F3}");
        }
        Assert.True(h.PopulationLost > 0 || h.Events.Any(e => e.Kind is HistoryEventKind.SettlementFounded or HistoryEventKind.Shock),
            "The run must actually move people, or the ledger proves nothing.");
        Assert.All(h.Displaced, g => Assert.True(g.Amount >= 0));
        Assert.All(h.Settlements, s => Assert.True(s.Population >= 0));
    }
}
