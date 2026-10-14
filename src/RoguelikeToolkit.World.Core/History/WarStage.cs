using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 50: raids between neighboring polities. Every turn, in polity order, each crowded
/// polity rolls against its pressure (plus its founding culture's xenophobia) to raid one
/// bordering polity: the war appends a <see cref="HistoryEventKind.War"/> event plus a battlefield
/// site entry on the battlefield tile, severs every trade route between the two capitals (one
/// <see cref="HistoryEventKind.RoadCut"/> event per route), and pressures both sides. Wars cost one
/// decision from this stage's own per-turn budget, so a noisy war exhausts only itself. Every draw
/// is a pure function of <c>(seed, turn, attacker, purpose)</c>: this stage never reads another
/// operator's draws.
/// </summary>
public sealed class WarStage : HistoryPluginBase
{
    /// <summary>War chance per unit of crowding pressure.</summary>
    public const double WarPressureScale = 0.4;

    /// <summary>War chance per point of founding-culture xenophobia (0..100).</summary>
    public const double WarXenophobiaScale = 0.0015;

    /// <summary>Ceiling of the per-attacker, per-turn war chance.</summary>
    public const double MaxWarChance = 0.6;

    /// <summary>Minimum crowding pressure before a polity considers raiding.</summary>
    public const double MinWarPressure = 0.02;

    /// <summary>Diagnostic name, <c>War</c>.</summary>
    public override string Name => "War";
    /// <summary><see cref="StageOrder.War"/> (50).</summary>
    public override int Order => StageOrder.War;
    /// <summary>None: neighbors come from the catalog ownership map plus tile adjacency.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True once at least two polities share the map.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
        => history.PolitiesFormed && history.Polities.Count >= 2;

    /// <summary>Raids turn by turn over the whole span.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        PeoplesStore? store = null;
        try
        {
            store = PeoplesStore.Load();
        }
        catch (PeoplesDataException)
        {
            store = null;
        }

        double[] capacities = HistoryDemography.CarryingCapacities(map.DataStore, history.Polities, history.Ownership);
        var xenophobia = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Span<int> neighbors = stackalloc int[6];

        for (int turn = 1; turn < context.TurnCount; turn++)
        {
            for (int attacker = 0; attacker < history.Polities.Count; attacker++)
            {
                var polity = history.Polities[attacker];
                var folk = HistoryDemography.ResolveFolk(polity.FolkKey, store);
                double rate = HistoryDemography.AnnualToTurnRate(folk.GrowthR, context.TurnYears);
                double pressure = HistoryDemography.PressureAtTurn(capacities[attacker], rate, turn)
                    + HistoryDemography.EventPressureBonus(history.Events, attacker, turn - 1);
                if (pressure < MinWarPressure) continue;
                if (!xenophobia.TryGetValue(polity.CultureKey, out int xeno))
                {
                    xeno = XenophobiaOf(polity.CultureKey, store);
                    xenophobia[polity.CultureKey] = xeno;
                }
                double chance = Math.Min(pressure * WarPressureScale + xeno * WarXenophobiaScale, MaxWarChance);
                var stream = HistoryRng.Create(history.Seed, Name, turn, attacker, "raid");
                if (stream.NextDouble() >= chance) continue;
                var bordering = BorderingPolities(map, history, neighbors, attacker);
                if (bordering.Count == 0) continue;
                int defender = bordering[(int)stream.NextUInt((uint)bordering.Count)];
                if (!context.TrySpendOperatorDecision(Name, turn)) continue;
                int battlefield = BattlefieldTile(map, history, neighbors, attacker, defender);
                history.AppendEvent(HistoryEventKind.War, turn, attacker, battlefield,
                    new[] { "war", $"attacker:{attacker}", $"defender:{defender}" });
                AddBattlefieldSite(map, history, attacker, battlefield, turn);
                SeverRoutes(history, turn, attacker, polity.CapitalTile, history.Polities[defender].CapitalTile);
                polity.Pressure = Math.Min(polity.Pressure + HistoryDemography.WarPressureBump, HistoryDemography.MaxPressure);
                history.Polities[defender].Pressure =
                    Math.Min(history.Polities[defender].Pressure + HistoryDemography.WarPressureBump, HistoryDemography.MaxPressure);
            }
        }
    }

    private static int XenophobiaOf(string cultureKey, PeoplesStore? store)
    {
        if (!string.IsNullOrWhiteSpace(cultureKey) && store != null
            && store.Cultures.TryGetValue(cultureKey, out var culture))
        {
            return culture.Xenophobia;
        }
        if (string.Equals(cultureKey, "dwarven_clan", StringComparison.OrdinalIgnoreCase)) return 60;
        if (string.Equals(cultureKey, "human_common", StringComparison.OrdinalIgnoreCase)) return 30;
        return 30;
    }

    private static List<int> BorderingPolities(
        WorldMap map, HistoryCatalog history, Span<int> neighbors, int attacker)
    {
        var bordering = new List<int>();
        for (int tile = 0; tile < map.DataStore.TileCount && tile < history.Ownership.Length; tile++)
        {
            if (history.Ownership[tile] != attacker) continue;
            int adjacent = map.DataStore.GetAdjacent(tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int owner = history.OwnerOf(neighbors[k]);
                if (owner >= 0 && owner != attacker && !bordering.Contains(owner)) bordering.Add(owner);
            }
        }
        bordering.Sort();
        return bordering;
    }

    private static int BattlefieldTile(
        WorldMap map, HistoryCatalog history, Span<int> neighbors, int attacker, int defender)
    {
        for (int tile = 0; tile < map.DataStore.TileCount && tile < history.Ownership.Length; tile++)
        {
            if (history.Ownership[tile] != defender) continue;
            int adjacent = map.DataStore.GetAdjacent(tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                if (history.OwnerOf(neighbors[k]) == attacker) return tile;
            }
        }
        return history.Polities[defender].CapitalTile;
    }

    private static void AddBattlefieldSite(
        WorldMap map, HistoryCatalog history, int attacker, int battlefield, int turn)
    {
        foreach (var site in history.Sites)
            if (site.TileIndex == battlefield) return;
        history.Sites.Add(new HistorySite
        {
            TileIndex = battlefield,
            PolityId = attacker,
            FoundedTurn = turn,
            Position = map.DataStore.GetGeoCoord(battlefield),
            Active = true,
        });
    }

    private static void SeverRoutes(HistoryCatalog history, int turn, int attacker, int capitalA, int capitalB)
    {
        int endA = Math.Min(capitalA, capitalB);
        int endB = Math.Max(capitalA, capitalB);
        bool severed = false;
        foreach (var road in history.Roads)
        {
            if (road.RouteA != endA || road.RouteB != endB || road.Severed) continue;
            road.Severed = true;
            road.SeveredTurn = turn;
            severed = true;
        }
        if (severed)
        {
            history.AppendEvent(HistoryEventKind.RoadCut, turn, attacker, capitalA,
                new[] { "road", "cut", HistoryDemography.RouteTag(endA, endB) });
        }
    }
}
