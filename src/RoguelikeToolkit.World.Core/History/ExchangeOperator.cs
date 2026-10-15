using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 80: diffuses loanwords and openness along active trade routes. Every turn, in route
/// order, each route opened by <see cref="TradeRoadOperator"/> and not yet severed by
/// <see cref="WarOperator"/> rolls to carry one exchange: a
/// <see cref="HistoryEventKind.Exchange"/> event (tags <c>loanword</c> and the route tag) plus one
/// point of openness drift on both endpoint polities (bounded by
/// <see cref="HistoryDemography.MaxOpennessDrift"/>). Diffusion spends no budget — the road
/// network itself is the throttle — but stays under the catalog's per-turn event cap like every
/// other operator. Every draw is a pure function of <c>(seed, turn, route, purpose)</c>.
/// </summary>
public sealed class ExchangeOperator : IHistoryOperator
{
    /// <summary>Per-route, per-turn chance of one exchange while the route stays active.</summary>
    public const double ExchangeChance = 0.5;

    /// <summary>Diagnostic name, <c>Exchange</c>.</summary>
    public string Name => "Exchange";

    /// <summary><see cref="StageOrder.Exchange"/> (80).</summary>
    public int Order => StageOrder.Exchange;

    /// <summary>Proposes this turn's exchanges along the routes still active in the snapshot.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (view.Roads.Count == 0) return;
        var routes = new Dictionary<(int EndA, int EndB), bool>();
        foreach (var road in view.Roads)
        {
            if (road.PolityId >= 0) continue; // spurs are internal to one polity; only trunks diffuse culture
            var route = (road.RouteA, road.RouteB);
            routes[route] = routes.TryGetValue(route, out bool active) ? active && !road.Severed : !road.Severed;
        }
        var ordered = routes.Where(static kv => kv.Value).Select(static kv => kv.Key).ToList();
        ordered.Sort();

        var drifted = new HashSet<int>();
        foreach (var (endA, endB) in ordered)
        {
            var stream = sink.Rng(endA * 100003 + endB, "diffuse");
            if (stream.NextDouble() >= ExchangeChance) continue;
            int polityA = view.OwnerOf(endA);
            int polityB = view.OwnerOf(endB);
            sink.Event(HistoryEventKind.Exchange, polityA >= 0 ? polityA : -1, endA,
                new[] { "exchange", "loanword", HistoryDemography.RouteTag(endA, endB) });
            if (polityA >= 0 && polityA < view.Polities.Count && drifted.Add(polityA))
                sink.Emit(new Proposal.DriftOpenness { EntityId = polityA });
            if (polityB >= 0 && polityB < view.Polities.Count && drifted.Add(polityB))
                sink.Emit(new Proposal.DriftOpenness { EntityId = polityB });
        }
    }
}
