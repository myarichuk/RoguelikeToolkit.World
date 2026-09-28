using System;
using System.Collections.Generic;
using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Site injector pipeline: ordering, duplicate-order, determinism, visibility.</summary>
public class SiteInjectorTests
{
    private static readonly GeoCoord TestCenter = new(20, 30);
    private const double TestRadiusKm = 120.0;

    private sealed class StubInjector : ISiteInjector
    {
        public string Id { get; }
        public int Order { get; }
        public SiteTier Tier { get; }
        private readonly SiteKind _kind;
        private readonly int _cell;
        public List<int> ObservedExistingCounts { get; } = new();

        public StubInjector(string id, int order, SiteKind kind, int cell, SiteTier tier = SiteTier.Both)
        {
            Id = id;
            Order = order;
            _kind = kind;
            _cell = cell;
            Tier = tier;
        }

        public IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
        {
            ObservedExistingCounts.Add(context.ExistingSites.Count);
            return new[]
            {
                new PlacedSite
                {
                    Kind = _kind, CellIndex = _cell, FootprintRadius = 0,
                    NameSeed = context.Rng.NextUInt(), Tags = new[] { "stub" }, InjectorId = Id,
                },
            };
        }
    }

    private static RegionHandle TestRegion(int size = 8)
    {
        var up = Vector3D.FromGeoCoord(TestCenter);
        double lonR = TestCenter.Longitude * GeoCoord.Deg2Rad;
        var east = new Vector3D(-Math.Sin(lonR), Math.Cos(lonR), 0).Normalize();
        var north = Vector3D.Cross(up, east).Normalize();
        var parent = new ParentContext
        {
            MeanElevation = 0.3f,
            DominantBiome = BiomeType.Plains,
            MeanTemperature = 0.6f,
            MeanPrecipitation = 0.5f,
            MeanMoisture = 0.5f,
            ElevationGradient = north * -0.4,
            AspectRadians = 0.0,
            OrogenyStrike = east,
            NeighborHeights = new[] { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f },
            Wind = north,
        };
        return RegionMaps.DeriveRegionMap(
            MapAddress.ForRegion(7, 10, -1), parent,
            TestCenter, TestRadiusKm, MapSeeds.DeriveRegionSeed(7, 10), size);
    }

    private static string SiteSig(PlacedSite s)
        => $"{s.Kind}@{s.CellIndex}#{s.NameSeed}:{string.Join("+", s.Tags)}:{s.DangerDelta}/{s.HabitabilityDelta}";

    [Fact]
    public void Injectors_RunInOrder_RegardlessOfInputOrder()
    {
        var region = TestRegion();
        var late = new StubInjector("late", 20, SiteKind.Ruin, 5);
        var early = new StubInjector("early", 5, SiteKind.City, 9);
        // Input order is late-first; execution must follow Order.
        var catalog = region.WithInjectors(new ISiteInjector[] { late, early });

        Assert.Equal(2, catalog.Sites.Count);
        Assert.Equal("early", catalog.Sites[0].InjectorId);
        Assert.Equal("late", catalog.Sites[1].InjectorId);
        Assert.Equal(0, early.ObservedExistingCounts[0]);
        Assert.Equal(1, late.ObservedExistingCounts[0]);
    }

    [Fact]
    public void DuplicateOrder_ThrowsOnBothHandles()
    {
        var region = TestRegion();
        var injectors = new ISiteInjector[]
        {
            new StubInjector("a", 7, SiteKind.Ruin, 1),
            new StubInjector("b", 7, SiteKind.City, 2),
        };
        Assert.Throws<InvalidOperationException>(() => region.WithInjectors(injectors));
        Assert.Throws<InvalidOperationException>(() => region.GetLocal(3, 8).WithInjectors(injectors));
    }

