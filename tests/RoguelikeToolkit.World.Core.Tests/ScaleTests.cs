using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Scale smoke tests (backlog C3): generation success + pipeline determinism
/// at sizes 6–7. No wall-clock asserts — timings are benchmark territory.
/// Size 7 is <c>Stress</c>: excluded from CI, run by the nightly workflow.
/// </summary>
public class ScaleTests
{
    private static (float[] Heights, BiomeType[] Biomes) Snapshot(World world)
    {
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var locals = store.GetSpan<LocalMapInfo>();
        var heights = new float[store.TileCount];
        var biomes = new BiomeType[store.TileCount];
        for (int i = 0; i < store.TileCount; i++)
        {
            heights[i] = elev[i].Height;
            biomes[i] = locals[i].Biome;
        }
        return (heights, biomes);
    }

    private static void AssertValidPlanet(World world, int size)
    {
        int expected = 10 * (1 << (2 * size)) + 2;
        Assert.Equal(expected, world.TileCount);
        var (_, biomes) = Snapshot(world);
        int ocean = 0;
        foreach (var b in biomes)
            if (b == BiomeType.Ocean) ocean++;
        Assert.True(ocean > 0, "Expected some ocean tiles");
        Assert.True(ocean < biomes.Length, "Expected some land tiles");
    }

    private static void AssertDeterministic(int size, int seed)
    {
        using var first = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        using var second = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        var (h1, b1) = Snapshot(first);
        var (h2, b2) = Snapshot(second);
        Assert.Equal(h1, h2);
        Assert.Equal(b1, b2);
    }

    [Fact]
    public void Size6_FullBuild_SucceedsAndIsDeterministic()
    {
        using var world = new WorldBuilder().WithSize(6).WithSeed(42).Build();
        AssertValidPlanet(world, 6);
        AssertDeterministic(6, 42);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void Size7_FullBuild_SucceedsAndIsDeterministic()
    {
        using var world = new WorldBuilder().WithSize(7).WithSeed(42).Build();
        AssertValidPlanet(world, 7);
        AssertDeterministic(7, 42);
    }
}
