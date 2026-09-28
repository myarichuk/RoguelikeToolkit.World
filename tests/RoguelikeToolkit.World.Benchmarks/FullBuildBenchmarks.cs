using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace RoguelikeToolkit.World.Benchmarks;

/// <summary>
/// Full-pipeline builds (<see cref="Core.WorldBuilder"/> defaults, seed 42).
/// Sizes 3–6 fit BenchmarkDotNet's default job; size 7 lives in
/// <see cref="LargeBuildBenchmarks"/> with a reduced job so local runs stay sane.
/// </summary>
[MemoryDiagnoser]
public class FullBuildBenchmarks
{
    [Params(3, 4, 5, 6)]
    public int Size { get; set; }

    [Benchmark(Description = "FullBuild")]
    public int FullBuild()
    {
        using var world = new Core.WorldBuilder().WithSize(Size).WithSeed(42).Build();
        return world.TileCount;
    }
}

/// <summary>Size 7 full build (~5 s, ~23 MB managed per backlog §C): few iterations.</summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class LargeBuildBenchmarks
{
    [Benchmark(Description = "FullBuild_Size7")]
    public int FullBuildSize7()
    {
        using var world = new Core.WorldBuilder().WithSize(7).WithSeed(42).Build();
        return world.TileCount;
    }
}
