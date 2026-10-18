using RoguelikeToolkit.World.Core.History;

namespace EconomyPlugin;

/// <summary>
/// Production and revenue. Each living polity turns the land it owns into output (more with live trade
/// roads, less under scarcity), collects the baseline tax on it minus graft, and lets its estates and
/// corruption creep. The fiscal operator reads the result next turn.
/// </summary>
public sealed class EconomyOperator : IHistoryOperator
{
    /// <summary>Output gained per active road, capped at <see cref="MaxTradeBonus"/>.</summary>
    public const double TradeBonusPerRoad = 0.1;
    /// <summary>Largest trade multiplier.</summary>
    public const double MaxTradeBonus = 0.5;
    /// <summary>Fraction of output that becomes gross revenue before tax and graft.</summary>
    public const double RevenueShare = 0.5;
    /// <summary>Wealth above which elites entrench.</summary>
    public const double EntrenchWealth = 300;

    /// <inheritdoc />
    public string Name => EconomyScalars.Namespace + "/economy";

    /// <inheritdoc />
    public int Order => 300;

    /// <inheritdoc />
    public void Propose(TurnView view, ProposalSink sink)
    {
        var h = EconomyHandles.Resolve(view.Scalars);
        if (h is null) return;
        double[] totals = Resources.Totals(view);
        int[] roads = new int[view.Polities.Count];
        foreach (var road in view.Roads)
            if (!road.Severed && (uint)road.PolityId < (uint)roads.Length) roads[road.PolityId]++;

        foreach (var polity in view.Polities)
        {
            if (!polity.Alive) continue;
            double pressure = polity.Scalars[ScalarRegistry.Pressure];
            double scarcity = Math.Max(0, pressure - HistoryDemography.CrowdingStart);
            double trade = 1 + Math.Min(MaxTradeBonus, roads[polity.Id] * TradeBonusPerRoad);
            double output = Resources.Sum(totals, polity.Id) / Resources.Scale * trade * (1 - Math.Min(1, scarcity));
            double tax = polity.Scalars[h.Tax];
            double corruption = polity.Scalars[h.Corruption];
            double estates = polity.Scalars[h.Estates];
            double wealth = polity.Scalars[h.Wealth];

            double revenue = output * RevenueShare * tax * (1 - corruption) * (1 - estates * 0.5);
            sink.Adjust(polity.Id, h.Wealth, revenue - 0.5); // 0.5: upkeep of the realm
            sink.Adjust(polity.Id, h.Estates, wealth > EntrenchWealth ? 0.01 : -0.005);
            sink.Adjust(polity.Id, h.Corruption, 0.005 * estates);
        }
        WriteLedger(view, sink, totals);
    }

    // Adds this turn's flow on every standing road to the cumulative ledger in the state bag. This operator is the
    // ledger's only writer, so the read-modify-write is deterministic.
    private static void WriteLedger(TurnView view, ProposalSink sink, double[] totals)
    {
        var ledger = view.ReadPluginState(TradeLedger.Key, TradeLedger.Version, out var old) == PluginStateLookup.Found
            ? TradeLedger.Decode(old) : new SortedDictionary<(int A, int B), double[]>();
        var seen = new HashSet<(int, int)>();
        foreach (var road in view.Roads)
        {
            if (road.Severed) continue;
            int a = view.OwnerOf(road.TileA), b = view.OwnerOf(road.TileB);
            if (a < 0 || b < 0 || a == b) continue;
            var pair = a < b ? (a, b) : (b, a);
            if (!seen.Add(pair)) continue; // one flow per pair per turn, however many segments join them
            if (!ledger.TryGetValue(pair, out var volume)) ledger[pair] = volume = new double[Resources.Kinds];
            for (int k = 0; k < Resources.Kinds; k++)
                volume[k] += Math.Min(totals[pair.Item1 * Resources.Kinds + k], totals[pair.Item2 * Resources.Kinds + k]) / 2 / Resources.Scale;
        }
        if (seen.Count > 0) sink.SetState(TradeLedger.Key, TradeLedger.Version, TradeLedger.Encode(ledger));
    }
}
