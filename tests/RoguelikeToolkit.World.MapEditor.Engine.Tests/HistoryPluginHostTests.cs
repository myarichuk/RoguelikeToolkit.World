using HistoryPlugin;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.MapEditor.Engine;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

/// <summary>M3.13: a host registers the sample history plugin through the engine and the base history is untouched without it.</summary>
public class HistoryPluginHostTests : IDisposable
{
    public HistoryPluginHostTests() => HistoryPluginHost.Clear();
    public void Dispose() => HistoryPluginHost.Clear();

    private static HistoryCatalog Build(bool plugin, int seed = 11)
    {
        HistoryPluginHost.Clear();
        if (plugin) HistoryPluginHost.Register(GranaryOperator.Register, GranaryOperator.DeclareScalars);
        var builder = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits();
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
    public void Plugin_DeclaresItsScalar_AndAdjustsIt()
    {
        var h = Build(plugin: true);
        Assert.True(h.Scalars.TryGet(GranaryOperator.GrainScalar, out var grain));
        Assert.Contains(h.Polities, p => p.GetScalar(grain.Id, h.Scalars) != grain.Default);
    }

    [Fact]
    public void Plugin_IsDeterministic_AndRoundTrips()
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
        using var plain = new WorldBuilder().WithSize(3).WithSeed(11).WithUserEdits().Build();
        Assert.Equal(plain.History.ToBytes(), none.ToBytes());
        Assert.False(none.Scalars.TryGet(GranaryOperator.GrainScalar, out _));
    }

    [Fact]
    public void Generate_RunsRegisteredPlugins()
    {
        HistoryPluginHost.Register(GranaryOperator.Register, GranaryOperator.DeclareScalars);
        Assert.True(WorldExports.Generate(7, 2, 0, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4) > 0);
    }
}
