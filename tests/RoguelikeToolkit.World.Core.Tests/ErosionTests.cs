using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

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

    // Diffusion + glacial only: thermal, fluvial and aeolian off, so the only movers are the two passes
    // that used to sweep in place.
    private static (ElevationInfo[] Before, ElevationInfo[] After) CreepAndIce(bool orderIndependent)
    {
        using var map = new WorldMap(5);
        using var tectonicLayer = new TectonicPlateLayer(map.DataStore, 12);
        map.RegisterLayer(tectonicLayer);
        using var elevationLayer = new ElevationLayer(map.DataStore);
        map.RegisterLayer(elevationLayer);
        map.DataStore.Allocate();

        var setup = new WorldGenerationPipeline();
        setup.AddStage(new TectonicPlateGenerationStage(12, 7));
        setup.AddStage(new ElevationGenerationStage(7));
        setup.Execute(map);
        var before = map.DataStore.GetSpan<ElevationInfo>().ToArray();

        new ErosionGenerationStage(0)
        {
            HydraulicIterations = 0,
            AeolianIterations = 0,
            DiffusionIterations = 3,
            DiffusionRate = 0.1,
            GlacialIterations = 3,
            OrderIndependent = orderIndependent,
        }.Execute(map);
        return (before, map.DataStore.GetSpan<ElevationInfo>().ToArray());
    }

    [Fact]
    public void OrderIndependentCreepAndIce_ConservesMassAndDiffersFromTheInPlaceSweep()
    {
        var (before, jacobi) = CreepAndIce(true);
        var (_, inPlace) = CreepAndIce(false);

        double sumBefore = 0, sumJacobi = 0;
        int moved = 0, differ = 0;
        for (int i = 0; i < before.Length; i++)
        {
            sumBefore += before[i].Height;
            sumJacobi += jacobi[i].Height;
            if (jacobi[i].Height != before[i].Height) moved++;
            if (jacobi[i].Height != inPlace[i].Height) differ++;
        }
        Assert.InRange(sumJacobi - sumBefore, -1e-2, 1e-2);
        Assert.True(moved > before.Length / 10, "the passes must actually move ground, or this proves nothing");
        Assert.True(differ > 0, "the knob must change the result, or the Jacobi path is dead code");
    }

    [Fact]
    public void OrderIndependentPasses_LoseNoMoreFromOneTileThanTheInPlaceSweep()
    {
        // Many glaciers can pluck one wall tile in a sweep; the cap on a source's outflow keeps the
        // wall from being drawn down through the valley floor it feeds.
        static double WorstDrop((ElevationInfo[] Before, ElevationInfo[] After) r)
        {
            double worst = 0;
            for (int i = 0; i < r.Before.Length; i++)
                worst = Math.Max(worst, r.Before[i].Height - r.After[i].Height);
            return worst;
        }
        double jacobi = WorstDrop(CreepAndIce(true)), inPlace = WorstDrop(CreepAndIce(false));
        Assert.True(jacobi <= inPlace * 1.25, $"Jacobi lost {jacobi:F3} from one tile, the in-place sweep {inPlace:F3}");
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
    public void Erosion_IsDeterministic()
    {
        var first = ElevationsAfterErosion(3, 42, 12, 10);
        var second = ElevationsAfterErosion(3, 42, 12, 10);

        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
            Assert.Equal(first[i].Height, second[i].Height);
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
