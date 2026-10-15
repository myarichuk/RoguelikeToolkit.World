using BenchmarkDotNet.Attributes;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Benchmarks;

/// <summary>
/// History-stage cost (orders 26..91) on pre-baked size-6 physics, plus the re-resolution cost.
/// The ownership-map spike evidence: the managed <c>int[]</c> flood fill and scoring live in
/// <c>PolityForm</c>, the edit-path re-resolution in <c>Respatialize</c>.
/// </summary>
[MemoryDiagnoser]
public class HistoryBenchmarks
{
    private const int Size = 6;
    private const int Seed = 42;

    private Core.World _world = null!;
    private HistoryCatalog _seeded = null!;

    /// <summary>Bakes size-6 physics once (without history) and a seeded catalog to re-resolve.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _world = new Core.WorldBuilder().WithSize(Size).WithSeed(Seed).WithoutHistory().Build();
        using var seeded = new Core.WorldBuilder().WithSize(Size).WithSeed(Seed).Build();
        _seeded = seeded.History;
    }

    /// <summary>Disposes the physics host.</summary>
    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>Runs the whole history band (seeds, formation, driver, P2b operators, projection) on the pre-baked physics; repeats the same deterministic work.</summary>
    [Benchmark(Description = "History_Size6")]
    public int History()
    {
        var history = new HistoryCatalog(Seed);
        var context = new HistoryContext();
        _world.Map.SetAttachment(history);
        _world.Map.SetAttachment(context);
        new CultureSeedStage().Execute(_world.Map);
        new PolityFormStage().Execute(_world.Map);
        new HistoryEventsStage().Execute(_world.Map);
        new WarStage().Execute(_world.Map);
        new ConquestStage().Execute(_world.Map);
        new RebellionStage().Execute(_world.Map);
        new ExchangeStage().Execute(_world.Map);
        new CityHistoryProjectStage().Execute(_world.Map);
        return history.Polities.Count;
    }

    /// <summary>Re-resolves the seeded catalog against unchanged physics (the no-op edit path).</summary>
    [Benchmark(Description = "Respatialize_Size6")]
    public bool Respatialize() => _seeded.Respatialize(_world.Map.DataStore);
}

/// <summary>Size-7 history cost (~164k tiles); few iterations.</summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class LargeHistoryBenchmarks
{
    private const int Size = 7;
    private const int Seed = 42;

    private Core.World _world = null!;

    /// <summary>Bakes size-7 physics once (without history).</summary>
    [GlobalSetup]
    public void Setup()
    {
        _world = new Core.WorldBuilder().WithSize(Size).WithSeed(Seed).WithoutHistory().Build();
    }

    /// <summary>Disposes the physics host.</summary>
    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>Runs the whole history band on the pre-baked size-7 physics.</summary>
    [Benchmark(Description = "History_Size7")]
    public int History()
    {
        var history = new HistoryCatalog(Seed);
        var context = new HistoryContext();
        _world.Map.SetAttachment(history);
        _world.Map.SetAttachment(context);
        new CultureSeedStage().Execute(_world.Map);
        new PolityFormStage().Execute(_world.Map);
        new HistoryEventsStage().Execute(_world.Map);
        new WarStage().Execute(_world.Map);
        new ConquestStage().Execute(_world.Map);
        new RebellionStage().Execute(_world.Map);
        new ExchangeStage().Execute(_world.Map);
        new CityHistoryProjectStage().Execute(_world.Map);
        return history.Polities.Count;
    }
}
