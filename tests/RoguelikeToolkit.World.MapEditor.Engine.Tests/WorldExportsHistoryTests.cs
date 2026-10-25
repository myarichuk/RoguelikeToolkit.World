using HistoryPlugin;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

/// <summary>M3.20: history configuration for <c>Generate</c> and the batch history reads a time-scrubbing panel pulls.</summary>
public class WorldExportsHistoryTests : IDisposable
{
    public WorldExportsHistoryTests() { HistoryPluginHost.Clear(); WorldExports.ResetHistoryConfig(); }
    public void Dispose() { HistoryPluginHost.Clear(); WorldExports.ResetHistoryConfig(); }

    private static int Generate(int seed = 7) => WorldExports.Generate(seed, 2, 0, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4);

    [Fact]
    public void ConfigureHistory_SetsTheSpan_WithoutAPlugin()
    {
        WorldExports.ConfigureHistory(true, 18, 25, 3);
        Generate();
        var info = WorldExports.HistoryInfo();
        Assert.Equal(1, info[0]);
        Assert.Equal(17, info[1]);
        Assert.Equal(25, info[2]);
        var polities = WorldExports.HistoryPolities();
        int founders = 0;
        for (int i = 0; i < polities.Length; i += WorldExports.PolityStride)
            if (polities[i + 3] == 0) founders++;
        Assert.InRange(founders, 1, 3);
    }

    [Fact]
    public void ConfigureHistory_Off_KeepsHistoryOff_EvenWithAPluginRegistered()
    {
        HistoryPluginHost.Register(GranaryOperator.Register, GranaryOperator.DeclareScalars);
        WorldExports.ConfigureHistory(false, 30, 10, 8);
        Generate();
        var info = WorldExports.HistoryInfo();
        Assert.Equal(0, info[0]);
        Assert.Equal(0, info[3]);
        Assert.Empty(WorldExports.HistoryOwnershipAt(5));
        Assert.Empty(WorldExports.HistoryPolities());
        Assert.Equal(new[] { 0 }, WorldExports.HistoryEvents(0, 10));
        Assert.Single(WorldExports.HistoryPolitySeries());
    }

