using EconomyPlugin;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.MapEditor.Engine;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

/// <summary>M3.14: the economy plugin lives outside Core, is deterministic, round-trips, and costs nothing when absent.</summary>
public class EconomyPluginTests : IDisposable
{
    public EconomyPluginTests() => HistoryPluginHost.Clear();
    public void Dispose() => HistoryPluginHost.Clear();

    private static HistoryCatalog Build(bool plugin, int seed = 11, int size = 4)
    {
        HistoryPluginHost.Clear();
        if (plugin) HistoryPluginHost.Register(Economy.Register, Economy.DeclareScalars);
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithUserEdits();
        HistoryPluginHost.Apply(builder);
        using var world = builder.Build();
        return RoundTrip(world.History);
    }

    private static HistoryCatalog RoundTrip(HistoryCatalog h)
    {
        using var stream = new MemoryStream(h.ToBytes(), writable: false);
        return HistoryCatalog.Load(stream);
    }

    [Fact]
    public void DeclaresFiveScalars_AndTheyMove()
    {
        var h = Build(plugin: true);
        foreach (var name in new[] { EconomyScalars.Wealth, EconomyScalars.Tax, EconomyScalars.Corruption, EconomyScalars.Unrest, EconomyScalars.Estates })
        {
            Assert.True(h.Scalars.TryGet(name, out var def), name);
            Assert.All(h.Polities, p => Assert.InRange(p.GetScalar(def.Id, h.Scalars), def.Min, def.Max));
        }
        Assert.True(h.Scalars.TryGet(EconomyScalars.Wealth, out var wealth));
        Assert.Contains(h.Polities, p => p.GetScalar(wealth.Id, h.Scalars) != wealth.Default);
    }

    [Fact]
    public void IsDeterministic_AndRoundTrips()
    {
        var a = Build(plugin: true);
        var b = Build(plugin: true);
        Assert.Equal(a.ToBytes(), b.ToBytes());
        Assert.Equal(a.ToBytes(), RoundTrip(a).ToBytes());
    }

    [Fact]
    public void WithoutThePlugin_HistoryIsTheBasePreset()
    {
        var none = Build(plugin: false);
        using var plain = new WorldBuilder().WithSize(4).WithSeed(11).WithUserEdits().Build();
        Assert.Equal(plain.History.ToBytes(), none.ToBytes());
        Assert.False(none.Scalars.TryGet(EconomyScalars.Wealth, out _));
        Assert.Equal(0, none.PluginState.Count);
    }

    [Fact]
    public void TradeLedger_SurvivesSaveAndLoad()
    {
        var h = Build(plugin: true);
        Assert.Equal(PluginStateLookup.Found, h.PluginState.TryRead(TradeLedger.Key, TradeLedger.Version, out _));
        var ledger = TradeLedger.Read(h);
        Assert.NotEmpty(ledger);
        Assert.All(ledger, r =>
        {
            Assert.True(r.Key.A < r.Key.B);
            Assert.Equal(Resources.Kinds, r.Value.Length);
            Assert.All(r.Value, v => Assert.True(v >= 0));
        });
    }

    [Fact]
    public void Unrest_FeedsPressure_SoHistoryDiffersFromTheBase()
    {
        bool differs = false;
        foreach (int seed in new[] { 3, 11, 29 })
            differs |= !Build(plugin: true, seed).ToBytes().AsSpan().SequenceEqual(Build(plugin: false, seed).ToBytes());
        Assert.True(differs);
    }

    [Fact]
    public void ResourceWars_CiteTheirWarAsCause()
    {
        bool seen = false;
        foreach (int seed in new[] { 3, 5, 7, 11, 13, 29, 31 })
        {
            var h = Build(plugin: true, seed);
            foreach (var e in h.Events)
            {
                if (e.PluginKind != FiscalOperator.ResourceWarTag) continue;
                seen = true;
                Assert.Equal(HistoryEventKind.Plugin, e.Kind);
                Assert.True(e.Cause >= 0);
                Assert.Contains(h.Events, w => w.Id == e.Cause && w.Kind == HistoryEventKind.War);
            }
        }
        Assert.True(seen, "no resource war in any probe seed");
    }
}
