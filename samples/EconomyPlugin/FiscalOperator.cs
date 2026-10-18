using RoguelikeToolkit.World.Core.History;

namespace EconomyPlugin;

/// <summary>
/// Fiscal decisions and their social cost. Reads last turn's wars (shocks), sets the tax, spends on
/// enforcement, grows unrest from heavy tax, scarcity, graft and war, and feeds unrest above
/// <see cref="UnrestLeak"/> into <c>pressure</c>, which the built-in Rebellion and Collapse rules read.
/// A war against a better-endowed neighbour also logs an <c>econ/resource-war</c> plugin event citing the war.
/// </summary>
public sealed class FiscalOperator : IHistoryOperator
{
    /// <summary>Treasury cost of a war turn, paid by both sides.</summary>
    public const double WarCost = 8;
    /// <summary>Treasury below which a polity at war raises tax.</summary>
    public const double WarChestFloor = 20;
    /// <summary>Tax step per decision.</summary>
    public const double TaxStep = 0.05;
    /// <summary>Unrest above which a state cuts tax to calm the people.</summary>
    public const double CalmUnrest = 0.5;
    /// <summary>Unrest above which it leaks into pressure.</summary>
    public const double UnrestLeak = 0.5;
    /// <summary>Pressure added per unit of unrest above <see cref="UnrestLeak"/>.</summary>
    public const double LeakRate = 0.2;
    /// <summary>Graft level that prompts an enforcement drive.</summary>
    public const double GraftLimit = 0.3;
    /// <summary>Treasury needed for an enforcement drive.</summary>
    public const double EnforcementCost = 20;
    /// <summary>A resource-war cause needs the defender to hold this many times the attacker's stock.</summary>
    public const double RichnessRatio = 1.25;
    /// <summary>Plugin event kind of a resource-war cause (it cites the war as its cause).</summary>
    public const string ResourceWarTag = EconomyScalars.Namespace + "/resource-war";

    /// <inheritdoc />
    public string Name => EconomyScalars.Namespace + "/fiscal";

    /// <inheritdoc />
    public int Order => 400;

    /// <inheritdoc />
    public void Propose(TurnView view, ProposalSink sink)
    {
        var h = EconomyHandles.Resolve(view.Scalars);
        if (h is null) return;
        var wars = LastTurnWars(view);
        double[]? totals = null;

        foreach (var polity in view.Polities)
        {
            if (!polity.Alive) continue;
            int id = polity.Id;
            double wealth = polity.Scalars[h.Wealth];
            double tax = polity.Scalars[h.Tax];
            double corruption = polity.Scalars[h.Corruption];
            double unrest = polity.Scalars[h.Unrest];
            double pressure = polity.Scalars[ScalarRegistry.Pressure];
            double scarcity = Math.Max(0, pressure - HistoryDemography.CrowdingStart);
            bool atWar = wars.TryGetValue(id, out var war);

            if (atWar) sink.Adjust(id, h.Wealth, -WarCost);

            if (atWar && wealth < WarChestFloor && sink.TrySpendDecision())
                sink.Adjust(id, h.Tax, TaxStep);
            else if (unrest > CalmUnrest && sink.TrySpendDecision())
                sink.Adjust(id, h.Tax, -TaxStep);
            else
                sink.Adjust(id, h.Tax, Math.Clamp(EconomyScalars.BaseTax - tax, -0.01, 0.01));

            if (corruption > GraftLimit && wealth >= EnforcementCost && sink.TrySpendDecision())
            {
                sink.Adjust(id, h.Wealth, -EnforcementCost);
                sink.Adjust(id, h.Corruption, -0.1);
            }

            double unrestDelta = (tax - EconomyScalars.BaseTax) * 0.4 + scarcity * 0.5 + corruption * 0.05
                + (atWar ? 0.05 : 0) - 0.03;
            sink.Adjust(id, h.Unrest, unrestDelta);
            double next = Math.Clamp(unrest + unrestDelta, 0, 1);
            if (next > UnrestLeak) sink.Adjust(id, ScalarRegistry.Pressure, (next - UnrestLeak) * LeakRate);

            if (atWar && war.Attacker == id)
            {
                totals ??= Resources.Totals(view);
                TryLogResourceWar(view, sink, totals, war);
            }
        }
    }

    private readonly record struct War(int Attacker, int Defender, int EventId, int Tile);

    // Attacker and defender of every war logged last turn; a polity in several wars keeps the lowest event id.
    private static Dictionary<int, War> LastTurnWars(TurnView view)
    {
        var map = new Dictionary<int, War>();
        if (view.Turn < 1) return map;
        foreach (var p in view.Polities)
        {
            var events = view.EventsOf(p.Id);
            for (int i = events.Count - 1; i >= 0 && events[i].Turn >= view.Turn - 1; i--)
            {
                var e = events[i];
                if (e.Kind != HistoryEventKind.War || e.Turn != view.Turn - 1) continue;
                int defender = DefenderOf(e);
                if (defender < 0) continue;
                var war = new War(e.PolityId, defender, e.Id, e.TileIndex);
                Keep(map, war.Attacker, war);
                Keep(map, war.Defender, war);
            }
        }
        return map;
    }

    private static void Keep(Dictionary<int, War> map, int polity, War war)
    {
        if (!map.TryGetValue(polity, out var old) || war.EventId < old.EventId) map[polity] = war;
    }

    private static int DefenderOf(HistoryEvent e)
    {
        foreach (var tag in e.Tags)
            if (tag.StartsWith("defender:", StringComparison.Ordinal)
                && int.TryParse(tag.AsSpan("defender:".Length), out int id)) return id;
        return -1;
    }

    private static void TryLogResourceWar(TurnView view, ProposalSink sink, double[] totals, War war)
    {
        int best = -1;
        double bestGap = 0;
        for (int k = 0; k < Resources.Kinds; k++)
        {
            double mine = totals[war.Attacker * Resources.Kinds + k];
            double theirs = totals[war.Defender * Resources.Kinds + k];
            if (theirs < 20 || theirs < mine * RichnessRatio) continue;
            if (theirs - mine > bestGap) { bestGap = theirs - mine; best = k; }
        }
        if (best < 0) return;
        sink.PluginEvent(ResourceWarTag, war.Attacker, war.Tile,
            new[] { $"resource:{(ResourceKind)best}".ToLowerInvariant(), $"attacker:{war.Attacker}", $"defender:{war.Defender}" },
            cause: war.EventId);
    }
}
