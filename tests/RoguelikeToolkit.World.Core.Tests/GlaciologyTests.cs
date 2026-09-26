using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class GlaciologyTests
{
    [Fact]
    public void SnowlineDescendsWithLatitude()
    {
        // Same mid height: polar land glaciates, equatorial lowland does not.
        var equator = Vector3D.FromGeoCoord(new GeoCoord(0, 0));
        var pole = Vector3D.FromGeoCoord(new GeoCoord(85, 0));
        Assert.False(Glaciology.IsGlacierTile(equator, 0.1f));
        Assert.True(Glaciology.IsGlacierTile(pole, 0.1f));
    }

    [Fact]
    public void OceanIsNeverGlacier()
    {
        var pole = Vector3D.FromGeoCoord(new GeoCoord(85, 0));
        Assert.False(Glaciology.IsGlacierTile(pole, -0.5f));
    }

    [Fact]
    public void MaskIsDeterministic()
    {
        var pos = Vector3D.FromGeoCoord(new GeoCoord(62, 30));
        Assert.Equal(Glaciology.IsGlacierTile(pos, 0.35f), Glaciology.IsGlacierTile(pos, 0.35f));
    }

    [Fact]
    public void MeltBonusRaisesRunoff()
    {
        var pole = Vector3D.FromGeoCoord(new GeoCoord(85, 0));
        var equator = Vector3D.FromGeoCoord(new GeoCoord(0, 0));
        // Same rainfall: the glacier tile yields strictly more runoff.
        Assert.True(Glaciology.RunoffWeight(pole, 0.2f, 0.5f) > Glaciology.RunoffWeight(equator, 0.2f, 0.5f));
        // No climate layer: uniform rain plus melt still favors the glacier.
        Assert.True(Glaciology.RunoffWeight(pole, 0.2f) > Glaciology.RunoffWeight(equator, 0.2f));
    }

    [Fact]
    public void FullBuild_GlaciersExistAndAreLand()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var vectors = store.GetTileVectors();

        int glaciers = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            if (!Glaciology.IsGlacierTile(vectors[i], elev[i].Height)) continue;
            glaciers++;
            Assert.True(elev[i].Height >= ElevationGenerationStage.SeaLevel, $"Glacier tile {i} below sea level");
        }
        Assert.True(glaciers > 0, "Expected glaciated tiles for size 2, seed 7");
    }

    [Fact]
    public void ClimateAware_DryPolarLowlandStaysTundra()
    {
        // Cold dry continental interior (Siberia-like): no accumulation, no ice.
        var pos = Vector3D.FromGeoCoord(new GeoCoord(75, 40));
        Assert.False(Glaciology.IsGlacierTile(pos, 0.05f, 0.06f, 0.05f));
    }

    [Fact]
    public void ClimateAware_WetPolarLowlandGlaciates()
    {
        // Same cold, but wet: accumulation wins, ice sheet grows.
        var pos = Vector3D.FromGeoCoord(new GeoCoord(75, 40));
        Assert.True(Glaciology.IsGlacierTile(pos, 0.05f, 0.06f, 0.60f));
    }

    [Fact]
    public void ClimateAware_WarmLowlandNeverGlaciates()
    {
        var pos = Vector3D.FromGeoCoord(new GeoCoord(10, 0));
        Assert.False(Glaciology.IsGlacierTile(pos, 0.10f, 0.80f, 0.90f));
    }

    [Fact]
    public void FullBuild_GlacierBiomeExistsOnLand()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var locals = store.GetSpan<LocalMapInfo>();

        int glaciers = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            if (locals[i].Biome != BiomeType.Glacier) continue;
            glaciers++;
            Assert.True(elev[i].Height >= ElevationGenerationStage.SeaLevel);
        }
        Assert.True(glaciers > 0, "Expected Glacier biome tiles for size 3, seed 42");
    }

    [Fact]
    public void RainAndMelt_SeedMoreRiversThanUniform()
    {
        // Same terrain flagged both ways: modeled runoff must not lose rivers
        // versus the old uniform-rainfall assumption.
        using var world = new WorldBuilder().WithSize(3).WithSeed(7).Build();
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var hydro = store.GetSpan<HydrologyInfo>();
        int n = store.TileCount;
        float threshold = Math.Max(8f, n / 200f);

        int modeled = 0;
        for (int i = 0; i < n; i++)
            if (hydro[i].IsRiver == 1) modeled++;

        var uniform = new float[n];
        Hydrography.ComputeFlow(store, elev, uniform);
        int baseline = 0;
        for (int i = 0; i < n; i++)
            if (uniform[i] >= threshold && elev[i].Height >= ElevationGenerationStage.SeaLevel) baseline++;

        Assert.True(modeled >= baseline, $"Modeled rivers {modeled} < uniform baseline {baseline}");
    }
}
