using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class PipelineContractTests
{
    [WorldGeneratorStage(30, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class BlindWriterA : IWorldGeneratorStage
    {
        public void Execute(WorldMap map) { }
    }

    [WorldGeneratorStage(31, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class BlindWriterB : IWorldGeneratorStage
    {
        public void Execute(WorldMap map) { }
    }

    [WorldGeneratorStage(5, Reads = new[] { typeof(TectonicPlate) })]
    private sealed class EarlyReader : IWorldGeneratorStage
    {
        public void Execute(WorldMap map) { }
    }

    [WorldGeneratorStage(32, Reads = new[] { typeof(ElevationInfo) }, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class RefiningWriter : IWorldGeneratorStage
    {
        public void Execute(WorldMap map) { }
    }

    private sealed class UndeclaredStage : IWorldGeneratorStage
    {
        public void Execute(WorldMap map) { }
    }

    [Fact]
    public void BlindWriteCollision_ThrowsOnExecute()
    {
        using var map = new WorldMap(1);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new BlindWriterA());
        pipeline.AddStage(new BlindWriterB());

        Assert.Throws<InvalidOperationException>(() => pipeline.Execute(map));
    }

    [Fact]
    public void RefinementChain_ReadModifyWrite_IsAllowed()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new TectonicPlateGenerationStage(4, 42));
        pipeline.AddStage(new ElevationGenerationStage(42));
        pipeline.AddStage(new ErosionGenerationStage());
        pipeline.AddStage(new RefiningWriter());

        // Must not throw: every ElevationInfo writer after the first reads it too.
        pipeline.ValidateContracts();
    }

    [Fact]
    public void ReadBeforeWrite_ThrowsOrderingConflict()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new EarlyReader());
        pipeline.AddStage(new TectonicPlateGenerationStage(4, 42));

        Assert.Throws<InvalidOperationException>(() => pipeline.ValidateContracts());
    }

    [Fact]
    public void UndeclaredStages_SkipValidation()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new UndeclaredStage());
        pipeline.AddStage(new UndeclaredStage());

        pipeline.ValidateContracts();
    }

    [Fact]
    public void DeclaredLayer_MissingRegistration_ThrowsWithStageName()
    {
        using var map = new WorldMap(1);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        // Reads TectonicPlate which is not registered.
        pipeline.AddStage(new ElevationGenerationStage(42));

        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.ValidateContracts(map));
        Assert.Contains("ElevationGenerationStage", ex.Message);
    }
}
