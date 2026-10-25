using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Invariants over many seeds for the review's section 2 history defects (batch F).</summary>
public class HistoryIntegrityTests
{
    private static IEnumerable<World> Worlds(int count = 30)
    {
        for (int seed = 1; seed <= count; seed++)
            yield return new WorldBuilder().WithSize(3).WithSeed(seed).Build();
    }

    [Fact]
    public void EveryLivingPolity_SitsInAStandingCapital()
    {
        int successors = 0;
        foreach (var world in Worlds())
        {
            using (world)
            {
                var h = world.History;
                foreach (var p in h.Polities.Where(p => p.Alive))
                {
                    if (p.Parent >= 0 && h.Polities[p.Parent].EndedTurn == p.FoundedTurn) successors++;
                    var seat = h.Settlements.FirstOrDefault(s => !s.Ended && s.TileIndex == p.CapitalTile && s.PolityId == p.Id);
                    if (seat == null) continue; // a sacked-out seat is a separate matter
                    Assert.True(seat.Role == SettlementRole.Capital, $"seed {world.Seed}: polity {p.Id} seat is a {seat.Role}");
                    Assert.True(seat.Tier >= TierOperator.CapitalFloor, $"seed {world.Seed}: polity {p.Id} seat is a {seat.Tier}");
                }
            }
        }
        Assert.True(successors > 0, "no collapse successor in the sweep");
    }

    [Fact]
    public void DisplacedPeople_NeverBelongToAPolityWithAnHeir()
    {
        foreach (var world in Worlds())
        {
            using (world)
            {
                var h = world.History;
                foreach (var g in h.Displaced.Where(g => g.Amount > 0))
                {
                    var p = h.Polities[g.PolityId];
                    if (p.Alive) continue;
                    bool heir = h.LivingHeir(p.Id) >= 0
                        || h.Polities.Any(q => q.Alive && q.Parent == p.Id && q.FoundedTurn == p.EndedTurn);
                    Assert.False(heir, $"seed {world.Seed}: group of dead polity {p.Id} was not rehomed");
                }
            }
        }
    }

    [Fact]
    public void SuccessorCount_IgnoresRebels()
    {
        var h = new HistoryCatalog(1);
        h.Polities.Add(new Polity { Id = 0, EndedTurn = 10 });                    // falls at turn 10
        h.Polities.Add(new Polity { Id = 1, Parent = 0, FoundedTurn = 5 });      // rebelled while it stood
        h.Polities.Add(new Polity { Id = 2, Parent = 0, FoundedTurn = 10 });     // founded by the collapse
        Assert.Equal(1, h.SuccessorCount());
        Assert.Equal(1, h.RebelCount());
    }

    [Fact]
    public void RulerGenerations_CountFromFounding()
    {
        foreach (var world in Worlds(15))
        {
            using (world)
            {
                var h = world.History;
                foreach (var p in h.Polities.Where(p => p.FoundedTurn > 0 && !p.HasVillain))
                    Assert.True(p.RulerGeneration <= h.SimulatedTurns - p.FoundedTurn + 1,
                        $"seed {world.Seed}: polity {p.Id} founded at {p.FoundedTurn} has generation {p.RulerGeneration}");
            }
        }
    }
}
