using System;
using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class PlatePartitionTests
{
    private static Vector3D[] MakeSeeds(int seed, int plates)
    {
        var seeds = new Vector3D[plates];
        for (int s = 0; s < plates; s++)
        {
            var r = Rng.Create(seed, s);
            double lat = r.NextDouble() * 180.0 - 90.0;
            double lon = r.NextDouble() * 360.0 - 180.0;
            seeds[s] = Vector3D.FromGeoCoord(new GeoCoord(lat, lon));
        }
        return seeds;
    }

    private static int[] Partition(IPlatePartitioner partitioner, int size, int seed, int plates)
    {
        using var store = new WorldDataStore(size);
        var positions = store.GetTileVectors();
        var seeds = MakeSeeds(seed, plates);
        var ids = new int[store.TileCount];
        partitioner.Partition(store, positions, seeds, ids, seed);
        return ids;
    }

    [Fact]
    public void FloodFill_CoversEveryTileWithValidPlate()
    {
        var ids = Partition(new NoisyFloodFillPlatePartitioner(), 2, 42, 12);
        var seen = new HashSet<int>();
        foreach (int id in ids)
        {
            Assert.InRange(id, 0, 11);
            seen.Add(id);
        }
        // Every seed claims at least its seed tile, so no plate is orphaned.
        Assert.Equal(12, seen.Count);
    }

    [Fact]
    public void FloodFill_IsDeterministic()
    {
        var first = Partition(new NoisyFloodFillPlatePartitioner(), 2, 42, 12);
        var second = Partition(new NoisyFloodFillPlatePartitioner(), 2, 42, 12);
        Assert.Equal(first, second);
    }

    [Fact]
    public void FloodFill_DifferentSeed_DifferentLayout()
    {
        var first = Partition(new NoisyFloodFillPlatePartitioner(), 2, 42, 12);
        var second = Partition(new NoisyFloodFillPlatePartitioner(), 2, 43, 12);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void FloodFill_BordersDifferFromVoronoi()
    {
        var flood = Partition(new NoisyFloodFillPlatePartitioner(), 2, 42, 12);
        var voronoi = Partition(new VoronoiPlatePartitioner(), 2, 42, 12);
        int differing = 0;
        for (int i = 0; i < flood.Length; i++)
            if (flood[i] != voronoi[i]) differing++;
        // Noise + speeds must move a visible share of border tiles.
        Assert.True(differing > flood.Length / 20,
            $"Flood-fill should perturb borders, only {differing}/{flood.Length} tiles differ.");
    }

    [Fact]
    public void Voronoi_IsDeterministic()
    {
        var first = Partition(new VoronoiPlatePartitioner(), 2, 42, 12);
        var second = Partition(new VoronoiPlatePartitioner(), 2, 42, 12);
        Assert.Equal(first, second);
    }

    private static TectonicPlate[] RunTectonics(int size, int seed, int plates, bool segment)
    {
        using var map = new WorldMap(size);
        map.RegisterLayer<TectonicPlate>(new TectonicPlateLayer(map.DataStore, plates, seed));
        map.DataStore.Allocate();
        using var stage = new TectonicPlateGenerationStage(plates, seed,
            new VoronoiPlatePartitioner()) { SegmentOrogeny = segment };
        stage.Execute(map);
        return map.DataStore.GetSpan<TectonicPlate>().ToArray();
    }

    [Fact]
    public void OrogenySegmentation_ModulatesConvergentDriverWithinBounds()
    {
        var plain = RunTectonics(3, 42, 12, segment: false);
        var segmented = RunTectonics(3, 42, 12, segment: true);

        int convergent = 0, modulated = 0;
        for (int i = 0; i < plain.Length; i++)
        {
            if (plain[i].Boundary != PlateBoundaryType.Convergent) continue;
            convergent++;
            float a = plain[i].Orogeny, b = segmented[i].Orogeny;
            if (Math.Abs(a) < 1e-6) continue;
            if (b == 0f) { modulated++; continue; } // strike gap: fully suppressed, see below.
            double ratio = b / a;
            Assert.InRange(ratio, 1.0 - 0.65 - 0.01, 1.0 + 0.65 + 0.01);
            if (a != b) modulated++;
        }
        Assert.True(convergent > 0, "Expected convergent boundary tiles.");
        Assert.True(modulated > 0, "Segmentation should modulate some convergent tiles.");
    }

    [Fact]
    public void OrogenySegmentation_CreatesStrikeGaps()
    {
        // Without exact-zero gaps a modulated wall is still one connected
        // mountain blurb along the whole boundary; gaps cut it into discrete
        // ranges with passes between them.
        var segmented = RunTectonics(3, 42, 12, segment: true);
        int convergent = 0, gaps = 0;
        for (int i = 0; i < segmented.Length; i++)
        {
            if (segmented[i].Boundary != PlateBoundaryType.Convergent) continue;
            convergent++;
            if (segmented[i].Orogeny == 0f) gaps++;
        }
        Assert.True(convergent > 0, "Expected convergent boundary tiles.");
        Assert.True(gaps > 0, "Segmentation should cut strike gaps (exact-zero drivers).");

        // Legacy unsegmented behavior never fully suppresses a convergent
        // driver (it scales by [1-S, 1+S], and convergence exceeds threshold).
        var plain = RunTectonics(3, 42, 12, segment: false);
        for (int i = 0; i < plain.Length; i++)
            if (plain[i].Boundary == PlateBoundaryType.Convergent)
                Assert.NotEqual(0f, plain[i].Orogeny);
    }

    [Fact]
    public void MountainBelts_DoNotBlanketPlates()
    {
        // Regression: wide uplift aprons used to fuse into a single blurb
        // (size 4, seed 42 grew one 80-tile range across two plates). Ranges
        // must stay compact enough that no plate is mostly mountains.
        foreach (int seed in new[] { 42, 7 })
        {
            using var world = new WorldBuilder().WithSize(4).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var elev = store.GetSpan<ElevationInfo>();
            var plates = store.GetSpan<TectonicPlate>();

            int mountains = 0;
            for (int i = 0; i < elev.Length; i++)
                if (elev[i].Height > 0.5f) mountains++;
            Assert.True(mountains > 0, $"Size 4 seed {seed}: mountains extinct.");

            int largest = 0;
            foreach (var f in world.Ranges.Features)
                if (f.Kind == RangeKind.MountainRange && f.Tiles.Count > largest)
                    largest = f.Tiles.Count;
            Assert.True(largest <= 70,
                $"Size 4 seed {seed}: largest range is {largest} tiles (plate-blanketing blurb).");

            var plateTiles = new Dictionary<int, int>();
            var plateMountains = new Dictionary<int, int>();
            for (int i = 0; i < store.TileCount; i++)
            {
                int id = plates[i].Id;
                plateTiles[id] = plateTiles.TryGetValue(id, out int c) ? c + 1 : 1;
                if (elev[i].Height > 0.5f)
                    plateMountains[id] = plateMountains.TryGetValue(id, out int m) ? m + 1 : 1;
            }
            foreach (var kv in plateTiles)
            {
                plateMountains.TryGetValue(kv.Key, out int m);
                double cover = (double)m / kv.Value;
                Assert.True(cover < 1.0 / 3.0,
                    $"Size 4 seed {seed}: plate {kv.Key} is {cover:P0} mountains.");
            }
        }
    }

    private static LocalMapInfo[] RunBiomes(int size, int seed, int plates, bool smooth)
    {
        using var world = new WorldBuilder()
            .WithSize(size).WithSeed(seed).WithPlateCount(plates)
            .WithBiomes(b => { b.SmoothBiomes = smooth; })
            .Build();
        return world.Map.DataStore.GetSpan<LocalMapInfo>().ToArray();
    }

    [Fact]
    public void BiomeSmoothing_OnlyTouchesMoistureBiomesAndReducesEnclaves()
    {
        int flips = 0, enclavesBefore = 0, enclavesAfter = 0;
        for (int seed = 40; seed <= 45; seed++)
        {
            var before = RunBiomes(2, seed, 12, smooth: false);
            var after = RunBiomes(2, seed, 12, smooth: true);
            Assert.Equal(before.Length, after.Length);
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i].Biome != after[i].Biome)
                {
                    flips++;
                    Assert.True(IsMoistureBiome(before[i].Biome), "Smoothing must not flip gated biomes.");
                    Assert.True(IsMoistureBiome(after[i].Biome), "Smoothing must flip within the moisture set.");
                }
            }
            enclavesBefore += CountEnclaves(seed, before);
            enclavesAfter += CountEnclaves(seed, after);
        }
        Assert.True(flips > 0, "Smoothing should flip speckled tiles on some seed.");
        Assert.True(enclavesAfter <= enclavesBefore, "Smoothing should not create enclaves.");
    }

    private static bool IsMoistureBiome(BiomeType biome)
        => biome == BiomeType.Plains || biome == BiomeType.Forest
            || biome == BiomeType.Jungle || biome == BiomeType.Swamp;

    private static int CountEnclaves(int seed, LocalMapInfo[] locals)
    {
        // Enclave: a moisture-biome tile with no same-biome neighbor.
        using var store = new WorldDataStore(2);
        Span<int> neighbors = stackalloc int[6];
        int count = 0;
        for (int i = 0; i < locals.Length; i++)
        {
            if (!IsMoistureBiome(locals[i].Biome)) continue;
            int adjacent = store.GetAdjacent(i, neighbors);
            bool hasSame = false;
            for (int k = 0; k < adjacent; k++)
                if (locals[neighbors[k]].Biome == locals[i].Biome) { hasSame = true; break; }
            if (!hasSame) count++;
        }
        return count;
    }
}
