using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M3.12: reserved plugin order band and namespaced names.</summary>
public class HistoryNamingTests
{
    private sealed class Op(string name, int order) : IHistoryOperator
    {
        public string Name => name;
        public int Order => order;
        public void Propose(TurnView view, ProposalSink sink) { }
    }

    [Theory]
    [InlineData("econ/tax", true)]
    [InlineData("econ", false)]
    [InlineData("/tax", false)]
    [InlineData("econ/", false)]
    [InlineData("a/b/c", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    public void PluginNames_AreNamespaced(string? name, bool expected)
        => Assert.Equal(expected, HistoryNaming.IsPluginName(name));

    [Fact]
    public void PluginOperators_NeedNamespaceAndBand()
    {
        var driver = new HistoryDriver();
        driver.AddPluginOperator(new Op("econ/fiscal", HistoryNaming.PluginOrderMin));
        driver.AddPluginOperator(new Op("econ/economy", HistoryNaming.PluginOrderMax));
        Assert.Throws<ArgumentException>(() => driver.AddPluginOperator(new Op("fiscal", 150)));
        Assert.Throws<ArgumentOutOfRangeException>(() => driver.AddPluginOperator(new Op("econ/low", 99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => driver.AddPluginOperator(new Op("econ/high", 900)));
        Assert.Throws<InvalidOperationException>(() => driver.AddPluginOperator(new Op("econ/fiscal", 120)));
    }

    [Fact]
    public void BuiltIns_StayBelowThePluginBand()
    {
        IHistoryOperator[] builtIns =
        [
            new PopulationOperator(), new SettleOperator(), new TierOperator(), new HazardOperator(),
            new TradeRoadOperator(), new HolySiteOperator(), new KhanOperator(), new TreatyOperator(),
            new WarOperator(), new ConquestOperator(), new RebellionOperator(), new CollapseOperator(),
            new AbsorbOperator(), new VillainOperator(), new ExchangeOperator(),
        ];
        foreach (var op in builtIns)
            Assert.True(op.Order < HistoryNaming.PluginOrderMin, op.Name);
    }

    [Fact]
    public void PluginScalars_NeedNamespace()
    {
        var reg = new ScalarRegistry();
        Assert.Equal("econ/wealth", reg.DeclarePlugin("econ/wealth", 0, 100, 10).Name);
        Assert.Throws<ArgumentException>(() => reg.DeclarePlugin("wealth", 0, 100, 10));
    }
}
