using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// A null edit must change nothing, on every seed. The single-seed check in HistoryTests passed because seed 42 has no
/// absorption; absorbed land used to be forced onto the absorber once and then re-flooded away by the next
/// <see cref="HistoryCatalog.Respatialize"/>, which forked history on a plain <c>Recompute()</c>.
/// </summary>
public class RecomputeNullEditTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 30).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void NullEdit_DoesNotFork(int seed)
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().Build();
        var before = (int[])world.History.Ownership.Clone();
        int events = world.History.Events.Count;

        world.Recompute();

        Assert.Null(world.History.ForkedAtTurn);
        Assert.Equal(before, world.History.Ownership);
        Assert.Equal(events, world.History.Events.Count);
    }

    [Fact]
    public void AbsorbedLand_StaysWithTheAbsorber()
    {
        int checkedWorlds = 0;
        for (int seed = 1; seed <= 30; seed++)
        {
            using var world = new WorldBuilder().WithSize(3).WithSeed(seed).Build();
            var h = world.History;
            foreach (var gone in h.Polities.Where(p => p.AbsorbedInto >= 0))
            {
                int heir = h.LivingHeir(gone.Id);
                if (heir < 0) continue; // the absorber itself fell to a collapse
                checkedWorlds++;
                if (h.Polities.All(p => !p.Alive || p.CapitalTile != gone.CapitalTile))
                    Assert.Equal(heir, h.Ownership[gone.CapitalTile]);
            }
        }
        Assert.True(checkedWorlds > 0, "no seed in 1..30 absorbed a polity; widen the sweep");
    }
}
