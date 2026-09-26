using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Prospectable resources seeded from tectonic + climate setting.</summary>
public enum DepositType : byte
{
    Iron = 0,
    Copper = 1,
    Gold = 2,
    Silver = 3,
    Tin = 4,
    LeadZinc = 5,
    Uranium = 6,
    Coal = 7,
    Oil = 8,
    Gas = 9,
    Salt = 10,
    Gems = 11,
    Bauxite = 12
}

/// <summary>Deposit richness class, derived from prospectivity score.</summary>
public enum DepositSize : byte
{
    Small = 0,
    Medium = 1,
    Large = 2,
    Rich = 3
}

/// <summary>Sparse feature: one ore/fuel/mineral occurrence on a tile.</summary>
public sealed class Deposit
{
    public int Id { get; }
    public DepositType Type { get; }
    public int TileIndex { get; }
    public float Richness { get; }
    public DepositSize Size { get; }

    public Deposit(int id, DepositType type, int tileIndex, float richness)
    {
        Id = id;
        Type = type;
        TileIndex = tileIndex;
        Richness = richness;
        Size = richness >= 0.85f ? DepositSize.Rich
            : richness >= 0.65f ? DepositSize.Large
            : richness >= 0.45f ? DepositSize.Medium
            : DepositSize.Small;
    }
}

/// <summary>Managed catalog of mineral deposits (overlay, not a dense layer).</summary>
public sealed class DepositCatalog
{
    public List<Deposit> Deposits { get; } = new();

    public List<Deposit> AtTile(int tileIndex)
    {
        var found = new List<Deposit>();
        foreach (var d in Deposits)
        {
            if (d.TileIndex == tileIndex) found.Add(d);
        }
        return found;
    }
}

/// <summary>
/// Seeds ore, fuel, and mineral deposits from the world's tectonic and climate
/// setting, following simplified metallogenic rules: porphyry copper/gold in
/// continental arcs, tin in collisional granites, iron on cratons, lead-zinc
/// on rifted margins, coal in wet lowland basins, oil/gas in marine basins
/// and deltas, salt on playas, gems in metamorphic belts and diamond pipes,
/// bauxite under tropical weathering, uranium on shields and sandstone.
/// Deterministic for a given map + seed; tiles are ranked per type and the
/// top prospects win, so deposits are sparse by construction.
/// </summary>
public static class DepositCatalogBuilder
{
    // Expected occurrences per type at the reference size (642 tiles); scaled
    // linearly with tile count.
    private static readonly Dictionary<DepositType, double> Abundance = new()
    {
        { DepositType.Iron, 6 },
        { DepositType.Copper, 5 },
        { DepositType.Gold, 4 },
        { DepositType.Silver, 3 },
        { DepositType.Tin, 2 },
        { DepositType.LeadZinc, 4 },
        { DepositType.Uranium, 2 },
        { DepositType.Coal, 6 },
        { DepositType.Oil, 5 },
        { DepositType.Gas, 3 },
        { DepositType.Salt, 3 },
        { DepositType.Gems, 2 },
        { DepositType.Bauxite, 3 },
    };

    private const double MinScore = 0.35;
    private const int ReferenceTiles = 642;

