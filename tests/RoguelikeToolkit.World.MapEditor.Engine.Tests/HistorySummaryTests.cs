using System.Text.Json;
using EconomyPlugin;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.MapEditor.Engine;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

/// <summary>The engine's plugin-agnostic history summary for the editor: scalars, per-polity values, plugin events and state keys.</summary>
public class HistorySummaryTests : IDisposable
{
    public HistorySummaryTests() => HistoryPluginHost.Clear();
    public void Dispose() => HistoryPluginHost.Clear();

    private static JsonElement Summarize(bool economy)
    {
        HistoryPluginHost.Clear();
        if (economy) HistoryPluginHost.Register(Economy.Register, Economy.DeclareScalars);
        var builder = new WorldBuilder().WithSize(4).WithSeed(3).WithUserEdits().WithHistory();
        HistoryPluginHost.Apply(builder);
        using var world = builder.Build();
        return JsonDocument.Parse(HistorySummary.ToJson(world.History)).RootElement.Clone();
    }

    [Fact]
    public void WithoutAPlugin_OnlyPressureIsDeclared_AndNoPluginData()
    {
        var s = Summarize(economy: false);
        Assert.Equal("pressure", Assert.Single(s.GetProperty("scalars").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(0, s.GetProperty("pluginState").GetArrayLength());
        Assert.Equal(0, s.GetProperty("pluginEvents").GetArrayLength());
        Assert.True(s.GetProperty("polities").GetArrayLength() > 0);
    }

    [Fact]
    public void EconomyPlugin_ShowsItsScalars_PerPolity_AndItsTradeLedgerKey()
    {
        var s = Summarize(economy: true);
        var names = s.GetProperty("scalars").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("econ/wealth", names);
        Assert.Contains("econ/unrest", names);
        foreach (var p in s.GetProperty("polities").EnumerateArray())
            Assert.Equal(names.Count, p.GetProperty("values").GetArrayLength());
        Assert.Contains(s.GetProperty("pluginState").EnumerateArray(), k => k.GetString() == TradeLedger.Key);
    }
}
