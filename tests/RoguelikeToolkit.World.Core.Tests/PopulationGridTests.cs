using RoguelikeToolkit.World.Core.Entities;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class PopulationGridTests
{
    /// <summary>A ring of cells: cell i touches i-1 and i+1.</summary>
    private readonly struct Ring(int n) : IAdjacency
    {
        public int GetAdjacent(int cell, Span<int> neighbors)
        {
            neighbors[0] = (cell + n - 1) % n;
            neighbors[1] = (cell + 1) % n;
            return 2;
        }
    }

    private readonly struct Rates(double birth, double death, double migrate, int attractive = -1) : IPopulationPolicy
    {
        public double BirthRate(int cell, int group, uint count) => birth;
        public double DeathRate(int cell, int group, uint count) => death;
        public double MigrationRate(int cell, int group, uint count) => migrate;
        public double Attraction(int cell, int group) => attractive < 0 || cell == attractive ? 1 : 0;
    }

    private static PopulationGrid Seeded(int cells, int groups)
    {
        var grid = new PopulationGrid(cells, groups);
        var rng = new Random(11);
        for (int c = 0; c < cells; c++)
            for (int g = 0; g < groups; g++)
                grid.Give(c, g, (uint)rng.Next(0, 400));
        return grid;
    }

    [Fact]
    public void Migration_ConservesPeopleExactly()
    {
        var grid = Seeded(200, 3);
        long before = grid.Total();
        for (int i = 0; i < 50; i++) grid.Step(new Rates(0, 0, 0.3), new Ring(200), seed: 5);
        Assert.Equal(before, grid.Total());
        Assert.Equal(50u, grid.Tick);
    }

    [Fact]
    public void Migrants_GoOnlyWhereAttractive()
    {
        var grid = new PopulationGrid(5, 1);
        grid.Give(2, 0, 1000);
        grid.Step(new Rates(0, 0, 1.0, attractive: 3), new Ring(5), seed: 1);
        Assert.Equal(1000u, grid.Get(3, 0));
        Assert.Equal(0u, grid.Get(2, 0));
        Assert.Equal(0u, grid.Get(1, 0));
    }

    [Fact]
    public void NoAttractiveNeighbour_MeansTheyStay()
    {
        var grid = new PopulationGrid(3, 1);
        grid.Give(1, 0, 50);
        grid.Step(new Rates(0, 0, 1.0, attractive: 99), new Ring(3), seed: 1);
        Assert.Equal(50u, grid.Get(1, 0));
    }

    [Fact]
    public void BirthsAndDeaths_TrackTheExpectedRates()
    {
        var grid = new PopulationGrid(1000, 1);
        for (int c = 0; c < 1000; c++) grid.Give(c, 0, 1000);
        grid.Step(new Rates(0.10, 0.04, 0), new Ring(1000), seed: 9);
        double expected = 1_000_000 * 1.06;
        Assert.InRange(grid.Total(), expected * 0.999, expected * 1.001);
    }

    [Fact]
    public void Extinction_NeverGoesNegative()
    {
        var grid = Seeded(20, 2);
        for (int i = 0; i < 5; i++) grid.Step(new Rates(0, 1.0, 1.0), new Ring(20), seed: 2);
        Assert.Equal(0, grid.Total());
    }

    [Fact]
    public void Tick_IsDeterministic_AndSeedDependent()
    {
        static uint[] Run(int seed)
        {
            var g = Seeded(300, 2);
            for (int i = 0; i < 10; i++) g.Step(new Rates(0.05, 0.03, 0.2), new Ring(300), seed);
            var all = new List<uint>();
            for (int c = 0; c < 300; c++) all.AddRange(g.Cell(c).ToArray());
            return all.ToArray();
        }
        Assert.Equal(Run(3), Run(3));
        Assert.NotEqual(Run(3), Run(4));
    }

    [Fact]
    public void SubsetStep_LeavesOtherCellsAlone()
    {
        var grid = Seeded(50, 1);
        uint far = grid.Get(40, 0);
        grid.Step(new Rates(0.5, 0, 0), new Ring(50), seed: 1, cells: new[] { 0, 1, 2 });
        Assert.Equal(far, grid.Get(40, 0));
    }

    [Fact]
    public void SaveLoad_RoundTrips_AndResumesIdentically()
    {
        var a = Seeded(100, 3);
        for (int i = 0; i < 4; i++) a.Step(new Rates(0.05, 0.02, 0.1), new Ring(100), seed: 8);

        using var ms = new MemoryStream();
        a.Save(ms);
        ms.Position = 0;
        var b = new PopulationGrid(100, 3);
        b.Load(ms);
        Assert.Equal(a.Tick, b.Tick);

        for (int i = 0; i < 4; i++)
        {
            a.Step(new Rates(0.05, 0.02, 0.1), new Ring(100), seed: 8);
            b.Step(new Rates(0.05, 0.02, 0.1), new Ring(100), seed: 8);
        }
        for (int c = 0; c < 100; c++) Assert.Equal(a.Cell(c).ToArray(), b.Cell(c).ToArray());

        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => new PopulationGrid(100, 2).Load(ms));
    }

    [Fact]
    public void Step_AllocatesNothing()
    {
        var grid = Seeded(2000, 4);
        var adj = new Ring(2000);
        var policy = new Rates(0.02, 0.01, 0.1);
        grid.Step(policy, adj, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        grid.Step(policy, adj, 1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private struct Npc { public ushort Group; public uint Seed; }

    [Fact]
    public void Materialize_ThenDematerialize_ConservesHeadCount()
    {
        var grid = new PopulationGrid(10, 2);
        grid.Give(4, 1, 500);

        var store = new EntityStore();
        var npc = store.AddColumn<Npc>();
        var map = new EntityKeyMap();

        // The player arrives: take 30 out of the aggregate and spawn them under deterministic keys.
        uint taken = grid.Take(4, 1, 30);
        var spawned = new List<EntityId>();
        for (int i = 0; i < taken; i++)
        {
            var id = store.Create();
            npc[id] = new Npc { Group = 1, Seed = (uint)i };
            map.Set(EntityKeyMap.MakeKey(7, 4, i), id);
            spawned.Add(id);
        }
        Assert.Equal(470u, grid.Get(4, 1));

        // The player leaves: some died while visible; survivors are absorbed back.
        store.Destroy(spawned[0]);
        foreach (var id in spawned.Skip(1)) { store.Destroy(id); grid.Give(4, npc.At(0).Group, 1); }
        Assert.Equal(499L, grid.Total());
    }
}
