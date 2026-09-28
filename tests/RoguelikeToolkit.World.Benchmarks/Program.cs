using BenchmarkDotNet.Running;
using RoguelikeToolkit.World.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(FullBuildBenchmarks).Assembly).Run(args);
