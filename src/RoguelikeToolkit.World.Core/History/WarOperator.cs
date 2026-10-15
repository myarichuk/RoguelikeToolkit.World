using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 50: raids between neighboring polities. Every turn, in polity order, each crowded
/// polity picks a bordering polity and rolls against its pressure (plus its founding culture's
/// xenophobia, plus the pair's grudge weight) to raid it: the war appends a
/// <see cref="HistoryEventKind.War"/> event plus a battlefield site entry on the battlefield tile,
/// severs every trade route between the two capitals (one <see cref="HistoryEventKind.RoadCut"/>
/// event per route), and pressures both sides. Pairs under a treaty or tribute non-aggression
/// window are skipped; a battlefield on a holy tile appends a <see cref="HistoryEventKind.Desecration"/>
/// event and raises grudges (plus a coalition of the holy owner's neighbors). Wars cost one
/// decision from this operator's own per-turn budget, so a noisy war exhausts only itself. Every draw
/// is a pure function of <c>(seed, turn, attacker, purpose)</c>: this stage never reads another
/// operator's draws.
/// </summary>
public sealed class WarOperator : IHistoryOperator
{
    /// <summary>War chance per unit of crowding pressure.</summary>
    public const double WarPressureScale = 0.4;

    /// <summary>War chance per point of founding-culture xenophobia (0..100).</summary>
    public const double WarXenophobiaScale = 0.0015;

    /// <summary>Ceiling of the per-attacker, per-turn war chance.</summary>
    public const double MaxWarChance = 0.6;

    /// <summary>Minimum crowding pressure before a polity considers raiding.</summary>
    public const double MinWarPressure = 0.02;

    /// <summary>War chance per point of grudge weight between attacker and defender.</summary>
    public const double GrudgeWarScale = 0.02;

    /// <summary>Grudge weight a desecration raises between the desecrator and the holy owner.</summary>
    public const int DesecrationGrudge = 3;

    /// <summary>Grudge weight a desecration raises between the desecrator and each of the holy owner's neighbors (the coalition).</summary>
    public const int DesecrationCoalitionGrudge = 1;

    private readonly Dictionary<string, int> _xenophobia = new(StringComparer.OrdinalIgnoreCase);
    private PeoplesStore? _store;
    private bool _storeLoaded;

    /// <summary>Diagnostic name, <c>War</c>.</summary>
    public string Name => "War";

    /// <summary><see cref="StageOrder.War"/> (50).</summary>
    public int Order => StageOrder.War;

    /// <summary>Proposes this turn's raids from the snapshot (pressure, grudges and windows as of the end of the last turn).</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count < 2) return;
        if (!_storeLoaded)
        {
            _store = view.LoadPeoples();
            _storeLoaded = true;
        }
        var map = view.Map;
        int[] ownership = view.OwnershipSnapshot();
        var severedNow = new HashSet<(int, int)>();
        var sitedNow = new HashSet<int>();
        Span<int> neighbors = stackalloc int[6];

        foreach (var polity in polities)
        {
            if (!polity.Alive) continue;
            int attacker = polity.Id;
            double pressure = view.ScalarOf(attacker, ScalarRegistry.Pressure);
            if (pressure < MinWarPressure) continue;
            if (!_xenophobia.TryGetValue(polity.CultureKey, out int xeno))
            {
                xeno = XenophobiaOf(polity.CultureKey, _store);
                _xenophobia[polity.CultureKey] = xeno;
            }
            var bordering = HistoryNeighbors.Bordering(map.DataStore, ownership, attacker);
            if (bordering.Count == 0) continue;
            var stream = sink.Rng(attacker, "raid");
            int defender = bordering[(int)stream.NextUInt((uint)bordering.Count)];
            if (view.IsWindowed(attacker, defender)) continue;
            double chance = Math.Min(pressure * WarPressureScale + xeno * WarXenophobiaScale
                + view.GrudgeWeight(attacker, defender) * GrudgeWarScale, MaxWarChance);
            if (stream.NextDouble() >= chance) continue;
            if (!sink.TrySpendDecision()) continue;
            int battlefield = BattlefieldTile(view, neighbors, attacker, defender);
            sink.Event(HistoryEventKind.War, attacker, battlefield,
                new[] { "war", $"attacker:{attacker}", $"defender:{defender}" });
            if (!view.HasSite(battlefield) && sitedNow.Add(battlefield))
            {
                sink.Emit(new Proposal.AddSite
                {
                    EntityId = attacker,
                    Tile = battlefield,
                    Position = map.DataStore.GetGeoCoord(battlefield),
                });
            }
            CheckDesecration(view, sink, ownership, attacker, defender, battlefield);
            SeverRoutes(view, sink, severedNow, attacker, polity.CapitalTile, polities[defender].CapitalTile);
            sink.Adjust(attacker, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
            sink.Adjust(defender, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
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

    private static void CheckDesecration(
        TurnView view, ProposalSink sink, int[] ownership, int attacker, int defender, int battlefield)
    {
        int holyOwner = -1;
        foreach (var site in view.HolySites)
        {
            if (site.Active && site.Tile == battlefield) { holyOwner = site.PolityId; break; }
        }
        if (holyOwner < 0) return;
        sink.Event(HistoryEventKind.Desecration, attacker, battlefield,
            new[] { "desecration", $"by:{attacker}", $"holy:{holyOwner}", $"tile:{battlefield}" });
        sink.Emit(new Proposal.AddGrudge { EntityId = attacker, Other = holyOwner, Weight = DesecrationGrudge });
        foreach (int neighbor in HistoryNeighbors.Bordering(view.Map.DataStore, ownership, holyOwner))
        {
            if (neighbor != attacker)
                sink.Emit(new Proposal.AddGrudge { EntityId = neighbor, Other = attacker, Weight = DesecrationCoalitionGrudge });
        }
        sink.Adjust(attacker, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
    }

    private static int BattlefieldTile(TurnView view, Span<int> neighbors, int attacker, int defender)
    {
        var store = view.Map.DataStore;
        for (int tile = 0; tile < store.TileCount && tile < view.TileCount; tile++)
        {
            if (view.OwnerOf(tile) != defender) continue;
            int adjacent = store.GetAdjacent(tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                if (view.OwnerOf(neighbors[k]) == attacker) return tile;
            }
        }
        return view.Polities[defender].CapitalTile;
    }

    private static void SeverRoutes(
        TurnView view, ProposalSink sink, HashSet<(int, int)> severedNow, int attacker, int capitalA, int capitalB)
    {
        int endA = Math.Min(capitalA, capitalB);
        int endB = Math.Max(capitalA, capitalB);
        if (severedNow.Contains((endA, endB))) return;
        bool live = false;
        foreach (var road in view.Roads)
        {
            if (road.RouteA == endA && road.RouteB == endB && !road.Severed) { live = true; break; }
        }
        if (!live) return;
        severedNow.Add((endA, endB));
        sink.Emit(new Proposal.SeverRoute { EntityId = attacker, RouteA = endA, RouteB = endB });
        sink.Event(HistoryEventKind.RoadCut, attacker, capitalA,
            new[] { "road", "cut", HistoryDemography.RouteTag(endA, endB) });
    }
}
