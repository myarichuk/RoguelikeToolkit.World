using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Biome-histogram guardrails (backlog E2, E5): the tundra band must exist,
/// glaciers must stay confined to cold ground, and desert counts must not
/// collapse after a glacier-mask retune. Small-matrix slice (sizes 3-4 x
/// seeds 42/7, includes the binding s3seed42 case); the full matrix
/// (sizes 3-6) is probe-verified per retune, see Glaciology.DryPolarGate docs.
/// </summary>
public class BiomeHistogramTests
{
    private static readonly (int Size, int Seed, int DesertPreChange)[] Matrix =
    {
        (3, 42, 10), (3, 7, 16), (4, 42, 23), (4, 7, 56),
    };

    private static Dictionary<BiomeType, int> Histogram(World world)
    {
        var hist = new Dictionary<BiomeType, int>();
        var locals = world.Map.DataStore.GetSpan<LocalMapInfo>();
        for (int i = 0; i < locals.Length; i++)
        {
            var b = locals[i].Biome;
            hist[b] = hist.TryGetValue(b, out int c) ? c + 1 : 1;
        }
        return hist;
    }

    private static int Get(Dictionary<BiomeType, int> hist, BiomeType biome)
        => hist.TryGetValue(biome, out int v) ? v : 0;

    [Fact]
    public void TundraBandExistsOnEveryMatrixBuild()
    {
        foreach (var (size, seed, _) in Matrix)
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            int tundra = Get(Histogram(world), BiomeType.Tundra);
            Assert.True(tundra > 0, $"Size {size} seed {seed}: Tundra extinct");
        }
    }

    [Fact]
    public void GlacierConfinedToColdGroundAndPresent()
    {
        foreach (var (size, seed, _) in Matrix)
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var locals = store.GetSpan<LocalMapInfo>();
            var climate = store.GetSpan<ClimateInfo>();
            bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
            int glaciers = 0;
            for (int i = 0; i < store.TileCount; i++)
            {
                if (locals[i].Biome != BiomeType.Glacier) continue;
                glaciers++;
                // The mask only fires at or below the snowline (0.15 + 0.11*p,
                // p <= 1) plus noise margin; anything warmer is a mask bug.
                if (hasClimate)
                    Assert.True(climate[i].Temperature <= 0.32,
                        $"Size {size} seed {seed} tile {i}: glacier at temp {climate[i].Temperature}");
            }
            Assert.True(glaciers > 0, $"Size {size} seed {seed}: Glacier extinct");
        }
    }

    [Fact]
    public void DesertCountsWithinHalfToDoubleOfPreChange()
    {
        foreach (var (size, seed, pre) in Matrix)
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            int desert = Get(Histogram(world), BiomeType.Desert);
            Assert.InRange(desert, (int)(pre * 0.5), (int)(pre * 2.0) + 1);
        }
    }
}
