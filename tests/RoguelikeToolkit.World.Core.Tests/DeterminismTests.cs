using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

public class DeterminismTests
{
    private static (TectonicPlate[] Plates, ElevationInfo[] Elev, LocalMapInfo[] Locals) Generate(int size, int seed, int seedCount)
    {
        using var map = new WorldMap(size);
        using var tectonicLayer = new TectonicPlateLayer(map.DataStore, seedCount);
        map.RegisterLayer(tectonicLayer);
        using var elevationLayer = new ElevationLayer(map.DataStore);
        map.RegisterLayer(elevationLayer);
        using var localLayer = new LocalMapLayer(map.DataStore, seed, tectonicLayer);
        map.RegisterLayer(localLayer);
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new TectonicPlateGenerationStage(seedCount, seed));
        pipeline.AddStage(new ElevationGenerationStage(seed));
        pipeline.AddStage(new ErosionGenerationStage());
        pipeline.AddStage(new LocalMapGenerationStage(seed));
        pipeline.Execute(map);

        return (map.DataStore.GetSpan<TectonicPlate>().ToArray(),
                map.DataStore.GetSpan<ElevationInfo>().ToArray(),
                map.DataStore.GetSpan<LocalMapInfo>().ToArray());
    }

    [Fact]
    public void SameSeed_ProducesIdenticalOutput()
    {
        var first = Generate(2, 42, 12);
        var second = Generate(2, 42, 12);

        Assert.Equal(first.Plates.Length, second.Plates.Length);
        for (int i = 0; i < first.Plates.Length; i++)
        {
            Assert.Equal(first.Plates[i].Id, second.Plates[i].Id);
            Assert.Equal(first.Plates[i].Elevation, second.Plates[i].Elevation);
            Assert.Equal(first.Plates[i].DriftSpeed, second.Plates[i].DriftSpeed);
            Assert.Equal(first.Plates[i].DriftX, second.Plates[i].DriftX);
            Assert.Equal(first.Elev[i].Height, second.Elev[i].Height);
            Assert.Equal(first.Locals[i].Seed, second.Locals[i].Seed);
            Assert.Equal(first.Locals[i].Biome, second.Locals[i].Biome);
            Assert.Equal(first.Locals[i].DangerLevel, second.Locals[i].DangerLevel);
        }
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentOutput()
    {
        var first = Generate(2, 42, 12);
        var second = Generate(2, 43, 12);

        int differing = 0;
        for (int i = 0; i < first.Plates.Length; i++)
        {
            if (first.Plates[i].Id != second.Plates[i].Id ||
                first.Locals[i].Seed != second.Locals[i].Seed)
                differing++;
        }

        Assert.True(differing > 0, "Different seeds produced identical output");
    }

    [Fact]
    public void Pipeline_ProducesOceansAndLand()
    {
        var world = Generate(3, 42, 12);

        int ocean = 0, land = 0;
        var biomes = new System.Collections.Generic.HashSet<BiomeType>();
        foreach (var local in world.Locals)
        {
            biomes.Add(local.Biome);
            if (local.Biome == BiomeType.Ocean) ocean++;
            else land++;
        }

        Assert.True(ocean > 0, "Expected some ocean tiles");
        Assert.True(land > 0, "Expected some land tiles");
        Assert.True(biomes.Count >= 3, $"Expected biome variety, got {biomes.Count}: {string.Join(",", biomes)}");
    }

    [Fact]
    public void DeriveTileSeed_IsStableAndDiscriminating()
    {
        Assert.Equal(Rng.DeriveTileSeed(42, 7), Rng.DeriveTileSeed(42, 7));

        var seen = new System.Collections.Generic.HashSet<uint>();
        for (int i = 0; i < 256; i++)
            seen.Add(Rng.DeriveTileSeed(42, i));

        Assert.True(seen.Count >= 250, $"Only {seen.Count}/256 distinct tile seeds");
    }

    [Fact]
    public void Rng_NextDouble_StaysInUnitRange()
    {
        var r = Rng.Create(1234, 0);
        for (int i = 0; i < 1000; i++)
        {
            double d = r.NextDouble();
            Assert.InRange(d, 0.0, 1.0);
        }
    }
}
