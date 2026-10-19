using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Peoples;
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
        var founders = context.Founders;
        if (founders.Count > context.MaxPolities)
            throw new InvalidOperationException($"{founders.Count} pinned founders exceed MaxPolities ({context.MaxPolities}).");

        // Pinned capitals first (the author's choice is exempt from the spacing rule); the scored draw fills the rest.
        var located = new List<int>();
        var pinnedAt = new int[founders.Count];
        for (int i = 0; i < founders.Count; i++)
        {
            pinnedAt[i] = -1;
            var f = founders[i];
            if (!f.HasLocation) continue;
            int tile = NearestLand(elev, vectors, GeoToVector(f.Latitude!.Value, f.Longitude!.Value), located);
            if (tile < 0) throw new InvalidOperationException($"Founder {i} has no free land tile to take as a capital.");
            pinnedAt[i] = tile;
            located.Add(tile);
        }

        var autos = new List<int>();
        var stream = HistoryRng.Create(history.Seed, nameof(PolityFormStage), 0, 0, "capital");

        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < tileCount && located.Count + autos.Count < context.MaxPolities; i++)
        {
            if (elev[i].Height < HistoryTerritory.LandThreshold) continue;
            int score = ScoreTile(store, elev, hydro, hasHydro, capacity, hasCapacity, depositTiles, neighbors, i, out _, out _);
            double accept = score <= 0 ? 0.0 : (double)score / (score + 400.0);
            if (stream.NextDouble() >= accept) continue;
            bool spaced = true;
            foreach (int taken in autos)
            {
                if (Vector3D.Dot(vectors[i], vectors[taken]) >= minDot) { spaced = false; break; }
            }
            if (spaced)
                foreach (int taken in located)
                {
                    if (Vector3D.Dot(vectors[i], vectors[taken]) >= minDot) { spaced = false; break; }
                }
            if (!spaced) continue;
            autos.Add(i);
        }

        if (autos.Count + located.Count == 0)
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
            if (best >= 0 && context.MaxPolities > 0) autos.Add(best);
        }

        // Polity i is founder i: its pinned capital, else the next scored one, else (when the draw ran short) the best unused land.
        var capitals = new List<int>();
        int next = 0;
        for (int i = 0; i < founders.Count; i++)
        {
            if (pinnedAt[i] >= 0) { capitals.Add(pinnedAt[i]); continue; }
            if (next < autos.Count) { capitals.Add(autos[next++]); continue; }
            int pad = BestUnused(store, elev, hydro, hasHydro, capacity, hasCapacity, depositTiles, neighbors, capitals.Concat(located).Concat(autos));
            if (pad < 0) throw new InvalidOperationException($"Founder {i} has no free land tile to take as a capital.");
            capitals.Add(pad);
        }
        for (; next < autos.Count; next++) capitals.Add(autos[next]);

        PeoplesStore? peoplesStore = null;
        bool loaded = false;
        var peoples = HistoryRng.Create(history.Seed, nameof(PolityFormStage), 0, 0, "peoples");
        for (int id = 0; id < capitals.Count; id++)
        {
            int tile = capitals[id];
            ScoreTile(store, elev, hydro, hasHydro, capacity, hasCapacity, depositTiles, neighbors, tile,
                out bool coastal, out bool depositNear);
            int arable = hasCapacity ? capacity[tile].Arable : (byte)0;
            int pasture = hasCapacity ? capacity[tile].Pasture : (byte)0;
            var landForm = SelectForm(coastal, depositNear, arable, pasture);
            string folk = "human", culture = "human_common";
            if (history.Seeds.Count > 0)
            {
                var pair = history.Seeds[(int)peoples.NextUInt((uint)history.Seeds.Count)];
                folk = pair.FolkKey;
                culture = pair.CultureKey;
            }
            var founder = id < founders.Count ? founders[id] : null;
            if (founder is not null) (folk, culture) = PinnedPeople(founder, folk, culture, history.Seeds, id);
            PolityForm form;
            if (founder?.Form is { } pinnedForm) form = pinnedForm;
            else
            {
                if (!loaded) { peoplesStore = PeoplesLoader.LoadStore(history); loaded = true; }
                CultureDef? def = null;
                peoplesStore?.Cultures.TryGetValue(culture, out def);
                var weights = FormAffinity.WeightsOf(def);
                form = weights is null ? landForm
                    : FormAffinity.Pick(weights, landForm, HistoryRng.Create(history.Seed, nameof(PolityFormStage), id, 0, "form").NextDouble());
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

    /// <summary>A founder's folk and culture: its pins over the drawn pair. A folk alone takes the roster's first culture for it; a culture alone, the roster's first folk for it.</summary>
    private static (string Folk, string Culture) PinnedPeople(HistoryFounder f, string folk, string culture, List<HistoryPeople> roster, int id)
    {
        bool hasFolk = !string.IsNullOrWhiteSpace(f.FolkKey), hasCulture = !string.IsNullOrWhiteSpace(f.CultureKey);
        if (!hasFolk && !hasCulture) return (folk, culture);
        if (hasFolk && hasCulture) return (f.FolkKey.Trim(), f.CultureKey.Trim());
        if (hasFolk)
        {
            var match = roster.FirstOrDefault(p => string.Equals(p.FolkKey, f.FolkKey.Trim(), StringComparison.OrdinalIgnoreCase));
            return match is null
                ? throw new InvalidDataException($"Founder {id} names folk '{f.FolkKey}' but no culture, and the roster has none for it.")
                : (match.FolkKey, match.CultureKey);
        }
        var byCulture = roster.FirstOrDefault(p => string.Equals(p.CultureKey, f.CultureKey.Trim(), StringComparison.OrdinalIgnoreCase));
        return byCulture is null
            ? throw new InvalidDataException($"Founder {id} names culture '{f.CultureKey}' but no folk, and the roster has none for it.")
            : (byCulture.FolkKey, byCulture.CultureKey);
    }

    private static Vector3D GeoToVector(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude < -90 || latitude > 90) throw new ArgumentOutOfRangeException(nameof(latitude), "Founder latitude must be within -90..90.");
        if (double.IsNaN(longitude) || longitude < -180 || longitude > 180) throw new ArgumentOutOfRangeException(nameof(longitude), "Founder longitude must be within -180..180.");
        return Vector3D.FromGeoCoord(new GeoCoord(latitude, longitude));
    }

    /// <summary>The land tile closest to <paramref name="target"/> that is not in <paramref name="exclude"/> (lowest index on a tie), or -1.</summary>
    private static int NearestLand(Span<ElevationInfo> elev, ReadOnlySpan<Vector3D> vectors, Vector3D target, List<int> exclude)
    {
        int best = -1;
        double bestDot = double.NegativeInfinity;
        for (int i = 0; i < elev.Length; i++)
        {
            if (elev[i].Height < HistoryTerritory.LandThreshold || exclude.Contains(i)) continue;
            double dot = Vector3D.Dot(vectors[i], target);
            if (dot > bestDot) { bestDot = dot; best = i; }
        }
        return best;
    }

    private static int BestUnused(
        WorldDataStore store, Span<ElevationInfo> elev, Span<HydrologyInfo> hydro, bool hasHydro, Span<LandCapacityInfo> capacity,
        bool hasCapacity, HashSet<int> depositTiles, Span<int> neighbors, IEnumerable<int> used)
    {
        var skip = new HashSet<int>(used);
        int best = -1, bestScore = int.MinValue;
        for (int i = 0; i < elev.Length; i++)
        {
            if (elev[i].Height < HistoryTerritory.LandThreshold || skip.Contains(i)) continue;
            int score = ScoreTile(store, elev, hydro, hasHydro, capacity, hasCapacity, depositTiles, neighbors, i, out _, out _);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
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
