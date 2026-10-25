using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 50: raids between neighboring polities. Every turn, in polity order, each crowded
/// polity picks a bordering polity and rolls against its pressure (plus its founding culture's
/// xenophobia, plus the pair's grudge weight) to raid it: the war appends a
/// <see cref="HistoryEventKind.War"/> event plus a `battlefield` scar on the battlefield tile,
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

    /// <summary>Chance a raid also takes the nearest defending settlement outright, when the attacker is strong enough and it is not a capital.</summary>
    public const double CaptureChance = 0.2;

    /// <summary>Chance a raid razes the nearest defending settlement, when the attacker is strong enough and it is not a capital.</summary>
    public const double RazeChance = 0.1;

    /// <summary>Chance a raid that neither takes nor razes sacks the nearest defending settlement.</summary>
    public const double SackChance = 0.5;

    /// <summary>Share of a sacked settlement's people who die.</summary>
    public const double SackCasualty = 0.08;

    /// <summary>Share of a sacked settlement's people who flee into the displaced pool.</summary>
    public const double SackDisplacement = 0.15;

    /// <summary>Share of a razed settlement's people who die before the rest flee.</summary>
    public const double RazeCasualty = 0.3;

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
        var severedNow = new HashSet<(int, int)>();
        var sitedNow = new HashSet<int>();

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
            var bordering = view.BorderingPolities(attacker);
            if (bordering.Count == 0) continue;
            var stream = sink.Rng(attacker, "raid");
            int defender = bordering[(int)stream.NextUInt((uint)bordering.Count)];
            if (view.IsWindowed(attacker, defender)) continue;
            double chance = Math.Min(pressure * WarPressureScale + xeno * WarXenophobiaScale
                + view.GrudgeWeight(attacker, defender) * GrudgeWarScale, MaxWarChance);
            if (stream.NextDouble() >= chance) continue;
            if (!sink.TrySpendDecision()) continue;
            int battlefield = BattlefieldTile(view, attacker, defender);
            sink.Event(HistoryEventKind.War, attacker, battlefield,
                new[] { "war", $"attacker:{attacker}", $"defender:{defender}" });
            if (!view.HasSite(battlefield) && sitedNow.Add(battlefield))
            {
                sink.Emit(new Proposal.AddScar
                {
                    EntityId = attacker,
                    Tile = battlefield,
                    ScarKind = HistoryScar.Battlefield,
                    Cause = "war",
                    Position = map.DataStore.GetGeoCoord(battlefield),
                });
            }
            CheckDesecration(view, sink, attacker, defender, battlefield);
            SeverRoutes(view, sink, severedNow, attacker, polity.CapitalTile, polities[defender].CapitalTile);
            Assault(view, sink, severedNow, polity, polities[defender], battlefield);
            sink.Adjust(attacker, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
            sink.Adjust(defender, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
        }
    }

    /// <summary>
    /// The raid reaches the defender's settlement nearest the battlefield. A stronger attacker
    /// (<see cref="HistoryDemography.ConquestAdvantage"/>) may take it or raze it; a capital is only ever sacked.
    /// Otherwise the settlement may be sacked: some die, some flee into the displaced pool.
    /// </summary>
    private static void Assault(
        TurnView view, ProposalSink sink, HashSet<(int, int)> severedNow, TurnView.PolityState attacker, TurnView.PolityState defender, int battlefield)
    {
        var vectors = view.Map.DataStore.GetTileVectors();
        if ((uint)battlefield >= (uint)vectors.Length) return;
        TurnView.SettlementState target = default;
        double best = double.NegativeInfinity;
        bool found = false;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId != defender.Id) continue;
            double dot = Vector3D.Dot(vectors[battlefield], vectors[s.Tile]);
            if (dot > best) { best = dot; target = s; found = true; }
        }
        if (!found) return;
        var rng = sink.Rng(attacker.Id, "assault");
        double roll = rng.NextDouble();
        int attackerStanding = 0;
        foreach (var s in view.Settlements)
            if (!s.Ended && s.PolityId == attacker.Id) attackerStanding++;
        bool canHold = attackerStanding < view.MaxSettlementsPerPolity;
        bool strong = target.Role != SettlementRole.Capital
            && attacker.Population >= HistoryDemography.ConquestAdvantage * Math.Max(defender.Population, 1);
        if (strong && canHold && roll < CaptureChance)
        {
            var capture = new Proposal.CaptureSettlement { EntityId = attacker.Id, Settlement = target.Index };
            sink.Emit(capture);
            sink.EventIfApplied(capture, HistoryEventKind.SettlementCaptured, attacker.Id, target.Tile,
                new[] { $"settlement:{target.Index}", $"from:{defender.Id}", $"to:{attacker.Id}" });
            return;
        }
        if (strong && roll < CaptureChance + RazeChance)
        {
            var raze = new Proposal.EndSettlement { EntityId = defender.Id, Settlement = target.Index, EndCause = "razed", LostShare = RazeCasualty };
            sink.Emit(raze);
            sink.Emit(new Proposal.AddScar
            {
                Requires = raze, // no ruin where a rival already took the town this turn
                EntityId = defender.Id, Tile = target.Tile, ScarKind = HistoryScar.Ruin, Cause = "war",
                Position = view.Map.DataStore.GetGeoCoord(target.Tile),
            });
            var live = new HashSet<(int, int)>();
            foreach (var road in view.Roads)
                if (!road.Severed && (road.RouteA == target.Tile || road.RouteB == target.Tile)) live.Add((road.RouteA, road.RouteB));
            foreach (var (a, b) in live) SeverRoutes(view, sink, severedNow, attacker.Id, a, b);
            return;
        }
        if (roll < CaptureChance + RazeChance + SackChance * (1 - CaptureChance - RazeChance))
        {
            sink.Emit(new Proposal.ReleasePopulation { EntityId = defender.Id, Settlement = target.Index, Share = SackDisplacement });
            sink.Emit(new Proposal.KillPopulation
            {
                EntityId = defender.Id, Settlement = target.Index, Share = Math.Min(1, SackCasualty / (1 - SackDisplacement)),
            });
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
        TurnView view, ProposalSink sink, int attacker, int defender, int battlefield)
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
        foreach (int neighbor in view.BorderingPolities(holyOwner))
        {
            if (neighbor != attacker)
                sink.Emit(new Proposal.AddGrudge { EntityId = neighbor, Other = attacker, Weight = DesecrationCoalitionGrudge });
        }
        sink.Adjust(attacker, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
    }

    /// <summary>The lowest-index defender tile on the attacker's frontier, else the defender's capital.</summary>
    private static int BattlefieldTile(TurnView view, int attacker, int defender)
    {
        var frontier = view.FrontierTiles(attacker, defender);
        return frontier.Count > 0 ? frontier[0] : view.Polities[defender].CapitalTile;
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
