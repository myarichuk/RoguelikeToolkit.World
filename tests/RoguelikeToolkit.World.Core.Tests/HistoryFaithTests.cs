using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Integration;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>P2c (treaty, faith, grudges, holy sites, villains, khan, myth text) acceptance.</summary>
public class HistoryFaithTests
{
    private const int Size = 3;
    private static readonly int[] Seeds = { 42, 7, 1234, 99 };

    private static World Build(int seed = 42, Action<HistoryContext>? configure = null)
    {
        var builder = new WorldBuilder().WithSize(Size).WithSeed(seed).WithUserEdits();
        if (configure != null) builder.WithHistory(configure);
        return builder.Build();
    }

    private static World BuildWithVillains(int seed, int maxVillains)
    {
        var w = Build(seed);
        new HistoryDriver()
            .AddOperator(new PopulationOperator())
            .AddOperator(new WarOperator())
            .AddOperator(new VillainOperator())
            .Apply(w.Map, w.History, new HistoryContext { MaxVillains = maxVillains });
        return w;
    }

    [Fact]
    public void StageOrders_SitBetweenTheOperators()
    {
        Assert.Equal(44, StageOrder.HolySite);
        Assert.Equal(46, StageOrder.Khan);
        Assert.Equal(48, StageOrder.Treaty);
        Assert.Equal(76, StageOrder.Villain);
        Assert.Equal(84, StageOrder.Faith);
        Assert.True(StageOrder.Treaty < StageOrder.War);
        Assert.True(StageOrder.Rebellion < StageOrder.Villain);
        Assert.True(StageOrder.Exchange < StageOrder.Faith);
        Assert.True(StageOrder.Faith < StageOrder.CityHistoryProject);
    }

    private static World BuildFull(int seed)
    {
        var w = Build(seed);
        new HistoryDriver()
            .AddOperator(new PopulationOperator())
            .AddOperator(new HolySiteOperator())
            .AddOperator(new TradeRoadOperator())
            .AddOperator(new WarOperator())
            .AddOperator(new ConquestOperator())
            .AddOperator(new TreatyOperator())
            .AddOperator(new ExchangeOperator())
            .AddPostPass(new FaithPostPass())
            .AddPostPass(new CityHistoryProjectPostPass())
            .Apply(w.Map, w.History, new HistoryContext());
        return w;
    }

    [Fact]
    public void PostPasses_ProjectCitiesAndFaiths_Once()
    {
        using var w = BuildFull(42);
        var h = w.History;
        Assert.Equal(h.Polities.Count, h.Settlements.Count);
        Assert.Equal(h.Polities.Count, h.Faiths.Count);
        new FaithPostPass().Run(w.Map, h, new HistoryContext());
        new CityHistoryProjectPostPass().Run(w.Map, h, new HistoryContext());
        Assert.Equal(h.Polities.Count, h.Settlements.Count);
        Assert.Equal(h.Polities.Count, h.Faiths.Count);
    }

    [Fact]
    public void Faith_EveryPolityHasBoundedPowersAndOneMythEvent()
    {
        foreach (int seed in Seeds)
        {
            using var w = BuildFull(seed);
            var h = w.History;
            Assert.Equal(h.Polities.Count, h.Faiths.Count);
            Assert.Equal(h.Polities.Count, h.Events.Count(e => e.Kind == HistoryEventKind.Myth));
            foreach (var faith in h.Faiths)
            {
                Assert.False(string.IsNullOrWhiteSpace(faith.Form));
                Assert.InRange(faith.Powers.Count, 2, 7);
                Assert.Equal(faith.Powers.Count, faith.Powers.Distinct().Count());
                Assert.Contains(faith.MythSlots, s => s.StartsWith("origin:", StringComparison.Ordinal));
                Assert.InRange(faith.MythSlots.Count, 1, 4);
            }
        }
    }

    [Fact]
    public void Villains_RespectTheCap()
    {
        foreach (int seed in Seeds)
        {
            using var none = BuildWithVillains(seed, 0);
            Assert.Empty(none.History.Villains);
            using var one = BuildWithVillains(seed, 1);
            Assert.True(one.History.Villains.Count <= 1);
        }
    }

    [Fact]
    public void Villains_AreLongLivedAndFreezeSuccession()
    {
        foreach (int seed in Seeds)
        {
            using var w = BuildWithVillains(seed, 4);
            var h = w.History;
            foreach (var villain in h.Villains)
            {
                var polity = h.Polities[villain.PolityId];
                Assert.True(polity.HasVillain);
                Assert.Equal(polity.FolkKey, villain.FolkKey);
                Assert.Equal("dwarf", villain.FolkKey);
                Assert.InRange(villain.RisenTurn, 1, 29);
            }
            Assert.Equal(h.Villains.Count, h.Polities.Count(p => p.HasVillain));
        }
    }

