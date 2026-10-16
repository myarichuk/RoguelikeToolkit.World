using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.9: wars reach settlements: sack, raze, capture.</summary>
public class HistoryWarEffectsTests
{
    [Fact]
    public void Wars_ReachSettlements_AndLeaveConsistentRecords()
    {
        int captured = 0, razed = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory(c => c.HazardRate = 0).Build();
            var h = w.History;
            foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.SettlementCaptured))
            {
                captured++;
                int index = int.Parse(e.Tags.First(t => t.StartsWith("settlement:")).Substring(11));
                int from = int.Parse(e.Tags.First(t => t.StartsWith("from:")).Substring(5));
                Assert.NotEqual(from, e.PolityId);
                var s = h.Settlements[index];
                Assert.Equal(e.TileIndex, s.TileIndex);
                Assert.NotEqual(SettlementRole.Capital, s.Role);
            }
            // A collapsing polity's seat is razed too, with a "collapse" ruin; wars never raze a capital.
            foreach (var s in h.Settlements.Where(s => s.EndCause == "razed" && s.Role != SettlementRole.Capital))
            {
                razed++;
                Assert.Contains(h.Scars, sc => sc.TileIndex == s.TileIndex && sc.Kind == HistoryScar.Ruin && sc.Cause == "war");
            }
        }
        Assert.True(captured + razed > 0, "No settlement was captured or razed across seeds 1-12.");
    }
}
