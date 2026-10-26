using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>The store is native memory or a mapped file with no finalizer: a failed build or allocation must release it.</summary>
public class BuildFailureCleanupTests
{
    [WorldGeneratorStage(40)]
    private sealed class ThrowingStage : IWorldGeneratorStage
    {
        public WorldMap? Seen { get; private set; }

        public void Execute(WorldMap map)
        {
            Seen = map;
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void FailedBuild_ReleasesTheStore()
    {
        var stage = new ThrowingStage();
        Assert.Throws<InvalidOperationException>(() => new WorldBuilder().WithSize(2).WithSeed(1).AddStage(stage).Build());
        Assert.NotNull(stage.Seen);
        var ex = Assert.Throws<InvalidOperationException>(() => stage.Seen!.DataStore.GetSpan<ElevationInfo>());
        Assert.Contains("not allocated", ex.Message);
    }

    [Fact]
    public void FailedAllocate_DoesNotLookAllocated()
    {
        string path = $"alloc-rollback-{Guid.NewGuid():N}.store";
        try
        {
            using (var first = new WorldDataStore(2, path))
            {
                first.RegisterLayer<ElevationInfo>();
                first.Allocate();
            }

            // Same size and byte count, different layer: the header check rejects it after the file is mapped.
            using var second = new WorldDataStore(2, path);
            second.RegisterLayer<SameSizeAsElevation>();
            Assert.Throws<InvalidDataException>(() => second.Allocate());
            Assert.Throws<InvalidDataException>(() => second.Allocate()); // not a silent no-op on retry
            Assert.Throws<InvalidOperationException>(() => second.GetSpan<SameSizeAsElevation>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private struct SameSizeAsElevation
    {
#pragma warning disable CS0649
        public float Value;
#pragma warning restore CS0649
    }
}
