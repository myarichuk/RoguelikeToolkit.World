using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 70: splits far-flung, oppressed regions off as new polities. Every turn, in polity
/// order, a polity whose crowding pressure (plus its accumulated war bumps) passes
/// <see cref="OppressionThreshold"/>, whose culture holds tight (<see cref="TightnessThreshold"/>
/// collectivism or more), and which owns a large-enough far province, may see that province
/// break away: the rebel capital is its most distant owned tile, ownership is re-flooded from
/// all capitals (user pins win, exactly like <see cref="HistoryTerritory"/>), and a
/// <see cref="HistoryEventKind.Rebellion"/> event records the parent. Rebellions cost one
/// emergence from this stage's own per-turn budget and never exceed
/// <see cref="HistoryContext.MaxPolities"/> total polities.
/// </summary>
public sealed class RebellionStage : HistoryPluginBase
{
    /// <summary>Minimum owned tiles before a polity can lose a province.</summary>
    public const int MinRebelTiles = 8;

    /// <summary>Minimum arc between a capital and its breakaway province, in degrees.</summary>
    public const double MinSeparationDegrees = 12.0;

    /// <summary>Crowding pressure (plus war bumps) at which an oppressed province snaps.</summary>
    public const double OppressionThreshold = 0.35;

    /// <summary>Minimum parent-culture collectivism (0..100) for a tight-culture break.</summary>
    public const int TightnessThreshold = 60;

    /// <summary>Per-province, per-turn breakaway chance once every condition holds.</summary>
    public const double RebelChance = 0.3;

    /// <summary>Diagnostic name, <c>Rebellion</c>.</summary>
    public override string Name => "Rebellion";
    /// <summary><see cref="StageOrder.Rebellion"/> (70).</summary>
    public override int Order => StageOrder.Rebellion;
    /// <summary>None required: ownership is re-flooded through the store with or without elevation.</summary>
    public override Type[] Reads => Array.Empty<Type>();
    /// <summary>Elevation (the land mask); absent layers flood every tile.</summary>
    public override Type[] ReadsOptional => new[] { typeof(ElevationInfo) };

    /// <summary>True once polities have formed.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history) => history.PolitiesFormed;

    /// <summary>Checks every polity every turn over the whole span.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        PeoplesStore? store = PeoplesLoader.LoadStore(history);

        double separationDot = DetMath.Cos(MinSeparationDegrees * GeoCoord.Deg2Rad);
        double[] capacities = HistoryDemography.CarryingCapacities(map.DataStore, history.Polities, history.Ownership);

        for (int turn = 1; turn < context.TurnCount; turn++)
        {
            for (int parent = 0; parent < history.Polities.Count; parent++)
            {
                if (history.Polities.Count >= context.MaxPolities) return;
                var polity = history.Polities[parent];
                if (polity.FoundedTurn >= turn) continue;
                int owned = CountOwned(history, parent);
                if (owned < MinRebelTiles) continue;
                if (CollectivismOf(polity.CultureKey, store) < TightnessThreshold) continue;
                var folk = HistoryDemography.ResolveFolk(polity.FolkKey, store);
                double rate = HistoryDemography.AnnualToTurnRate(folk.GrowthR, context.TurnYears);
                double pressure = HistoryDemography.PressureAtTurn(capacities[parent], rate, turn)
                    + HistoryDemography.EventPressureBonus(history.Events, parent, turn - 1);
                if (pressure < OppressionThreshold) continue;
                int capital = FarthestTile(map, history, polity);
                if (capital < 0) continue;
                var vectors = map.DataStore.GetTileVectors();
                if (Vector3D.Dot(vectors[polity.CapitalTile], vectors[capital]) > separationDot) continue;
                var stream = HistoryRng.Create(history.Seed, Name, turn, parent, "break");
                if (stream.NextDouble() >= RebelChance) continue;
                if (!context.TrySpendOperatorEmergence(Name, turn)) continue;
                FoundRebel(map, history, parent, capital, turn);
                capacities = HistoryDemography.CarryingCapacities(map.DataStore, history.Polities, history.Ownership);
            }
        }
    }

    private static int CollectivismOf(string cultureKey, PeoplesStore? store)
    {
        if (!string.IsNullOrWhiteSpace(cultureKey) && store != null
            && store.Cultures.TryGetValue(cultureKey, out var culture))
        {
            return culture.Collectivism;
        }
        if (string.Equals(cultureKey, "dwarven_clan", StringComparison.OrdinalIgnoreCase)) return 80;
        if (string.Equals(cultureKey, "human_common", StringComparison.OrdinalIgnoreCase)) return 50;
        return 50;
    }

    private static int CountOwned(HistoryCatalog history, int polityId)
    {
        int owned = 0;
        for (int tile = 0; tile < history.Ownership.Length; tile++)
            if (history.Ownership[tile] == polityId) owned++;
        return owned;
    }

    private static int FarthestTile(WorldMap map, HistoryCatalog history, Polity polity)
    {
        var vectors = map.DataStore.GetTileVectors();
        var home = vectors[polity.CapitalTile];
        int best = -1;
        double bestDot = double.MaxValue;
        for (int tile = 0; tile < history.Ownership.Length; tile++)
        {
            if (history.Ownership[tile] != polity.Id || tile == polity.CapitalTile) continue;
            double dot = Vector3D.Dot(home, vectors[tile]);
            if (dot < bestDot || (dot == bestDot && tile < best))
            {
                bestDot = dot;
                best = tile;
            }
        }
        return best;
    }

    private static void FoundRebel(
        WorldMap map, HistoryCatalog history, int parent, int capital, int turn)
    {
        var polity = history.Polities[parent];
        int id = history.Polities.Count;
        var rebel = new Polity
        {
            Id = id,
            Form = PolityForm.LooseAssociation,
            CapitalTile = capital,
            FolkKey = polity.FolkKey,
            CultureKey = polity.CultureKey,
            FoundedTurn = turn,
        };
        history.Polities.Add(rebel);

        var capitals = new List<int>(history.Polities.Count);
        foreach (var p in history.Polities) capitals.Add(p.CapitalTile);
        var pins = new Dictionary<int, int>();
        foreach (var kv in history.Overrides.PinnedTiles)
        {
            if (kv.Value >= 0 && kv.Value < history.Polities.Count) pins[kv.Key] = kv.Value;
        }
        history.Ownership = HistoryTerritory.AssignOwners(map.DataStore, capitals, pins);

        double[] fresh = HistoryDemography.CarryingCapacities(map.DataStore, history.Polities, history.Ownership);
        rebel.Population = Math.Max(fresh[id] * HistoryDemography.InitialShare, HistoryDemography.MinPopulation);
        rebel.Pressure = 0;
        history.AppendEvent(HistoryEventKind.Rebellion, turn, id, capital,
            new[] { "rebellion", $"parent:{parent}", rebel.FolkKey, rebel.CultureKey });
    }
}
