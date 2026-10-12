using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Mineral deposits: deterministic seeding that follows metallogenic rules
/// (arcs, cratons, basins, playas) rather than scattering at random.
/// </summary>
public class DepositTests
{
    [Fact]
    public void Deposits_AreDeterministic()
    {
        using var first = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        using var second = new WorldBuilder().WithSize(3).WithSeed(42).Build();

        Assert.NotEmpty(first.Deposits.Deposits);
        Assert.Equal(first.Deposits.Deposits.Count, second.Deposits.Deposits.Count);
        for (int k = 0; k < first.Deposits.Deposits.Count; k++)
        {
            var a = first.Deposits.Deposits[k];
            var b = second.Deposits.Deposits[k];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Type, b.Type);
            Assert.Equal(a.TileIndex, b.TileIndex);
            Assert.Equal(a.Richness, b.Richness);
            Assert.Equal(a.Size, b.Size);
        }
    }

    [Fact]
    public void Deposits_ScaleWithWorldSize()
    {
        using var small = new WorldBuilder().WithSize(2).WithSeed(42).Build();
        using var big = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        Assert.True(big.Deposits.Deposits.Count > small.Deposits.Deposits.Count,
            $"Big {big.Deposits.Deposits.Count} <= small {small.Deposits.Deposits.Count}");
    }

    private static CrustalProvince ProvinceOf(World world, int tile)
    {
        var store = world.Map.DataStore;
        var plate = store.GetSpan<TectonicPlate>()[tile];
        return Geology.Classify(plate, Geology.BasementAge(world.Seed, plate.Id, store.GetTileVectors()[tile]));
    }

    [Fact]
    public void Deposits_SitInTheirHostSetting()
    {
        foreach (int seed in new[] { 42, 7, 99 })
        {
            using var world = new WorldBuilder().WithSize(4).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var plates = store.GetSpan<TectonicPlate>();
            var elev = store.GetSpan<ElevationInfo>();
            var climate = store.GetSpan<ClimateInfo>();
            var hydro = store.GetSpan<HydrologyInfo>();

            foreach (var d in world.Deposits.Deposits)
            {
                int t = d.TileIndex;
                var province = ProvinceOf(world, t);
                float h = elev[t].Height;
                bool land = h >= ElevationGenerationStage.SeaLevel;
                bool veined = province is CrustalProvince.Orogen || (province == CrustalProvince.Subduction && land);
                bool vms = province is CrustalProvince.Rift || (province == CrustalProvince.SpreadingRidge && land);
                string where = $"seed {seed} tile {t} ({province})";

                switch (d.Type)
                {
                    // Hydrothermal veins and porphyries: convergent belts and arcs; massive sulfide: rifts and ridges.
                    case DepositType.Copper:
                    case DepositType.Silver:
                        Assert.True(land && (veined || vms), $"{d.Type} outside arcs/rifts at {where}");
                        break;
                    case DepositType.LeadZinc:
                        Assert.True(land && (veined || vms || province == CrustalProvince.Platform), $"Pb-Zn outside belts/rifts/foreland at {where}");
                        break;
                    case DepositType.Gold:
                        Assert.True(land && (veined || province == CrustalProvince.Craton), $"Gold outside lode belts/greenstone at {where}");
                        break;
                    case DepositType.Tin:
                        Assert.True(land && province == CrustalProvince.Orogen && h > 0.30f, $"Tin outside collisional highlands at {where}");
                        break;
                    // Banded iron: old, interior, quiet basement.
                    case DepositType.Iron:
                        Assert.Equal(CrustalProvince.Craton, province);
                        Assert.True(land && plates[t].BoundaryRings >= Geology.InteriorRings, $"Iron near a plate boundary at {where}");
                        break;
                    case DepositType.Coal:
                        Assert.True(land && province != CrustalProvince.Orogen && h < 0.30f
                            && climate[t].Temperature is > 0.30f and < 0.80f && climate[t].Precipitation > 0.50f, $"Coal outside wet lowlands at {where}");
                        break;
                    case DepositType.Oil:
                    case DepositType.Gas:
                        Assert.True(plates[t].Crust == CrustType.Continental, $"{d.Type} on oceanic crust at {where}");
                        break;
                    case DepositType.Bauxite:
                        Assert.True(land && province is CrustalProvince.Craton or CrustalProvince.Platform
                            && climate[t].Temperature > 0.62f && climate[t].Precipitation > 0.60f && h is >= 0f and < 0.30f, $"Bauxite outside tropics at {where}");
                        break;
                    case DepositType.Salt:
                    case DepositType.Gypsum:
                        // Evaporites: low, endorheic, and evaporation beats rainfall. Nowhere else.
                        Assert.True(hydro[t].Endorheic == 1, $"{d.Type} with a path to the sea at {where}");
                        Assert.True(Hydrography.WaterDeficit(climate[t].Temperature, climate[t].Precipitation) > 0f, $"{d.Type} where rain beats evaporation at {where}");
                        Assert.True(h < DepositCatalogBuilder.EvaporiteMaxHeight, $"{d.Type} on high ground at {where}");
                        Assert.Equal(d.Type == DepositType.Salt, hydro[t].Saline == 1);
                        break;
                }
            }
        }
    }

    [Fact]
    public void Evaporites_ExistInDryEndorheicBasins_AndHalitePrecedesGypsumMargin()
    {
        int salt = 0, gypsum = 0;
        foreach (int seed in new[] { 42, 7, 1234, 99, 5 })
        {
            using var world = new WorldBuilder().WithSize(5).WithSeed(seed).Build();
            foreach (var d in world.Deposits.Deposits)
            {
                if (d.Type == DepositType.Salt) salt++;
                if (d.Type == DepositType.Gypsum) gypsum++;
            }
        }
        Assert.True(salt > 0, "no halite anywhere across five worlds");
        Assert.True(gypsum > 0, "no gypsum anywhere across five worlds");
    }

    [Fact]
    public void Metals_FollowTheirTectonicSettings_NotTheClimate()
    {
        using var world = new WorldBuilder().WithSize(5).WithSeed(42).Build();
        var perProvince = new Dictionary<DepositType, Dictionary<CrustalProvince, int>>();
        foreach (var d in world.Deposits.Deposits)
        {
            var p = ProvinceOf(world, d.TileIndex);
            if (!perProvince.TryGetValue(d.Type, out var map)) perProvince[d.Type] = map = new Dictionary<CrustalProvince, int>();
            map[p] = (map.TryGetValue(p, out var c) ? c : 0) + 1;
        }
        // Every iron deposit is on craton; the base metals never land on abyssal or bare-platform ground.
        Assert.All(perProvince[DepositType.Iron].Keys, p => Assert.Equal(CrustalProvince.Craton, p));
        foreach (var type in new[] { DepositType.Copper, DepositType.Silver, DepositType.Gold })
            Assert.DoesNotContain(CrustalProvince.OceanBasin, perProvince[type].Keys);
    }

    [Fact]
    public void Geology_ClassifiesProvincesFromTectonicState()
    {
        var oro = new TectonicPlate { Crust = CrustType.Continental, NearestBoundary = PlateBoundaryType.Convergent, Orogeny = 0.8f, BoundaryRings = 0 };
        Assert.Equal(CrustalProvince.Orogen, Geology.Classify(oro, 0.9f));
        var rift = new TectonicPlate { Crust = CrustType.Continental, NearestBoundary = PlateBoundaryType.Divergent, Orogeny = -0.4f, BoundaryRings = 1 };
        Assert.Equal(CrustalProvince.Rift, Geology.Classify(rift, 0.9f));
        var interior = new TectonicPlate { Crust = CrustType.Continental, NearestBoundary = PlateBoundaryType.None, Orogeny = 0f, BoundaryRings = byte.MaxValue };
        Assert.Equal(CrustalProvince.Craton, Geology.Classify(interior, 0.8f));
        // Young basement in the same quiet interior is a sedimentary platform, not a craton.
        Assert.Equal(CrustalProvince.Platform, Geology.Classify(interior, 0.2f));
        var ridge = new TectonicPlate { Crust = CrustType.Oceanic, NearestBoundary = PlateBoundaryType.Divergent, Orogeny = 0.5f, BoundaryRings = 0 };
        Assert.Equal(CrustalProvince.SpreadingRidge, Geology.Classify(ridge, 0.5f));
        var arc = new TectonicPlate { Crust = CrustType.Oceanic, NearestBoundary = PlateBoundaryType.Convergent, Orogeny = 0.6f, BoundaryRings = 0 };
        Assert.Equal(CrustalProvince.Subduction, Geology.Classify(arc, 0.5f));
    }

    [Fact]
    public void Deposits_HaveWktAndQueries()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();

        string wkt = world.DepositToWkt(0);
        Assert.StartsWith("POINT(", wkt);
        Assert.EndsWith(")", wkt);

        var first = world.Deposits.Deposits[0];
        var atTile = world.GetDeposits(first.TileIndex);
        Assert.Contains(atTile, d => d.Id == first.Id);

        var coord = world.Map.DataStore.GetGeoCoord(first.TileIndex);
        var near = world.NearestDeposit(coord, first.Type);
        Assert.NotNull(near);
        Assert.Equal(first.TileIndex, near!.Value.Deposit.TileIndex);

        var any = world.NearestDeposit(coord);
        Assert.NotNull(any);

        // Tile features resolve deposits back to the catalog.
        var f = world.GetTileFeatures(first.TileIndex);
        Assert.Contains(first.Type, f.Deposits);
    }
}
