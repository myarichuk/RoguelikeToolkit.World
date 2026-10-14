using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 80: diffuses loanwords and openness along active trade routes. Every turn, in route
/// order, each route opened by <see cref="TradeRoadStage"/> and not yet severed by
/// <see cref="WarStage"/> rolls to carry one exchange: a
/// <see cref="HistoryEventKind.Exchange"/> event (tags <c>loanword</c> and the route tag) plus one
/// point of openness drift on both endpoint polities (bounded by
/// <see cref="HistoryDemography.MaxOpennessDrift"/>). Diffusion spends no budget — the road
/// network itself is the throttle — but stays under the catalog's per-turn event cap like every
/// other operator. Every draw is a pure function of <c>(seed, turn, route, purpose)</c>.
/// </summary>
public sealed class ExchangeStage : HistoryPluginBase
{
    /// <summary>Per-route, per-turn chance of one exchange while the route stays active.</summary>
    public const double ExchangeChance = 0.5;

    /// <summary>Diagnostic name, <c>Exchange</c>.</summary>
    public override string Name => "Exchange";
    /// <summary><see cref="StageOrder.Exchange"/> (80).</summary>
    public override int Order => StageOrder.Exchange;
    /// <summary>None: routes and owners come from the catalog alone.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True once polities have formed and at least one road segment exists.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
        => history.PolitiesFormed && history.Roads.Count > 0;

    /// <summary>Diffuses along every active route turn by turn over the whole span.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        var routes = new List<(int EndA, int EndB)>();
        foreach (var road in history.Roads)
        {
            var route = (road.RouteA, road.RouteB);
            if (!routes.Contains(route)) routes.Add(route);
        }
        routes.Sort();

        for (int turn = 1; turn < context.TurnCount; turn++)
        {
            var drifted = new HashSet<int>();
            for (int index = 0; index < routes.Count; index++)
            {
                var (endA, endB) = routes[index];
                if (!IsActive(history, endA, endB, turn)) continue;
                var stream = HistoryRng.Create(history.Seed, Name, turn, index, "diffuse");
                if (stream.NextDouble() >= ExchangeChance) continue;
                int polityA = history.OwnerOf(endA);
                int polityB = history.OwnerOf(endB);
                int actor = polityA >= 0 ? polityA : -1;
                history.AppendEvent(HistoryEventKind.Exchange, turn, actor, endA,
                    new[] { "exchange", "loanword", HistoryDemography.RouteTag(endA, endB) });
                if (polityA >= 0 && polityA < history.Polities.Count && drifted.Add(polityA))
                    Drift(history.Polities[polityA]);
                if (polityB >= 0 && polityB < history.Polities.Count && drifted.Add(polityB))
                    Drift(history.Polities[polityB]);
            }
        }
    }

    private static bool IsActive(HistoryCatalog history, int endA, int endB, int turn)
    {
        bool found = false;
        foreach (var road in history.Roads)
        {
            if (road.RouteA != endA || road.RouteB != endB) continue;
            found = true;
            if (road.FoundedTurn > turn) return false;
            if (road.Severed && turn >= road.SeveredTurn) return false;
        }
        return found;
    }

    private static void Drift(Polity polity)
    {
        if (polity.OpennessDrift < HistoryDemography.MaxOpennessDrift) polity.OpennessDrift++;
    }
}