    [Fact]
    public void LaterInjector_DoesNotReshuffleEarlierSites()
    {
        var region = TestRegion();
        var settlement = new SettlementInjector(maxSites: 3);
        var ruin = new RuinInjector(maxSites: 2);

        var before = region.WithInjectors(new ISiteInjector[] { settlement });
        var after = region.WithInjectors(new ISiteInjector[] { settlement, ruin });

        Assert.NotEmpty(before.Sites);
        Assert.Equal(before.Sites.Count, after.OfKind(SiteKind.City).Count);
        for (int k = 0; k < before.Sites.Count; k++)
            Assert.Equal(SiteSig(before.Sites[k]), SiteSig(after.Sites[k]));

        // Full determinism across identical runs.
        var rerun = region.WithInjectors(new ISiteInjector[] { settlement, ruin });
        Assert.Equal(after.Sites.Count, rerun.Sites.Count);
        for (int k = 0; k < after.Sites.Count; k++)
            Assert.Equal(SiteSig(after.Sites[k]), SiteSig(rerun.Sites[k]));
    }

    [Fact]
    public void LaterInjector_SeesEarlierSites()
    {
        var region = TestRegion();
        var first = new StubInjector("first", 1, SiteKind.Landmark, 4);
        var second = new StubInjector("second", 2, SiteKind.Ruin, 6);
        var catalog = region.WithInjectors(new ISiteInjector[] { first, second });

        Assert.Equal(1, second.ObservedExistingCounts[0]);
        Assert.Equal(2, catalog.Sites.Count);
        Assert.Equal(SiteKind.Landmark, catalog.Sites[0].Kind);
    }

    [Fact]
    public void TierFilter_SkipsNonMatchingInjectors()
    {
        var region = TestRegion();
        var catalog = region.WithInjectors(new ISiteInjector[]
        {
            new StubInjector("region-only", 1, SiteKind.City, 2, SiteTier.Region),
            new StubInjector("local-only", 2, SiteKind.Ruin, 3, SiteTier.Local),
        });
        Assert.Single(catalog.Sites);
        Assert.Equal("region-only", catalog.Sites[0].InjectorId);

        var localCatalog = region.GetLocal(3, 8).WithInjectors(new ISiteInjector[]
        {
            new StubInjector("region-only", 1, SiteKind.City, 2, SiteTier.Region),
            new StubInjector("local-only", 2, SiteKind.Ruin, 3, SiteTier.Local),
        });
        Assert.Single(localCatalog.Sites);
        Assert.Equal("local-only", localCatalog.Sites[0].InjectorId);
    }

    [Fact]
    public void MineInjector_PlacesMinesOnlyWithDeposits()
    {
        var region = TestRegion();
        var empty = new DepositCatalog();
        var none = region.WithInjectors(new ISiteInjector[] { new MineInjector(empty) });
        Assert.Empty(none.Sites);

        var ores = new DepositCatalog();
        ores.Deposits.Add(new Deposit(0, DepositType.Iron, region.WorldTileIndex, 0.9f));
        var mined = region.WithInjectors(new ISiteInjector[] { new MineInjector(ores) });
        Assert.Single(mined.Sites);
        Assert.Equal(SiteKind.Mine, mined.Sites[0].Kind);
        Assert.Contains("iron", mined.Sites[0].Tags);
    }

    [Fact]
    public void Builtins_ProduceSaneSitesOnLocalMap()
    {
        var region = TestRegion();
        var local = region.GetLocal(10, 8);
        var catalog = local.WithInjectors(new ISiteInjector[]
        {
            new SettlementInjector(maxSites: 2),
            new RuinInjector(maxSites: 1),
            new LandmarkInjector(),
        });
        Assert.Equal(4, catalog.Sites.Count);
        foreach (var site in catalog.Sites)
        {
            Assert.InRange(site.CellIndex, 0, local.Tiles.Length - 1);
            Assert.NotEmpty(site.Tags);
            Assert.NotEmpty(site.InjectorId);
        }
        Assert.Equal(2, catalog.OfKind(SiteKind.City).Count);
        Assert.Single(catalog.OfKind(SiteKind.Ruin));
        Assert.Single(catalog.OfKind(SiteKind.Landmark));
    }
}
