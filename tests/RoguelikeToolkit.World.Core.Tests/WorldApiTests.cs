using System;
using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class WorldApiTests
{
    private sealed class BoostTileHistory : IHistoricalContext
    {
        private readonly int _tile;
        public BoostTileHistory(int tile) => _tile = tile;

        public bool TryGetTileModifier(int worldTileIndex, out TileHistoryModifier modifier)
        {
            if (worldTileIndex == _tile)
            {
                modifier = new TileHistoryModifier { DangerDelta = 0, HabitabilityDelta = 5 };
                return true;
            }
            modifier = default;
            return false;
        }

        public IEnumerable<HistoricSite> GetSitesNear(GeoCoord position, double radiusKm)
        {
            yield break;
        }
    }

    private sealed class RuinHistory : IHistoricalContext
    {
        private readonly int _tile;
        public RuinHistory(int tile) => _tile = tile;

        public bool TryGetTileModifier(int worldTileIndex, out TileHistoryModifier modifier)
        {
            if (worldTileIndex == _tile)
            {
                modifier = new TileHistoryModifier { DangerDelta = 4, HabitabilityDelta = 0 };
                return true;
            }
            modifier = default;
            return false;
        }

        public IEnumerable<HistoricSite> GetSitesNear(GeoCoord position, double radiusKm)
        {
            yield break;
        }
    }

    [Fact]
    public void Build_SampleAndRadiusQueries_AgreeWithStore()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var store = world.Map.DataStore;

        var coord = store.GetGeoCoord(10);
        Assert.Equal(store.GetSpan<ElevationInfo>()[10].Height, world.SampleElevation(coord));

        var (biome, _) = world.SampleBiome(coord);
        Assert.Equal(store.GetSpan<LocalMapInfo>()[10].Biome, biome);

        var hits = world.QueryRadius(coord, 1500.0);
        Assert.NotEmpty(hits);
        Assert.Equal(10, hits[0].TileIndex);
        for (int k = 1; k < hits.Count; k++)
            Assert.True(hits[k].DistanceKm >= hits[k - 1].DistanceKm);

        Assert.NotNull(world.NearestWater(coord));
    }

    [Fact]
    public void RegionAndLocal_AreDeterministic()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();

        var a = world.GetRegion(5);
        var b = world.GetRegion(5);
        Assert.Equal(a.Seed, b.Seed);
        Assert.Equal(a.Cells.Length, b.Cells.Length);
        for (int i = 0; i < a.Cells.Length; i++)
            Assert.Equal(a.Cells[i].Seed, b.Cells[i].Seed);

        var la = a.GetLocal(3);
        var lb = b.GetLocal(3);
        Assert.Equal(la.Seed, lb.Seed);
        Assert.Equal(la.Tiles.Length, lb.Tiles.Length);
        for (int i = 0; i < la.Tiles.Length; i++)
            Assert.Equal(la.Tiles[i].Height, lb.Tiles[i].Height);
    }

    [Fact]
    public void ScoreCitySites_ReturnsRankedCandidates()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var sites = world.ScoreCitySites(new CitySiteFilter { TopN = 5 });

        Assert.NotEmpty(sites);
        Assert.True(sites.Count <= 5);
        for (int k = 1; k < sites.Count; k++)
            Assert.True(sites[k].Score <= sites[0].Score + 1e-9);
        foreach (var s in sites)
        {
            Assert.InRange(s.Elevation, 0.01f, 0.6f);
            Assert.False(string.IsNullOrEmpty(s.Reasons));
        }
    }

    [Fact]
    public void HistoryHook_BoostsChosenTileInCityRankings()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var baseline = world.ScoreCitySites(new CitySiteFilter { TopN = 50 });
        Assert.NotEmpty(baseline);

        // Boost a mid-ranked candidate so the history delta decides the order.
        int target = baseline[Math.Min(3, baseline.Count - 1)].TileIndex;
        double before = baseline.Find(s => s.TileIndex == target).Score;

        var boosted = world.ScoreCitySites(new CitySiteFilter { TopN = 50 },
            new QueryOptions { History = new BoostTileHistory(target) });
        var entry = boosted.Find(s => s.TileIndex == target);

        Assert.NotNull(entry);
        Assert.Equal(target, boosted[0].TileIndex);
        Assert.True(entry!.Score > before, $"History boost did not apply ({before} -> {entry.Score})");
        Assert.Contains("history", entry.Reasons);
    }

    [Fact]
    public void HistoryHook_DangerDelta_FiltersCityCandidates()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var baseline = world.ScoreCitySites(new CitySiteFilter { TopN = 100 });
        Assert.NotEmpty(baseline);
        int target = baseline[0].TileIndex;

        var withRuin = world.ScoreCitySites(new CitySiteFilter { TopN = 100, MaxDanger = 3 },
            new QueryOptions { History = new RuinHistory(target) });

        Assert.DoesNotContain(withRuin, s => s.TileIndex == target);

        var (_, danger) = world.SampleBiome(world.Map.DataStore.GetGeoCoord(target),
            new QueryOptions { History = new RuinHistory(target) });
        var (_, baseDanger) = world.SampleBiome(world.Map.DataStore.GetGeoCoord(target));
        Assert.True(danger > baseDanger);
    }

    [Fact]
    public void FullBuild_IsDeterministic_EndToEnd()
    {
        using var first = new WorldBuilder().WithSize(2).WithSeed(11).Build();
        using var second = new WorldBuilder().WithSize(2).WithSeed(11).Build();

        var a = first.Map.DataStore.GetSpan<ElevationInfo>();
        var b = second.Map.DataStore.GetSpan<ElevationInfo>();
        for (int i = 0; i < a.Length; i++)
            Assert.Equal(a[i].Height, b[i].Height);

        var citiesA = first.ScoreCitySites(new CitySiteFilter { TopN = 5 });
        var citiesB = second.ScoreCitySites(new CitySiteFilter { TopN = 5 });
        Assert.Equal(citiesA.Count, citiesB.Count);
        for (int i = 0; i < citiesA.Count; i++)
        {
            Assert.Equal(citiesA[i].TileIndex, citiesB[i].TileIndex);
            Assert.Equal(citiesA[i].Score, citiesB[i].Score);
        }
    }
}