    [Fact]
    public void HeirAdultGate_NeedsAnHeirOldEnough()
    {
        Assert.False(TreatyOperator.IsHeirAdult(-1, turn: 10, turnYears: 10, maturationYears: 16));
        Assert.False(TreatyOperator.IsHeirAdult(2, turn: 3, turnYears: 10, maturationYears: 16));
        Assert.True(TreatyOperator.IsHeirAdult(2, turn: 4, turnYears: 10, maturationYears: 16));
    }

    [Fact]
    public void Grudges_PersistUntilRepaid()
    {
        var h = new HistoryCatalog(1);
        h.AddGrudge(2, 1, 3);
        h.AddGrudge(1, 2, 1);
        Assert.Equal(4, h.GrudgeWeight(1, 2));
        Assert.Equal(4, h.GrudgeWeight(2, 1));
        Assert.True(h.RepayGrudges(2, 1));
        Assert.Equal(0, h.GrudgeWeight(1, 2));
        Assert.False(h.RepayGrudges(1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => h.AddGrudge(1, 2, 0));
        Assert.Throws<ArgumentException>(() => h.AddGrudge(3, 3, 1));
    }

    [Fact]
    public void Treaty_WindowsAreNonAggressionWindows()
    {
        var h = new HistoryCatalog(1);
        h.AddWindow(0, 1, untilTurn: 5);
        Assert.True(h.IsWindowed(1, 0, 5));
        Assert.False(h.IsWindowed(1, 0, 6));
        Assert.False(h.IsWindowed(0, 2, 3));
    }

    [Fact]
    public void P2cState_IsDeterministic_AndRoundTrips()
    {
        foreach (int seed in Seeds)
        {
            using var a = Build(seed);
            using var b = Build(seed);
            byte[] bytes = a.History.ToBytes();
            Assert.Equal(bytes, b.History.ToBytes());
            HistoryCatalog loaded;
            using (var stream = new MemoryStream(bytes, writable: false))
                loaded = HistoryCatalog.Load(stream);
            Assert.Equal(bytes, loaded.ToBytes());
            Assert.Equal(a.History.Faiths.Count, loaded.Faiths.Count);
            Assert.Equal(a.History.HolySites.Count, loaded.HolySites.Count);
            Assert.Equal(a.History.Villains.Count, loaded.Villains.Count);
            Assert.Equal(a.History.Grudges.Count, loaded.Grudges.Count);
            Assert.Equal(a.History.Treaties.Count, loaded.Treaties.Count);
        }
    }

    [Fact]
    public void Operators_EmitTheirEventKinds_AcrossSeeds()
    {
        var seen = new HashSet<HistoryEventKind>();
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = BuildFull(seed);
            foreach (var e in w.History.Events) seen.Add(e.Kind);
        }
        Assert.Contains(HistoryEventKind.Myth, seen);
        Assert.Contains(HistoryEventKind.HolyFound, seen);
    }

    [Fact]
    public void Khan_SubmitChance_GrowsWithAdvantage_AndIsCapped()
    {
        Assert.False(KhanOperator.DecideOutcome(0.5, ratio: 1.0));
        Assert.True(KhanOperator.DecideOutcome(0.5, ratio: 2.0));
        Assert.False(KhanOperator.DecideOutcome(KhanOperator.MaxSubmitChance, ratio: 1000));
        Assert.True(KhanOperator.DecideOutcome(KhanOperator.MaxSubmitChance - 0.01, ratio: 1000));
    }

    [Fact]
    public void MythText_RendersFromTags_WithoutTouchingHistory()
    {
        using var w = BuildFull(42);
        byte[] before = w.History.ToBytes();
        var provider = new TemplateMythTextProvider();
        var faith = w.History.Faiths[0];
        foreach (string slot in faith.MythSlots)
        {
            string first = provider.Render(faith.Form, slot, faith.Powers);
            Assert.Equal(first, provider.Render(faith.Form, slot, faith.Powers));
            Assert.False(string.IsNullOrWhiteSpace(first));
        }
        Assert.Equal(string.Empty, provider.Render(faith.Form, "unknown:slot", faith.Powers));
        Assert.Equal(string.Empty, provider.Render(faith.Form, "", faith.Powers));
        Assert.Equal(before, w.History.ToBytes());
    }
}
