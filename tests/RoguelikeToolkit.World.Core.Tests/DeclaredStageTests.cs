using System;
using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Runtime-declared stages (the Jint shape) join ordering + contracts.</summary>
public class DeclaredStageTests
{
    private sealed class FakeDeclaredStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed
    {
        public string Name { get; init; } = "fake";
        public int Order { get; init; }
        public Type[] Reads { get; init; } = Array.Empty<Type>();
        public Type[] Writes { get; init; } = Array.Empty<Type>();
        public Type[] ReadsOptional { get; init; } = Array.Empty<Type>();
        public List<string> Log { get; }
        public FakeDeclaredStage(List<string> log) => Log = log;
        public void Execute(WorldMap map) => Log.Add(Name);
    }

    [WorldGeneratorStage(10, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class AttrWriter10 : IWorldGeneratorStage
    {
        public void Execute(WorldMap map) { }
    }

    [Fact]
    public void DeclaredStage_BlindWriteConflict_Throws()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new AttrWriter10());
        pipeline.AddStage(new FakeDeclaredStage(new List<string>())
        {
            Name = "script",
            Order = 11,
            Writes = new[] { typeof(ElevationInfo) },
        });

        Assert.Throws<InvalidOperationException>(() => pipeline.ValidateContracts());
    }

    [Fact]
    public void DeclaredStage_RefinementChain_IsAllowed()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new AttrWriter10());
        pipeline.AddStage(new FakeDeclaredStage(new List<string>())
        {
            Name = "script",
            Order = 11,
            Reads = new[] { typeof(ElevationInfo) },
            Writes = new[] { typeof(ElevationInfo) },
        });

        pipeline.ValidateContracts();
    }

    [Fact]
    public void DeclaredStage_RunsInOrder()
    {
        using var map = new WorldMap(0);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();

        var log = new List<string>();
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new FakeDeclaredStage(log) { Name = "late", Order = 50 });
        pipeline.AddStage(new FakeDeclaredStage(log) { Name = "early", Order = 5 });

        pipeline.Execute(map);

        Assert.Equal(new[] { "early", "late" }, log);
    }

    [Fact]
    public void DeclaredStage_DuplicateOrderWithDiscovered_ThrowsOnDiscover()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new FakeDeclaredStage(new List<string>()) { Name = "script-10", Order = 10 });

        // Core ships TectonicPlateGenerationStage at order 10.
        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.Discover());
        Assert.Contains("Duplicate Order values", ex.Message);
        Assert.Contains("10", ex.Message);
    }

    [Fact]
    public void DeclaredStage_MissingLayer_ThrowsWithStageName()
    {
        using var map = new WorldMap(0);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new FakeDeclaredStage(new List<string>())
        {
            Name = "aridity-js",
            Order = 19,
            Reads = new[] { typeof(ClimateInfo) },
            Writes = new[] { typeof(ClimateInfo) },
        });

        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.ValidateContracts(map));
        Assert.Contains("aridity-js", ex.Message);
    }
}