    [Fact]
    public void ConfigureHistory_RejectsABadRequest_AndKeepsThePreviousOne()
    {
        WorldExports.ConfigureHistory(true, 12, 10, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.ConfigureHistory(true, -1, 10, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.ConfigureHistory(true, 12, 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.ConfigureHistory(true, 12, 10, -1));
        Generate();
        Assert.Equal(11, WorldExports.HistoryInfo()[1]);
    }

    [Fact]
    public void HistoryPeoples_ListsEveryShippedPeople_WithItsFormAffinity()
    {
        var peoples = WorldExports.HistoryPeoples();
        Assert.Equal(12, peoples.Length);
        var elves = peoples.Single(p => p.StartsWith("elf_wood\telven_wood\t")).Split('\t');
        Assert.Equal("Wood elf", elves[2]);
        Assert.StartsWith("LooseAssociation:55", elves[5]);
        Assert.Equal("", peoples.Single(p => p.StartsWith("human\t")).Split('\t')[5]);   // humans leave the form to the land
        Assert.Equal(new[] { "Kingdom", "MerchantRepublic", "LooseAssociation", "Horde" }, WorldExports.HistoryFormNames());
    }

    [Fact]
    public void ConfigureHistoryFounders_PinsFolkFormAndPlace_ForTheNextWorld()
    {
        WorldExports.ConfigureHistoryPeoples(new[] { "orc" }, new[] { "orcish_horde" });
        WorldExports.ConfigureHistoryFounders(new[] { "orc", "" }, new[] { "orcish_horde", "" }, new[] { 3, -1 }, new[] { 10.0, double.NaN }, new[] { 20.0, double.NaN });
        Generate();
        var polities = WorldExports.HistoryPolities();
        Assert.Equal((int)PolityForm.Horde, polities[1]);                  // polity 0's form
        Assert.StartsWith("orc\torcish_horde", WorldExports.HistoryPolityKeys()[0]);
        Assert.EndsWith("\torc\tfantasy_common\thorde", WorldExports.HistoryPolityKeys()[0]);   // an orc realm names through the orc table, builds as a horde
        Assert.All(Enumerable.Range(0, polities.Length / WorldExports.PolityStride), i =>
            Assert.StartsWith("orc\t", WorldExports.HistoryPolityKeys()[i]));   // the roster is orcs only
    }

    [Fact]
    public void ConfigureHistoryPeoples_RejectsAnUnknownPair_AndKeepsThePreviousRoster()
    {
        WorldExports.ConfigureHistoryPeoples(new[] { "dwarf" }, new[] { "dwarven_clan" });
        Assert.Throws<ArgumentException>(() => WorldExports.ConfigureHistoryPeoples(new[] { "elf_wood" }, new[] { "no_such_culture" }));
        Assert.Throws<ArgumentException>(() => WorldExports.ConfigureHistoryPeoples(new[] { "dwarf" }, Array.Empty<string>()));
        Generate();
        Assert.All(Enumerable.Range(0, WorldExports.HistoryPolityKeys().Length), i => Assert.StartsWith("dwarf\t", WorldExports.HistoryPolityKeys()[i]));
    }

    [Fact]
    public void ConfigureHistoryFounders_RejectsBadArrays()
    {
        Assert.Throws<ArgumentException>(() => WorldExports.ConfigureHistoryFounders(new[] { "" }, new[] { "" }, Array.Empty<int>(), new[] { 0.0 }, new[] { 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.ConfigureHistoryFounders(new[] { "" }, new[] { "" }, new[] { 9 }, new[] { double.NaN }, new[] { double.NaN }));
    }

    [Fact]
    public void ResetHistoryConfig_ClearsTheRosterAndTheFounders()
    {
        WorldExports.ConfigureHistoryPeoples(new[] { "orc" }, new[] { "orcish_horde" });
        WorldExports.ConfigureHistoryFounders(new[] { "orc" }, new[] { "orcish_horde" }, new[] { 3 }, new[] { double.NaN }, new[] { double.NaN });
        WorldExports.ResetHistoryConfig();
        Generate();
        Assert.Contains(WorldExports.HistoryPolityKeys(), k => k.StartsWith("human\t"));
    }

    [Fact]
    public void BasePlusChanges_ReplaysToOwnershipAt_EveryTurn()
    {
        int n = Generate();
        int head = WorldExports.HistoryInfo()[1];
        var owners = WorldExports.HistoryOwnershipBase();
        var changes = WorldExports.HistoryTerritoryChanges();
        int c = 0;
        for (int turn = 0; turn <= head; turn++)
        {
            for (; c < changes.Length && changes[c] <= turn; c += WorldExports.TerritoryChangeStride)
            {
                Assert.InRange(changes[c + 1], 0, n - 1);
                owners[changes[c + 1]] = changes[c + 2];
            }
            Assert.Equal(WorldExports.HistoryOwnershipAt(turn), owners);
        }
        Assert.Equal(changes.Length, c);
    }

    [Fact]
    public void EventPages_ConcatenateToTheWholeLog()
    {
        Generate();
        int total = WorldExports.HistoryInfo()[7];
        var whole = WorldExports.HistoryEventText(0, total);
        var paged = new List<string>();
        for (int start = 0; start < total; start += 7) paged.AddRange(WorldExports.HistoryEventText(start, 7));
        Assert.Equal(whole, paged);
        Assert.Equal(new[] { 0 }, WorldExports.HistoryEvents(total + 5, 10));
        Assert.Empty(WorldExports.HistoryEventText(total, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.HistoryEvents(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.HistoryEvents(0, -1));
    }

    [Fact]
    public void PolitySeries_CarriesThePluginScalar_AndEndsAtTheHead()
    {
        HistoryPluginHost.Register(GranaryOperator.Register, GranaryOperator.DeclareScalars);
        Generate();
        int head = WorldExports.HistoryInfo()[1];
        var series = WorldExports.HistoryPolitySeries();
        int width = (int)series[0], stride = 3 + width;
        Assert.Equal(2, width);   // pressure, then the granary's scalar
        Assert.Contains(GranaryOperator.GrainScalar, WorldExports.HistorySummaryJson());
        double lastTurn = -1;
        bool moved = false;
        for (int o = 1; o < series.Length; o += stride)
        {
            Assert.True(series[o] >= lastTurn, "by turn");
            lastTurn = series[o];
            moved |= series[o + 4] != GranaryOperator.MaxGrain / 2;
        }
        Assert.Equal(head, lastTurn);
        Assert.True(moved, "the plugin scalar never changed across the series");
    }
}
