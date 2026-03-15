using System;
using System.Linq;
using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

public class PipelineTests
{
    [Fact]
    public void Discover_LoadsStagesInOrder()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.Discover();

        var stages = pipeline.Stages;

        Assert.True(stages.Count >= 2);

        var tectonicStage = stages.OfType<TectonicPlateGenerationStage>().FirstOrDefault();
        var localMapStage = stages.OfType<LocalMapGenerationStage>().FirstOrDefault();

        Assert.NotNull(tectonicStage);
        Assert.NotNull(localMapStage);

        var tectonicIndex = Array.IndexOf(stages.ToArray(), tectonicStage);
        var localMapIndex = Array.IndexOf(stages.ToArray(), localMapStage);

        Assert.True(tectonicIndex < localMapIndex);
    }
}
