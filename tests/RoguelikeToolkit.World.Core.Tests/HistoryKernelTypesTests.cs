using RoguelikeToolkit.World.Core.History;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M1.1: additive kernel types (interned kinds, event links, importance compaction, polity lifecycle).</summary>
public class HistoryKernelTypesTests
{
    [Fact]
    public void StringTable_SeedsBuiltInKinds_WithEnumIds()
    {
        var table = new StringTable();
        foreach (var kind in Enum.GetValues<HistoryEventKind>())
            Assert.Equal((int)kind, table.Intern(kind.ToString()));
        Assert.Equal(EventKinds.War.Id, (int)HistoryEventKind.War);
        Assert.Equal(nameof(HistoryEventKind.Myth), table.NameOf(EventKinds.Myth.Id));
    }

    [Fact]
    public void StringTable_InternsCustomNames_DenseAndStable()
    {
        var table = new StringTable();
        int first = table.Intern("plague");
        Assert.Equal(first, table.Intern("plague"));
        Assert.Equal(first + 1, table.Intern("famine"));
        Assert.True(table.TryGetId("famine", out _));
        Assert.False(table.TryGetId("flood", out _));
    }

    [Fact]
    public void Events_GetStableIds_AndImportanceFromKind()
    {
        var history = new HistoryCatalog(1);
        history.AppendEvent(HistoryEventKind.Founding, 0, 0, 1, new[] { "a" });
        history.AppendEvent(HistoryEventKind.Exchange, 1, 0, -1, new[] { "b" });
        history.AppendEvent(HistoryEventKind.War, 1, 0, 2, new[] { "c" });
        Assert.Equal(new[] { 0, 1, 2 }, history.Events.Select(e => e.Id).ToArray());
        Assert.Equal(EventImportance.Founding, history.Events[0].Importance);
        Assert.Equal(EventImportance.Minor, history.Events[1].Importance);
        Assert.Equal(EventImportance.Structural, history.Events[2].Importance);
    }

    [Fact]
    public void Compaction_DropsMinorBeforeStructural_AndKeepsFounding()
    {
        var history = new HistoryCatalog(1);
        history.SyncBudgets(64, 4);
        history.AppendEvent(HistoryEventKind.Founding, 0, 0, 1, new[] { "f" });
        history.AppendEvent(HistoryEventKind.War, 1, 0, 2, new[] { "war" });
        history.AppendEvent(HistoryEventKind.Exchange, 2, 0, -1, new[] { "x1" });
        history.AppendEvent(HistoryEventKind.Exchange, 3, 0, -1, new[] { "x2" });
        history.AppendEvent(HistoryEventKind.Exchange, 4, 0, -1, new[] { "x3" });
        Assert.Equal(4, history.Events.Count);
        Assert.Contains(history.Events, e => e.Kind == HistoryEventKind.War);
        Assert.Contains(history.Events, e => e.Kind == HistoryEventKind.Founding);
        Assert.DoesNotContain(history.Events, e => e.Tags.Contains("x1"));
    }

    [Fact]
    public void Compaction_ProtectsAnEventCitedAsCause()
    {
        var history = new HistoryCatalog(1);
        history.SyncBudgets(64, 3);
        int cause = history.AppendLinked(HistoryEventKind.Exchange, 1, 0, -1, new[] { "cause" }, -1, null);
        history.AppendLinked(HistoryEventKind.War, 2, 0, 2, new[] { "effect" }, cause, null);
        history.AppendEvent(HistoryEventKind.Exchange, 3, 0, -1, new[] { "other" });
        history.AppendEvent(HistoryEventKind.Exchange, 4, 0, -1, new[] { "newest" });
        Assert.Equal(3, history.Events.Count);
        Assert.Contains(history.Events, e => e.Id == cause);
        Assert.DoesNotContain(history.Events, e => e.Tags.Contains("other"));
    }

    [Fact]
    public void Compaction_StillHoldsTheCap_WhenEverythingIsCited()
    {
        var history = new HistoryCatalog(1);
        history.SyncBudgets(64, 2);
        int a = history.AppendLinked(HistoryEventKind.War, 1, 0, 1, Array.Empty<string>(), -1, null);
        int b = history.AppendLinked(HistoryEventKind.War, 2, 0, 1, Array.Empty<string>(), a, null);
        history.AppendLinked(HistoryEventKind.War, 3, 0, 1, Array.Empty<string>(), b, null);
        Assert.Equal(2, history.Events.Count);
    }

    [Fact]
    public void Participants_RoundTripThroughAppend()
    {
        var history = new HistoryCatalog(1);
        int attacker = history.Strings.Intern("attacker");
        int id = history.AppendLinked(HistoryEventKind.War, 1, 0, 1, Array.Empty<string>(), -1,
            new[] { new EventParticipant(0, attacker), new EventParticipant(1, history.Strings.Intern("defender")) });
        var e = history.Events.Single(x => x.Id == id);
        Assert.Equal(2, e.Participants.Count);
        Assert.Equal("attacker", history.Strings.NameOf(e.Participants[0].Role));
    }

    [Fact]
    public void Polity_DefaultsToAlive_WithNoParent()
    {
        var polity = new Polity { Id = 0 };
        Assert.True(polity.Alive);
        Assert.Equal(-1, polity.Parent);
        polity.EndedTurn = 5;
        Assert.False(polity.Alive);
    }
}
