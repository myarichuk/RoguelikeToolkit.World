using System.Linq;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class MaterializationTests
{
    private static World BuildWorld() => new WorldBuilder().WithSize(2).WithSeed(7).Build();

    [Fact]
    public void Override_AppliesToSampleBiomeAndTileFeatures()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        const int tile = 10;
        var address = MapAddress.ForPlanet(world.Seed, tile);
        var coord = store.GetGeoCoord(tile);

        var materialized = new MaterializationStore();
        materialized.SetOverride(address, new TileOverride { DangerLevel = 9, Biome = BiomeType.Desert });
        var options = new QueryOptions { Materialized = materialized };

        var (biome, danger) = world.SampleBiome(coord, options);
        Assert.Equal(BiomeType.Desert, biome);
        Assert.Equal(9, danger);

        var features = world.GetTileFeatures(tile, options);
        Assert.Equal(BiomeType.Desert, features.Biome);
        Assert.Equal(9, features.DangerLevel);
        Assert.Equal(address, features.Address);
    }

    [Fact]
    public void Override_DangerDelta_FiltersCityCandidates()
    {
        using var world = BuildWorld();
        var baseline = world.ScoreCitySites(new CitySiteFilter { TopN = 100 });
        Assert.NotEmpty(baseline);
        int target = baseline[0].TileIndex;

        var materialized = new MaterializationStore();
        materialized.SetOverride(
            MapAddress.ForPlanet(world.Seed, target), new TileOverride { DangerLevel = 200 });
        var filtered = world.ScoreCitySites(
            new CitySiteFilter { TopN = 100, MaxDanger = 3 },
            new QueryOptions { Materialized = materialized });

        Assert.DoesNotContain(filtered, s => s.TileIndex == target);
    }

    [Fact]
    public void BindLocation_RoundTrips()
    {
        var store = new MaterializationStore();
        var address = MapAddress.ForPlanet(7, 10);
        var other = MapAddress.ForPlanet(7, 11);

        Assert.Null(store.GetBoundLocationId(address));
        Assert.False(store.TryGetOverride(address, out _));

        store.BindLocation(address, "region/10/3");
        Assert.Equal("region/10/3", store.GetBoundLocationId(address));
        Assert.Null(store.GetBoundLocationId(other));

        store.BindLocation(address, "local/10/3/5");
        Assert.Equal("local/10/3/5", store.GetBoundLocationId(address));
    }

    [Fact]
    public void NullStore_MatchesCurrentBehavior()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        var coord = store.GetGeoCoord(10);
        var filter = new CitySiteFilter { TopN = 20 };

        var baselineBiome = world.SampleBiome(coord);
        var baselineSites = world.ScoreCitySites(filter);
        var baselineFeatures = world.GetTileFeatures(10);

        foreach (var options in new[]
        {
            new QueryOptions { History = null, Materialized = null },
            new QueryOptions { Materialized = new MaterializationStore() },
        })
        {
            Assert.Equal(baselineBiome, world.SampleBiome(coord, options));

            var sites = world.ScoreCitySites(filter, options);
            Assert.Equal(baselineSites.Count, sites.Count);
            for (int k = 0; k < sites.Count; k++)
            {
                Assert.Equal(baselineSites[k].TileIndex, sites[k].TileIndex);
                Assert.Equal(baselineSites[k].Score, sites[k].Score);
            }

            var features = world.GetTileFeatures(10, options);
            Assert.Equal(baselineFeatures.Biome, features.Biome);
            Assert.Equal(baselineFeatures.DangerLevel, features.DangerLevel);
            Assert.Equal(baselineFeatures.TileIndex, features.TileIndex);
        }

        // Sites attach only when a catalog is passed; default is empty.
        Assert.Empty(baselineFeatures.Sites);
        var catalog = new SiteCatalog();
        catalog.Add(new PlacedSite { Kind = SiteKind.City, CellIndex = 10, InjectorId = "t" });
        var withSites = TileFeatures.Query(store, 10, world.Rivers, world.WaterBodies, world.Ranges, world.Deposits, catalog);
        Assert.Single(withSites.Sites);
        Assert.Equal(SiteKind.City, withSites.Sites[0].Kind);
    }
}
