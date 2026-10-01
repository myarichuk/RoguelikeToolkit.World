using System;
using System.Collections.Generic;
using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Regression coverage for the review-remediation fixes.</summary>
public class RemediationTests
{
    private static World BuildWorld(int size = 2, int seed = 7)
        => new WorldBuilder().WithSize(size).WithSeed(seed).Build();

    [Fact]
    public void EmptyAllowedBiomes_MatchesNull()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        var empty = world.ScoreCitySites(new CitySiteFilter { AllowedBiomes = new HashSet<BiomeType>(), TopN = 100 });
        var unfiltered = world.ScoreCitySites(new CitySiteFilter { AllowedBiomes = null, TopN = 100 });
        Assert.Equal(unfiltered.Count, empty.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void TopN_NonPositive_ReturnsEmpty(int topN)
    {
        using var world = BuildWorld();
        var sites = world.ScoreCitySites(new CitySiteFilter { TopN = topN });
        Assert.Empty(sites);
    }

    [Fact]
    public void FreshWaterRadius_ZeroIsSubsetOfDefault()
    {
        using var world = BuildWorld();
        var none = world.ScoreCitySites(new CitySiteFilter { FreshWaterRadiusKm = 0, TopN = 100 });
        var wide = world.ScoreCitySites(new CitySiteFilter { FreshWaterRadiusKm = 600, TopN = 100 });
        Assert.True(none.Count <= wide.Count);
    }

    [Fact]
    public void FreshWaterRadius_GlobeCovering_DoesNotThrow()
    {
        using var world = BuildWorld();
        var sites = world.ScoreCitySites(new CitySiteFilter { FreshWaterRadiusKm = 50000, TopN = 100 });
        Assert.NotNull(sites);
    }

    [Fact]
    public void SingleTileRivers_AreVisibleToNearestRiver()
    {
        // Lone IsRiver trickles stay out of the catalog (Path.Count >= 2 keeps
        // WKT LINESTRINGs valid) but must resolve via the spatial index.
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var hydro = store.GetSpan<HydrologyInfo>();
        int riverTiles = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            if (hydro[i].IsRiver != 1) continue;
            riverTiles++;
            var hit = world.NearestRiver(store.GetGeoCoord(i));
            Assert.True(hit.HasValue, $"IsRiver tile {i} invisible to NearestRiver.");
            Assert.Equal(i, hit.Value.TileIndex);
            Assert.True(hit.Value.DistanceKm < 1e-3, $"IsRiver tile {i} resolved at {hit.Value.DistanceKm} km.");
        }
        Assert.True(riverTiles > 0, "Expected river tiles for size 3, seed 42.");
    }

    [Fact]
    public void MaxRivers_ConfigIsHonored()
    {
        using var capped = new WorldBuilder().WithSize(2).WithSeed(42).WithHydrology(h => h.MaxRivers = 2).Build();
        Assert.True(capped.Rivers.Rivers.Count <= 2);
        using var uncapped = new WorldBuilder().WithSize(2).WithSeed(42).Build();
        Assert.True(uncapped.Rivers.Rivers.Count > 2, "Seed must produce >2 rivers for a non-vacuous cap test.");
    }

    [Fact]
    public void Erosion_IterationsZero_LeavesTerrainUntouched()
    {
        using var map = new WorldMap(2);
        using var tectonicLayer = new TectonicPlateLayer(map.DataStore, 12);
        map.RegisterLayer(tectonicLayer);
        using var elevationLayer = new ElevationLayer(map.DataStore);
        map.RegisterLayer(elevationLayer);
        map.DataStore.Allocate();

        var setup = new WorldGenerationPipeline();
        setup.AddStage(new TectonicPlateGenerationStage(12, 42));
        setup.AddStage(new ElevationGenerationStage(42));
        setup.Execute(map);
        var before = map.DataStore.GetSpan<ElevationInfo>().ToArray();

        new ErosionGenerationStage(0).Execute(map);
        var after = map.DataStore.GetSpan<ElevationInfo>();
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i].Height, after[i].Height);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void GetTileCount_InvalidSize_Throws(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldDataStore.GetTileCount(size));
    }

    [Fact]
    public void Wkt_UnknownIds_ThrowWithParamName()
    {
        using var world = BuildWorld();
        var riverEx = Assert.Throws<ArgumentOutOfRangeException>(() => world.RiverToWkt(int.MaxValue));
        Assert.Equal("riverId", riverEx.ParamName);
        var bodyEx = Assert.Throws<ArgumentOutOfRangeException>(() => world.WaterBodyToWkt(-1));
        Assert.Equal("bodyId", bodyEx.ParamName);
        var depositEx = Assert.Throws<ArgumentOutOfRangeException>(() => world.DepositToWkt(int.MaxValue));
        Assert.Equal("depositId", depositEx.ParamName);
    }

    [Fact]
    public void OffsetGrid_FromIndex_OutOfRange_Throws()
    {
        Assert.Throws<IndexOutOfRangeException>(() => OffsetGrid.FromIndex(-1, 8));
        Assert.Throws<IndexOutOfRangeException>(() => OffsetGrid.FromIndex(64, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => OffsetGrid.FromIndex(0, 0));
        Assert.Equal((0, 0), OffsetGrid.FromIndex(0, 8));
        Assert.Equal((7, 7), OffsetGrid.FromIndex(63, 8));
    }

    [Fact]
    public void Pipeline_Execute_MissingRequiredRead_ThrowsFriendly()
    {
        using var map = new WorldMap(1);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new ElevationGenerationStage(42)); // Reads TectonicPlate: not registered.
        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.Execute(map));
        Assert.Contains("ElevationGenerationStage", ex.Message);
    }

    [Fact]
    public void GetLocal_CustomRegionSize_AgreesWithRegionHandle()
    {
        using var world = BuildWorld();
        var region = world.GetRegion(5, regionSize: 4);
        var viaHex = world.GetLocal(new PlanetHex(world.Seed, 5), 3, regionSize: 4);
        Assert.Equal(region.GetLocal(3).Seed, viaHex.Seed);
        var viaRef = world.GetLocal(new RegionRef(world.Seed, 5, 3), regionSize: 4);
        Assert.Equal(region.GetLocal(3).Seed, viaRef.Seed);
    }

    [Fact]
    public void TerrainOrientation_UnrunHydro_FallsBackToElevation()
    {
        using var map = new WorldMap(1);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.DataStore.Allocate();

        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        for (int i = 0; i < elev.Length; i++) elev[i] = new ElevationInfo { Height = 0.3f };
        const int tile = 12;
        Span<int> scratch = stackalloc int[6];
        int adj = store.GetAdjacent(tile, scratch);
        Assert.True(adj >= 3, "Need several neighbors for a directional fallback check.");
        elev[tile] = new ElevationInfo { Height = 0.5f };
        elev[scratch[0]] = new ElevationInfo { Height = 0.1f };
        elev[scratch[1]] = new ElevationInfo { Height = 0.8f };
        for (int k = 2; k < adj; k++) elev[scratch[k]] = new ElevationInfo { Height = 0.6f };

        var parent = TerrainOrientation.Sample(store, tile);
        Assert.Equal(scratch[1], parent.FlowEntryTile);
        Assert.Equal(scratch[0], parent.FlowExitTile);
    }
}
