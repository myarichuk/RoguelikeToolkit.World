using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Lithosphere;

/// <summary>
/// Seeds ore, fuel, and mineral deposits from the crust that hosts them, the
/// way an exploration geologist ranks ground, not by sprinkling. Each commodity
/// has a host rule and no fallback: a tile that is not in the host setting
/// scores zero, so a deposit is always explicable by where it sits.
/// <list type="bullet">
/// <item>Orogens and magmatic arcs (convergent belts): hydrothermal veins and
/// porphyries carrying Cu, Pb-Zn, Ag and Au; collisional granites with Sn;
/// metamorphic gems.</item>
/// <item>Cratons and shields (old, interior, quiet basement): banded iron
/// formation, greenstone and paleoplacer Au, unconformity and placer U,
/// diamond pipes.</item>
/// <item>Continental rifts (divergent): volcanogenic massive sulfide and
/// sediment-hosted Cu-Zn-Pb-Ag.</item>
/// <item>Endorheic sinks: evaporites only, and only where evaporation beats
/// rainfall and the water has no way to the sea: halite (Na) on the saline
/// sump, gypsum on its margin.</item>
/// <item>Sedimentary platforms and shelves: coal in warm wet lowlands, oil and
/// gas in basins, roll-front uranium in dry sandstone, bauxite under hot wet
/// weathering of old, flat surfaces.</item>
/// </list>
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
        { DepositType.Gypsum, 3 },
        { DepositType.Gems, 2 },
        { DepositType.Bauxite, 3 },
    };

    private const double MinScore = 0.35;
    private const int ReferenceTiles = 642;

    /// <summary>Evaporites form in depressions: no higher than this above the sea.</summary>
    public const float EvaporiteMaxHeight = 0.30f;

    /// <summary>Gypsum precipitates on the basin margin, within this many rings of the saline sump.</summary>
    public const int GypsumMarginRings = 3;

    /// <summary>Everything a rule may ask about one tile, gathered once.</summary>
    private readonly struct Site
    {
        public Vector3D Position { get; init; }
        public TectonicPlate Plate { get; init; }
        public CrustalProvince Province { get; init; }
        public float Age { get; init; }
        public float Height { get; init; }
        public float Relief { get; init; }
        public float Temperature { get; init; }
        public float Precipitation { get; init; }
        /// <summary>Evaporation minus rainfall; positive where precipitation is below evaporation.</summary>
        public float Deficit { get; init; }
        public HydrologyInfo Hydro { get; init; }
        public bool HasHydro { get; init; }
        public bool NearRiver { get; init; }
        public int CoastRings { get; init; }
        public int SalineRings { get; init; }

        public bool Land => Height >= ElevationGenerationStage.SeaLevel;
        public bool Continental => Plate.Crust == CrustType.Continental;
        /// <summary>Signed orogeny: positive uplift, negative rift/trench.</summary>
        public float Orogeny => Plate.Orogeny;
    }

    /// <summary>Appends deposits to <paramref name="catalog"/>, ranking every tile for every <see cref="DepositType"/>.</summary>
    /// <remarks>
    /// For each type the best-scoring tiles win, up to a quota scaled from the tile count (always at least one, fewer if too few tiles qualify).
    /// Needs the tectonic and elevation layers and returns without adding anything when either is missing; climate and hydrology are optional
    /// (temperature and precipitation default to 0.5). Deterministic for a given map and <paramref name="seed"/>. The catalog is not cleared; new ids continue
    /// after the highest existing id (starting at 0 for an empty catalog).
    /// </remarks>
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

        var nearRiver = new bool[n];
        var relief = new float[n];
        for (int i = 0; i < n; i++)
        {
            relief[i] = CanyonAnalysis.Relief(store, beds, i, scratch);
            if (!hasHydro) continue;
            if (hydro[i].IsRiver == 1) nearRiver[i] = true;
            int adjacent = store.GetAdjacent(i, scratch);
            for (int k = 0; k < adjacent; k++)
                if (hydro[scratch[k]].IsRiver == 1) nearRiver[i] = true;
        }

        // Rings to the nearest sea tile (arc vs. continental interior) and to the
        // nearest saline tile (gypsum margin), both capped: only short ranges matter.
        var coastRings = Rings(store, n, i => beds[i] < ElevationGenerationStage.SeaLevel, 6);
        var saline = new bool[n];
        if (hasHydro) for (int i = 0; i < n; i++) saline[i] = hydro[i].Saline == 1;
        var salineRings = Rings(store, n, i => saline[i], GypsumMarginRings);

        var sites = new Site[n];
        for (int i = 0; i < n; i++)
        {
            float age = Geology.BasementAge(seed, plates[i].Id, vectors[i]);
            float temp = hasClimate ? climate[i].Temperature : 0.5f;
            float precip = hasClimate ? climate[i].Precipitation : 0.5f;
            sites[i] = new Site
            {
                Position = vectors[i],
                Plate = plates[i],
                Province = Geology.Classify(plates[i], age),
                Age = age,
                Height = beds[i],
                Relief = relief[i],
                Temperature = temp,
                Precipitation = precip,
                Deficit = hasClimate ? Hydrography.WaterDeficit(temp, precip) : 0f,
                Hydro = hasHydro ? hydro[i] : default,
                HasHydro = hasHydro,
                NearRiver = nearRiver[i],
                CoastRings = coastRings[i],
                SalineRings = salineRings[i],
            };
        }

        int nextId = 0;
        foreach (var existing in catalog.Deposits) nextId = Math.Max(nextId, existing.Id + 1);
        foreach (DepositType type in Enum.GetValues<DepositType>())
        {
            int want = Math.Max(1, (int)Math.Round(Abundance[type] * n / (double)ReferenceTiles));
            var scored = new List<(int Tile, double Score)>();
            for (int i = 0; i < n; i++)
            {
                double s = Score(type, seed, sites[i]);
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

    /// <summary>Multi-source BFS: rings from each tile to the nearest tile satisfying <paramref name="source"/>, capped (cap + 1 = beyond).</summary>
    private static int[] Rings(WorldDataStore store, int n, Func<int, bool> source, int cap)
    {
        var rings = new int[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (source(i)) { rings[i] = 0; queue.Enqueue(i); }
            else rings[i] = cap + 1;
        }
        Span<int> scratch = stackalloc int[6];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (rings[cur] >= cap) continue;
            int adjacent = store.GetAdjacent(cur, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = scratch[k];
                if (rings[nb] > rings[cur] + 1) { rings[nb] = rings[cur] + 1; queue.Enqueue(nb); }
            }
        }
        return rings;
    }

    private static double Noise01(Vector3D v, int seed, double freq)
        => SphereNoise.Fbm(v * freq, seed) * 0.5 + 0.5;

    /// <summary>
    /// Prospectivity of one tile for one commodity, 0 when the tile is not in the commodity's host
    /// setting. The setting tests are public in spirit (see <see cref="Geology"/>); the numbers rank
    /// tiles that qualify.
    /// </summary>
    private static double Score(DepositType type, int seed, in Site s)
    {
        double n = Noise01(s.Position, seed + 500 + (int)type * 17, 3.0);
        double oro = Math.Min(Math.Max(s.Orogeny, 0f), 1.5);
        bool orogen = s.Province == CrustalProvince.Orogen;
        bool rift = s.Province == CrustalProvince.Rift;
        bool craton = s.Province == CrustalProvince.Craton;
        bool platform = s.Province == CrustalProvince.Platform;
        bool land = s.Land;
        // Island arcs are the same subduction magmatism as a continental arc, and a ridge or
        // rift that breaches the sea (Iceland, Cyprus's ophiolite) hosts the same volcanogenic sulfide.
        bool islandArc = s.Province == CrustalProvince.Subduction && land;
        bool ridge = s.Province == CrustalProvince.SpreadingRidge && land;
        bool veined = orogen || islandArc;
        bool vms = rift || ridge;
        bool arc = (orogen && s.CoastRings <= 4) || islandArc;
        double banding = Noise01(s.Position, seed + 911, 9.0);

        switch (type)
        {
            // Porphyry belts in magmatic arcs (Andes) and hydrothermal systems in
            // fold belts; Cu-Zn volcanogenic massive sulfide in rifts.
            case DepositType.Copper:
                if (!land) return 0;
                if (veined) return 0.40 + oro * 0.30 + (arc ? 0.12 : 0) + n * 0.25;
                if (vms) return 0.42 + n * 0.30;
                return 0;

            // Orogenic lode gold in fold belts, enriched where a river works it into placers;
            // Archaean greenstone gold and paleoplacers (Witwatersrand) on old cratons.
            case DepositType.Gold:
                if (!land) return 0;
                if (veined) return 0.38 + oro * 0.28 + n * 0.28 + (s.NearRiver ? 0.12 : 0);
                if (craton && s.Age > 0.70f) return 0.40 + Math.Max(0.0, banding - 0.45) * 0.9 + (s.NearRiver ? 0.12 : 0) + n * 0.15;
                return 0;

            // Epithermal silver in magmatic belts; by-product Ag of rift massive sulfides.
            case DepositType.Silver:
                if (!land) return 0;
                if (veined) return 0.34 + Math.Min(oro, 1.2) * 0.30 + n * 0.30;
                if (vms) return 0.40 + n * 0.30;
                return 0;

            // Tin granites in collisional highlands, away from the arc coast.
            case DepositType.Tin:
                if (!land || !orogen || s.Height <= 0.30f) return 0;
                return 0.40 + oro * 0.35 + n * 0.28;

            // Hydrothermal vein and skarn in fold belts; VMS/SEDEX in rifts (the richest);
            // Mississippi-Valley-type in the carbonate foreland just beyond a belt.
            case DepositType.LeadZinc:
                if (!land) return 0;
                if (vms) return 0.50 + n * 0.30;
                if (veined) return 0.36 + Math.Min(oro, 1.2) * 0.22 + n * 0.30;
                if (platform && s.Plate.NearestBoundary == PlateBoundaryType.Convergent
                    && s.Plate.BoundaryRings is >= 2 and <= 4 && s.Relief < 0.15f && s.Height < 0.30f)
                    return 0.38 + n * 0.35;
                return 0;

            // Banded iron formation: the oldest cratons, in banded layers.
            case DepositType.Iron:
                if (!land || !craton) return 0;
                return 0.42 + (s.Age - Geology.CratonMinAge) * 0.5 + Math.Max(0.0, banding - 0.40) * 0.9 + n * 0.15;

            // Unconformity-related and paleoplacer uranium on shields; roll-front in dry sandstone basins.
            case DepositType.Uranium:
                if (!land) return 0;
                if (craton) return 0.38 + s.Age * 0.15 + (s.NearRiver ? 0.12 : 0) + n * 0.30;
                if (platform && s.Precipitation < 0.45f && s.Height < 0.25f && s.Relief < 0.12f) return 0.36 + n * 0.35;
                return 0;

            // Coal measures: warm wet lowlands on sedimentary crust, not in the fold belt itself.
            case DepositType.Coal:
                if (!land || !s.Continental || orogen) return 0;
                if (s.Height < 0.30f && s.Temperature is > 0.30f and < 0.80f && s.Precipitation > 0.50f)
                    return 0.45 + (s.Precipitation - 0.5) * 0.5 + (s.Hydro.WaterBodyId >= 0 || s.NearRiver ? 0.15 : 0)
                        + (rift ? 0.10 : 0) + n * 0.20;
                return 0;

            // Oil: continental shelves and onshore basins, richer on rifted margins and deltas.
            case DepositType.Oil:
                if (!land && s.Height > -0.45f && s.Continental)
                    return 0.40 + (rift || s.Plate.BoundaryDistance < 3 ? 0.20 : 0) + (s.NearRiver ? 0.20 : 0) + n * 0.25;
                if (land && platform && s.Height < 0.20f)
                    return 0.30 + (s.NearRiver ? 0.15 : 0) + n * 0.30;
                return 0;

            // Gas shares the oil basins, favouring deeper burial.
            case DepositType.Gas:
                if (!land && s.Height is > -0.60f and < 0.02f && s.Continental)
                    return 0.38 + Math.Min(-s.Height * 1.2, 0.25) + (rift || s.Plate.BoundaryDistance < 3 ? 0.15 : 0) + n * 0.25;
                if (land && platform && s.Height < 0.15f)
                    return 0.28 + n * 0.30;
                return 0;

            // Halite (Na): the saline sump of an endorheic basin, where evaporation beats rainfall.
            case DepositType.Salt:
                if (!IsEvaporiteBasin(s) || s.Hydro.Saline != 1) return 0;
                return 0.55 + Math.Min(s.Deficit, 1f) * 0.20 + (s.Hydro.IsPlaya == 1 ? 0.10 : 0) + n * 0.15;

            // Gypsum: drops out first as the brine concentrates, so it rims the sump rather than filling it.
            case DepositType.Gypsum:
                if (!IsEvaporiteBasin(s) || s.Hydro.Saline == 1 || s.SalineRings > GypsumMarginRings) return 0;
                return 0.45 + Math.Min(s.Deficit, 1f) * 0.20 + n * 0.25;

            // Gems: metamorphic belts of the collision zone, and kimberlite pipes through ancient craton.
            case DepositType.Gems:
                if (!land) return 0;
                if (orogen && s.Plate.Orogeny > 0.25f && s.Height > 0.25f) return 0.40 + n * 0.35;
                if (craton && s.Age > 0.70f && Noise01(s.Position, seed + 913, 11.0) > 0.72) return 0.55 + n * 0.25;
                return 0;

            // Bauxite: hot wet weathering of old, flat, stable surfaces.
            case DepositType.Bauxite:
                if (!land || !s.Continental || (!platform && !craton)) return 0;
                if (s.Temperature > 0.62f && s.Precipitation > 0.60f && s.Height is >= 0f and < 0.30f
                    && s.Relief < 0.22f && s.Age > 0.35f)
                    return 0.45 + (craton ? 0.10 : 0) + n * 0.30;
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>
    /// The one setting evaporites form in: a low depression whose water has no path to the sea, in a
    /// climate where precipitation is below evaporation.
    /// </summary>
    private static bool IsEvaporiteBasin(in Site s)
        => s.Land && s.HasHydro && s.Hydro.Endorheic == 1 && s.Deficit > 0f && s.Height < EvaporiteMaxHeight;
}
