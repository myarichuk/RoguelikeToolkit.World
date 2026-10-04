using BenchmarkDotNet.Attributes;
using RoguelikeToolkit.World.Core.Entities;

namespace RoguelikeToolkit.World.Benchmarks;

/// <summary>300k entities on a size-7 planet (163,842 tiles): the "hundreds of thousands of NPCs" target.</summary>
[MemoryDiagnoser]
public class EntityBenchmarks
{
    private const int Npcs = 300_000;
    private const int Tiles = 163_842;

    private struct Place { public int Tile; public ushort X, Y; }
    private struct Person { public ushort Species, Faction; public byte Age; public uint NameSeed; }

    private EntityStore _store = null!;
    private EntityStore.Column<Place> _place = null!;
    private EntityStore.Column<Person> _person = null!;
    private readonly CellIndex _index = new();
    private struct TileOf : ICellOf<Place> { public int Cell(in Place p) => p.Tile; }

    [GlobalSetup]
    public void Setup()
    {
        _store = new EntityStore(Npcs);
        _place = _store.AddColumn<Place>();
        _person = _store.AddColumn<Person>();
        var rng = new Random(7);
        for (int i = 0; i < Npcs; i++)
        {
            var id = _store.Create();
            _place[id] = new Place { Tile = rng.Next(Tiles) };
            _person[id] = new Person { Age = (byte)rng.Next(80), NameSeed = (uint)rng.Next() };
        }
        _index.Rebuild(Tiles, _store, _place, new TileOf());
    }

    [Benchmark(Description = "Create 300k")]
    public int CreateAll()
    {
        var s = new EntityStore(Npcs);
        s.AddColumn<Place>();
        s.AddColumn<Person>();
        for (int i = 0; i < Npcs; i++) s.Create();
        return s.Count;
    }

    [Benchmark(Description = "Per-tick: rebuild tile index")]
    public int RebuildIndex()
    {
        _index.Rebuild(Tiles, _store, _place, new TileOf());
        return _index.Count;
    }

    [Benchmark(Description = "Per-tick: age every NPC (column scan)")]
    public int AgeAll()
    {
        int old = 0;
        for (int k = 0; k < _person.ChunkCount; k++)
        {
            var people = _person.Chunk(k);
            for (int i = 0; i < people.Length; i++)
                if (people[i].Age++ > 90) old++;
        }
        return old;
    }

    [Benchmark(Description = "Query: NPCs in tile + 6 neighbours")]
    public int NeighbourhoodQuery()
    {
        int total = _index.InCell(1000).Length;
        // adjacency lives on the planet store; here we just touch 6 adjacent cells.
        for (int k = 1; k <= 6; k++) total += _index.InCell(1000 + k).Length;
        return total;
    }

    [Benchmark(Description = "Spawn 1 NPC on demand (keyed, steady state)")]
    public EntityId SpawnOne()
    {
        // Pre-sized so the measured cost is the steady-state path, not first growth.
        var id = _store.Create();
        _person[id] = new Person { NameSeed = 1 };
        _store.Destroy(id);
        return id;
    }

    [Benchmark(Description = "Snapshot save 300k (memory)")]
    public long Save()
    {
        using var ms = new MemoryStream(Npcs * 24);
        _store.Save(ms);
        return ms.Length;
    }

    [Benchmark(Description = "Delta save 300k (1 chunk touched)")]
    public long SaveDelta()
    {
        if (!_store.CanSaveDelta) _store.Save(Stream.Null);
        _person.At(5).Age++;
        using var ms = new MemoryStream();
        _store.SaveDelta(ms);
        return ms.Length;
    }

    private readonly struct RingAdj : IAdjacency
    {
        public int GetAdjacent(int cell, Span<int> n)
        {
            n[0] = (cell + 1) % Tiles; n[1] = (cell + Tiles - 1) % Tiles; n[2] = (cell + 313) % Tiles;
            n[3] = (cell + Tiles - 313) % Tiles; n[4] = (cell + 314) % Tiles; n[5] = (cell + Tiles - 314) % Tiles;
            return 6;
        }
    }

    private readonly struct Demo : IPopulationPolicy
    {
        public double BirthRate(int cell, int group, uint count) => 0.002;
        public double DeathRate(int cell, int group, uint count) => 0.0015;
        public double MigrationRate(int cell, int group, uint count) => 0.01;
        public double Attraction(int cell, int group) => 1 + (cell & 3);
    }

    private PopulationGrid? _pop;

    [Benchmark(Description = "Aggregate tick: 163k tiles x 8 groups (~650k people)")]
    public long AggregateTick()
    {
        if (_pop is null)
        {
            _pop = new PopulationGrid(Tiles, 8);
            var rng = new Random(3);
            for (int c = 0; c < Tiles; c++)
                if (rng.Next(3) == 0)    // two thirds of the planet is empty
                    for (int g = 0; g < 8; g++) _pop.Give(c, g, (uint)rng.Next(0, 4));
        }
        _pop.Step(new Demo(), new RingAdj(), 1);
        return _pop.Total();
    }
}
