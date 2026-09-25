using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

public class ErosionTests
{
    private static ElevationInfo[] ElevationsAfterErosion(int size, int seed, int seedCount, int iterations)
    {
        using var map = new WorldMap(size);
        using var tectonicLayer = new TectonicPlateLayer(map.DataStore, seedCount);
        map.RegisterLayer(tectonicLayer);
        using var elevationLayer = new ElevationLayer(map.DataStore);
        map.RegisterLayer(elevationLayer);
        map.DataStore.Allocate();

        var setup = new WorldGenerationPipeline();
        setup.AddStage(new TectonicPlateGenerationStage(seedCount, seed));
        setup.AddStage(new ElevationGenerationStage(seed));
        setup.Execute(map);

        var erosion = new ErosionGenerationStage(iterations);
        erosion.Execute(map);

        return map.DataStore.GetSpan<ElevationInfo>().ToArray();
    }

    private static double Variance(ElevationInfo[] values)
    {
        double mean = 0;
        foreach (var v in values) mean += v.Height;
        mean /= values.Length;
        double var = 0;
        foreach (var v in values) var += (v.Height - mean) * (v.Height - mean);
        return var / values.Length;
    }

    [Fact]
    public void Erosion_DoesNotIncreaseElevationVariance()
    {
        var uneroded = ElevationsAfterErosion(3, 42, 12, 0);
        var eroded = ElevationsAfterErosion(3, 42, 12, 10);

        Assert.True(Variance(eroded) <= Variance(uneroded),
            $"Eroded variance {Variance(eroded)} exceeds uneroded {Variance(uneroded)}");
    }

    [Fact]
    public void Erosion_PreservesTotalMass()
    {
        var uneroded = ElevationsAfterErosion(2, 42, 12, 0);
        var eroded = ElevationsAfterErosion(2, 42, 12, 10);

        double sumBefore = 0, sumAfter = 0;
        for (int i = 0; i < uneroded.Length; i++)
        {
            sumBefore += uneroded[i].Height;
            sumAfter += eroded[i].Height;
        }

        Assert.Equal(sumBefore, sumAfter, 3);
    }
}
