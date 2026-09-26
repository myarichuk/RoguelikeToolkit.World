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

    [Fact]
    public void Deposits_FollowTectonicAndClimateRules()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        var hydro = store.GetSpan<HydrologyInfo>();

        var seen = new HashSet<DepositType>();
        foreach (var d in world.Deposits.Deposits)
        {
            seen.Add(d.Type);
            int t = d.TileIndex;
            var p = plates[t];
            float h = elev[t].Height;
            bool land = h >= ElevationGenerationStage.SeaLevel;
            bool cont = p.Crust == CrustType.Continental;
            bool conv = p.NearestBoundary == PlateBoundaryType.Convergent;
            bool div = p.NearestBoundary == PlateBoundaryType.Divergent;
            double oro = System.Math.Abs(p.Orogeny);
            float temp = climate[t].Temperature;
            float precip = climate[t].Precipitation;

            switch (d.Type)
            {
                case DepositType.Copper:
                    Assert.True(land && conv && cont, $"Copper off-arc at {t}");
                    break;
                case DepositType.Gold:
                    Assert.True(land && ((conv && oro > 0.05) || (NearRiver(store, hydro, t) && oro > 0.02)),
                        $"Gold outside belts/placers at {t}");
                    break;
                case DepositType.Tin:
                    Assert.True(land && conv && cont && h > 0.30f, $"Tin outside collision highlands at {t}");
                    break;
                case DepositType.Iron:
                    Assert.True(land && cont && p.BoundaryDistance > 3, $"Iron off-craton at {t}");
                    break;
                case DepositType.Coal:
                    Assert.True(land && cont && h < 0.30f && temp is > 0.30f and < 0.80f && precip > 0.50f,
                        $"Coal outside wet lowlands at {t}");
                    break;
                case DepositType.Oil:
                    Assert.True((!land && h > -0.45f) || (land && cont && h < 0.20f && oro < 0.20),
                        $"Oil outside basins at {t}");
                    break;
                case DepositType.Salt:
                    bool playa = hydro[t].IsPlaya == 1;
                    bool lagoon = land && h < 0.20f && precip < 0.35f && temp > 0.50f && NearWater(store, hydro, t);
                    Assert.True(playa || lagoon, $"Salt outside playas/lagoons at {t}");
                    break;
                case DepositType.Bauxite:
                    Assert.True(land && cont && temp > 0.62f && precip > 0.60f && h is >= 0f and < 0.30f,
                        $"Bauxite outside tropics at {t}");
                    break;
                case DepositType.LeadZinc:
                    Assert.True((div && p.BoundaryDistance < 3) || (!land && h > -0.35f)
                        || (land && cont && h < 0.25f && oro < 0.2), $"LeadZinc outside margins at {t}");
                    break;
            }
            // Fallback scores can never clear the bar, so every winner above
            // had to satisfy its rule branch — except placer gold, which the
            // Gold case already allows.
        }
        Assert.True(seen.Count >= 8, $"Expected broad commodity coverage, got {seen.Count}");
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

    private static bool NearRiver(WorldDataStore store, System.Span<HydrologyInfo> hydro, int tile)
    {
        if (hydro[tile].IsRiver == 1) return true;
        System.Span<int> scratch = stackalloc int[6];
        int adjacent = store.GetAdjacent(tile, scratch);
        for (int k = 0; k < adjacent; k++)
            if (hydro[scratch[k]].IsRiver == 1) return true;
        return false;
    }

    private static bool NearWater(WorldDataStore store, System.Span<HydrologyInfo> hydro, int tile)
    {
        if (hydro[tile].WaterBodyId >= 0) return true;
        System.Span<int> scratch = stackalloc int[6];
        int adjacent = store.GetAdjacent(tile, scratch);
        for (int k = 0; k < adjacent; k++)
            if (hydro[scratch[k]].WaterBodyId >= 0) return true;
        return false;
    }
}
