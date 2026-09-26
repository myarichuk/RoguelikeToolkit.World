using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class TileFeatureTests
{
    [Fact]
    public void RiverTiles_LinkUpstreamDownstreamWithCoords()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var hydro = store.GetSpan<HydrologyInfo>();

        int rivers = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            var f = world.GetTileFeatures(i);
            Assert.Equal(i, f.TileIndex);
            Assert.True(f.HasBiome && f.HasElevation && f.HasHydrology);
            Assert.Equal(store.GetSpan<LocalMapInfo>()[i].Biome, f.Biome);
            if (hydro[i].IsRiver != 1) continue;
            rivers++;

            Assert.True(f.IsRiver);
            Assert.NotEqual(RiverWaterSource.None, f.RiverSource);
            if (f.UpstreamTile >= 0)
            {
                // Water comes from higher river ground; coords resolve.
                Assert.Equal(1, hydro[f.UpstreamTile].IsRiver);
                Assert.True(elev[f.UpstreamTile].Height > elev[i].Height - 1e-6f);
                Assert.Equal(store.GetGeoCoord(f.UpstreamTile), f.UpstreamCoord);
            }
            if (f.DownstreamTile >= 0)
            {
                // Water leaves a river reach only into the sea or a lake.
                if (hydro[f.DownstreamTile].IsRiver != 1)
                    Assert.True(elev[f.DownstreamTile].Height < ElevationGenerationStage.SeaLevel
                        || hydro[f.DownstreamTile].LakeDepth > 0f);
                Assert.Equal(store.GetGeoCoord(f.DownstreamTile), f.DownstreamCoord);
            }
        }
        Assert.True(rivers > 0, "Expected river tiles for size 3, seed 42");
    }

    [Fact]
    public void RiverHeads_HaveClassifiedSource()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        int heads = 0, springs = 0, melt = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            var f = world.GetTileFeatures(i);
            if (!f.IsRiver) Assert.Equal(RiverWaterSource.None, f.RiverSource);
            else if (f.UpstreamTile < 0)
            {
                heads++;
                if (f.RiverSource == RiverWaterSource.Spring) springs++;
                else if (f.RiverSource == RiverWaterSource.GlacierMelt) melt++;
                else Assert.Fail($"Head tile {i} has unclassified source");
            }
        }
        Assert.True(heads > 0, "Expected river heads");
        Assert.True(springs + melt == heads, "Every head is spring- or melt-fed");
    }

    [Fact]
    public void CatalogTiles_ResolveToCatalogEntries()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;
        // Every cataloged river path tile reports its river id back.
        foreach (var river in world.Rivers.Rivers)
        {
            Assert.NotEmpty(river.Path);
            foreach (int t in river.Path)
            {
                var f = world.GetTileFeatures(t);
                Assert.Equal(river.Id, f.RiverId);
            }
        }
        // Cataloged water tiles report their body kind.
        foreach (var body in world.WaterBodies.Bodies)
        {
            Assert.NotEmpty(body.Tiles);
            var f = world.GetTileFeatures(body.Tiles[0]);
            Assert.Equal(body.Kind, f.BodyKind);
        }
    }

    [Fact]
    public void PartialMap_QueryToleratesMissingLayers()
    {
        using var map = new WorldMap(1);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();

        var f = TileFeatures.Query(map.DataStore, 0);
        Assert.False(f.HasBiome);
        Assert.False(f.HasHydrology);
        Assert.True(f.HasElevation);
        Assert.False(f.IsRiver);
        Assert.Equal(RiverWaterSource.None, f.RiverSource);

        Assert.Throws<IndexOutOfRangeException>(() => TileFeatures.Query(map.DataStore, map.DataStore.TileCount));
    }
}