    public static void Populate(WorldMap map, DepositCatalog catalog, int seed)
    {
        var store = map.DataStore;
        if (!store.IsLayerRegistered<TectonicPlate>() || !store.IsLayerRegistered<ElevationInfo>()) return;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>();
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;

        int n = store.TileCount;
        var vectors = store.GetTileVectors();
        Span<int> scratch = stackalloc int[6];

        var beds = new float[n];
        for (int i = 0; i < n; i++) beds[i] = elev[i].Height;

        // Neighborhood context: water, rivers, relief.
        var nearWater = new bool[n];
        var nearRiver = new bool[n];
        var relief = new float[n];
        for (int i = 0; i < n; i++)
        {
            relief[i] = CanyonAnalysis.Relief(store, beds, i, scratch);
            if (!hasHydro) continue;
            if (hydro[i].WaterBodyId >= 0) nearWater[i] = true;
            if (hydro[i].IsRiver == 1) nearRiver[i] = true;
            int adjacent = store.GetAdjacent(i, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = scratch[k];
                if (hydro[nb].WaterBodyId >= 0) nearWater[i] = true;
                if (hydro[nb].IsRiver == 1) nearRiver[i] = true;
            }
        }

        int nextId = 0;
        foreach (DepositType type in Enum.GetValues<DepositType>())
        {
            int want = Math.Max(1, (int)Math.Round(Abundance[type] * n / (double)ReferenceTiles));
            var scored = new List<(int Tile, double Score)>();
            for (int i = 0; i < n; i++)
            {
                double temp = hasClimate ? climate[i].Temperature : 0.5;
                double precip = hasClimate ? climate[i].Precipitation : 0.5;
                double s = Score(type, seed, vectors[i], plates[i], beds[i], temp, precip,
                    hasHydro ? hydro[i] : default, hasHydro, nearWater[i], nearRiver[i], relief[i]);
                if (s >= MinScore) scored.Add((i, s));
            }
            // Best prospects win; index tie-break keeps ids deterministic.
            scored.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                return c != 0 ? c : a.Tile.CompareTo(b.Tile);
            });
            int take = Math.Min(want, scored.Count);
            var winners = scored.GetRange(0, take);
            winners.Sort((a, b) => a.Tile.CompareTo(b.Tile));
            foreach (var (tile, score) in winners)
                catalog.Deposits.Add(new Deposit(nextId++, type, tile, (float)Math.Min(1.0, score)));
        }
    }

    private static double Noise01(Vector3D v, int seed, double freq)
        => SphereNoise.Fbm(v * freq, seed) * 0.5 + 0.5;

    private static double Score(
        DepositType type, int seed, Vector3D v, TectonicPlate plate, float h,
        double temp, double precip, HydrologyInfo hydro, bool hasHydro,
        bool nearWater, bool nearRiver, float relief)
    {
        bool land = h >= ElevationGenerationStage.SeaLevel;
        bool cont = plate.Crust == CrustType.Continental;
        bool conv = plate.NearestBoundary == PlateBoundaryType.Convergent;
        bool div = plate.NearestBoundary == PlateBoundaryType.Divergent;
        double oro = Math.Abs(plate.Orogeny);
        double dist = plate.BoundaryDistance;
        double n = Noise01(v, seed + 500 + (int)type * 17, 3.0);
        bool playa = hasHydro && hydro.IsPlaya == 1;

        return type switch
        {
            // Porphyry belts in continental arcs (Andes).
            DepositType.Copper => !land ? 0
                : conv && cont ? 0.40 + Math.Min(oro, 1.5) * 0.45 + n * 0.30
                : 0.10 + n * 0.15,
            // Orogenic gold in convergent belts + placer boost on rivers.
            DepositType.Gold => !land ? 0
                : conv && oro > 0.05 ? 0.35 + Math.Min(oro, 1.5) * 0.40 + n * 0.30 + (nearRiver ? 0.15 : 0)
                : nearRiver && oro > 0.02 ? 0.30 + n * 0.25
                : 0.08 + n * 0.15,
            // Epithermal silver on active margins.
            DepositType.Silver => !land ? 0
                : (conv || div) ? 0.30 + Math.Min(oro, 1.2) * 0.35 + n * 0.35
                : 0.10 + n * 0.15,
            // Tin granites in collisional highlands.
            DepositType.Tin => !land ? 0
                : conv && cont && h > 0.30 ? 0.40 + Math.Min(oro, 1.5) * 0.40 + n * 0.30
                : 0.05 + n * 0.12,
            // MVT/SEDEX on rifts, passive margins, and shallow platforms.
            DepositType.LeadZinc => (div && dist < 3) || (!land && h > -0.35) || (land && cont && h < 0.25 && oro < 0.2)
                ? 0.35 + n * 0.45 : 0.08 + n * 0.12,
            // Banded iron on stable cratons (banded noise).
            DepositType.Iron => !land || !cont ? 0
                : dist > 3 && h is > -0.1f and < 0.45f
                    ? 0.40 + Math.Max(0, Noise01(v, seed + 911, 9.0) - 0.35) * 1.1 + n * 0.20
                    : 0.12 + n * 0.15,
            // Unconformity/sandstone uranium on shields and arid basins.
            DepositType.Uranium => !land ? 0
                : (cont && dist > 2.5 && relief < 0.2) || (cont && h < 0.25 && precip < 0.45)
                    ? 0.35 + n * 0.40 : 0.08 + n * 0.12,
            // Coal forests in warm wet lowlands near water or rift basins.
            DepositType.Coal => !land || !cont ? 0
                : h < 0.30 && temp is > 0.30 and < 0.80 && precip > 0.50
                    ? 0.45 + (precip - 0.5) * 0.5 + (nearWater ? 0.15 : 0) + (div ? 0.10 : 0) + n * 0.20
                    : 0.05 + n * 0.10,
            // Oil in marine basins, passive margins, and deltas; onshore basins.
            DepositType.Oil => !land && h > -0.45
                ? 0.40 + (div || dist < 3 ? 0.20 : 0) + (nearRiver ? 0.20 : 0) + n * 0.25
                : land && cont && h < 0.20 && oro < 0.20
                    ? 0.30 + (nearRiver ? 0.15 : 0) + n * 0.30
                    : 0.05 + n * 0.08,
            // Gas shares oil basins, favoring deeper burial.
            DepositType.Gas => !land && h is > -0.60f and < 0.02f
                ? 0.38 + Math.Min(-h * 1.2, 0.25) + (div || dist < 3 ? 0.15 : 0) + n * 0.25
                : land && cont && h < 0.15 && oro < 0.15
                    ? 0.28 + n * 0.30
                    : 0.05 + n * 0.08,
            // Evaporites on playas and arid coastal lagoons.
            DepositType.Salt => playa ? 0.75 + n * 0.25
                : land && h < 0.20 && precip < 0.35 && temp > 0.50 && nearWater
                    ? 0.45 + n * 0.35
                    : 0.03 + n * 0.08,
            // Gems in collision metamorphics + rare diamond pipes on cratons.
            DepositType.Gems => !land ? 0
                : conv && cont && oro > 0.25 && h > 0.25
                    ? 0.40 + n * 0.35
                    : cont && dist > 3 && Noise01(v, seed + 913, 11.0) > 0.72
                        ? 0.55 + n * 0.25
                        : 0.05 + n * 0.10,
            // Bauxite under hot wet weathering on low relief.
            DepositType.Bauxite => !land || !cont ? 0
                : temp > 0.62 && precip > 0.60 && h is >= 0f and < 0.30f && relief < 0.22
                    ? 0.45 + n * 0.30
                    : 0.04 + n * 0.08,
            _ => 0,
        };
    }
}
