using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 27: founds polities. Picks spaced capitals on land by deterministic draw (probability
/// from capacity, coastal/river access and nearby deposits), assigns each a form from local
/// conditions and a (folk, culture) pair from the seeded roster, floods tile ownership out from
/// the capitals, and appends one founding event per polity at turn 0.
/// </summary>
public sealed class PolityFormStage : HistoryPluginBase
{
    /// <summary>Minimum angular separation between capitals, in degrees.</summary>
    public const double MinCapitalSeparationDegrees = 18.0;

    /// <summary>Diagnostic name, <c>PolityForm</c>.</summary>
    public override string Name => "PolityForm";
    /// <summary><see cref="StageOrder.PolityForm"/> (27).</summary>
    public override int Order => StageOrder.PolityForm;
    /// <summary>Elevation (the land mask).</summary>
    public override Type[] Reads => new[] { typeof(ElevationInfo) };
    /// <summary>Hydrology (rivers) and land capacity (arable/pasture/game/timber); absent layers fall back to neutral values.</summary>
    public override Type[] ReadsOptional => new[] { typeof(HydrologyInfo), typeof(LandCapacityInfo) };

    /// <summary>True when the roster is seeded and no polities exist yet.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
        => history.CultureSeeded && !history.PolitiesFormed;

    /// <summary>Founds the polities; an all-ocean world (or an empty roster) validly yields none.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        var store = map.DataStore;
        int tileCount = store.TileCount;
        var elev = store.GetSpan<ElevationInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;

        var deposits = new DepositCatalog();
        DepositCatalogBuilder.Populate(map, deposits, history.Seed);
        var depositTiles = new HashSet<int>();
        foreach (var d in deposits.Deposits) depositTiles.Add(d.TileIndex);

        var vectors = store.GetTileVectors();
        double minDot = DetMath.Cos(MinCapitalSeparationDegrees * GeoCoord.Deg2Rad);
        var capitals = new List<int>();
        var capitalScore = new List<int>();
        var stream = HistoryRng.Create(history.Seed, nameof(PolityFormStage), 0, 0, "capital");

        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < tileCount && capitals.Count < context.MaxPolities; i++)
        {
            if (elev[i].Height < HistoryTerritory.LandThreshold) continue;
            int score = ScoreTile(store, elev, hydro, hasHydro, capacity, hasCapacity, depositTiles, neighbors, i, out _, out _);
            double accept = score <= 0 ? 0.0 : (double)score / (score + 400.0);
            if (stream.NextDouble() >= accept) continue;
            bool spaced = true;
            foreach (int taken in capitals)
            {
                if (Vector3D.Dot(vectors[i], vectors[taken]) >= minDot) { spaced = false; break; }
            }
            if (!spaced) continue;
            capitals.Add(i);
            capitalScore.Add(score);
        }

        if (capitals.Count == 0)
        {
            int best = -1, bestScore = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (elev[i].Height < HistoryTerritory.LandThreshold) continue;
                int score = ScoreTile(store, elev, hydro, hasHydro, capacity, hasCapacity, depositTiles, neighbors, i, out _, out _);
                if (score > bestScore || (score == bestScore && (best < 0 || i < best)))
                {
                    best = i;
                    bestScore = score;
                }
            }
            if (best >= 0 && context.MaxPolities > 0)
            {
                capitals.Add(best);
                capitalScore.Add(bestScore);
            }
        }

        var peoples = HistoryRng.Create(history.Seed, nameof(PolityFormStage), 0, 0, "peoples");
        for (int id = 0; id < capitals.Count; id++)
        {
            int tile = capitals[id];
            ScoreTile(store, elev, hydro, hasHydro, capacity, hasCapacity, depositTiles, neighbors, tile,
                out bool coastal, out bool depositNear);
            int arable = hasCapacity ? capacity[tile].Arable : (byte)0;
            int pasture = hasCapacity ? capacity[tile].Pasture : (byte)0;
            var form = SelectForm(coastal, depositNear, arable, pasture);
            string folk = "human", culture = "human_common";
            if (history.Seeds.Count > 0)
            {
                var pair = history.Seeds[(int)peoples.NextUInt((uint)history.Seeds.Count)];
                folk = pair.FolkKey;
                culture = pair.CultureKey;
            }
            var founded = new Polity
            {
                Id = id,
                Form = form,
                CapitalTile = tile,
                FolkKey = folk,
                CultureKey = culture,
                FoundedTurn = 0,
            };
            history.Polities.Add(founded);
            CapitalSettlements.Add(store, history, founded, 0);
            var tags = new List<string> { form.ToString(), folk, culture };
            history.AppendEvent(HistoryEventKind.Founding, 0, id, tile, tags);
        }

        var pins = new Dictionary<int, int>();
        foreach (var kv in history.Overrides.PinnedTiles)
        {
            if (kv.Value >= 0 && kv.Value < history.Polities.Count) pins[kv.Key] = kv.Value;
        }
        history.Ownership = HistoryTerritory.AssignOwners(store, capitals, pins);
        history.PolitiesFormed = true;
    }

    private static PolityForm SelectForm(bool coastal, bool depositNear, int arable, int pasture)
    {
        if (coastal && depositNear) return PolityForm.MerchantRepublic;
        if (arable >= 150) return PolityForm.Kingdom;
        if (pasture >= 120 && pasture >= 2 * arable) return PolityForm.Horde;
        return PolityForm.LooseAssociation;
    }

    /// <summary>True when a land tile touches open water; the test that makes a collapse successor a trading state.</summary>
    internal static bool IsCoastal(WorldDataStore store, int tile)
    {
        var elev = store.GetSpan<ElevationInfo>();
        Span<int> neighbors = stackalloc int[6];
        int adjacent = store.GetAdjacent(tile, neighbors);
        for (int k = 0; k < adjacent; k++)
            if (elev[neighbors[k]].Height < HistoryTerritory.LandThreshold) return true;
        return false;
    }

    private static int ScoreTile(
        WorldDataStore store,
        Span<ElevationInfo> elev,
        Span<HydrologyInfo> hydro,
        bool hasHydro,
        Span<LandCapacityInfo> capacity,
        bool hasCapacity,
        HashSet<int> depositTiles,
        Span<int> neighbors,
        int tile,
        out bool coastal,
        out bool depositNear)
    {
        coastal = false;
        depositNear = depositTiles.Contains(tile);
        bool river = hasHydro && hydro[tile].IsRiver != 0;
        int adjacent = store.GetAdjacent(tile, neighbors);
        for (int k = 0; k < adjacent; k++)
        {
            int other = neighbors[k];
            if (elev[other].Height < HistoryTerritory.LandThreshold) coastal = true;
            if (depositTiles.Contains(other)) depositNear = true;
            if (hasHydro && hydro[other].IsRiver != 0) river = true;
        }
        int score = hasCapacity
            ? capacity[tile].Arable + capacity[tile].Pasture + capacity[tile].Game + capacity[tile].Timber
            : 100;
        if (coastal) score += 150;
        if (river) score += 100;
        if (depositNear) score += 200;
        return score;
    }
}
