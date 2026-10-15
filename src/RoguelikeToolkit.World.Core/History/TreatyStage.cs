using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 48: abstract alliance contracts. Every turn, in polity order, each bordering pair may
/// seal one treaty by deterministic draw (xenophobic pairs refuse more often): pairs with no
/// heir marry (recording a blended heir on both sides plus a claim hook), pairs with heirs
/// foster — but only once both heirs are adults, so fostering a child is refused with a grudge
/// (the adult gate, enforced abstractly; see <see cref="Actors.Actor.RequireAdult"/> for the
/// per-actor form). Success opens a non-aggression window, repays the pair's grudges and takes a
/// little pressure off both sides; refusal logs a <see cref="HistoryEventKind.Grudge"/> event and
/// raises lasting weight. Each attempt costs one decision from this stage's own per-turn budget.
/// Every draw is a pure function of <c>(seed, turn, pair, purpose)</c>.
/// </summary>
public sealed class TreatyStage : HistoryPluginBase
{
    /// <summary>Base acceptance chance before xenophobia discounts it.</summary>
    public const double TreatyBase = 0.35;

    /// <summary>Acceptance lost per point of mean pair xenophobia.</summary>
    public const double TreatyXenophobiaScale = 0.002;

    /// <summary>Floor under the acceptance chance.</summary>
    public const double MinTreatyChance = 0.05;

    /// <summary>Non-aggression window a sealed treaty opens, in turns past the treaty turn.</summary>
    public const int TreatyWindowTurns = 4;

    /// <summary>Grudge weight a refused treaty raises between the pair.</summary>
    public const int RefusalGrudge = 1;

    /// <summary>Pressure relief a sealed treaty brings both sides (peace dividend).</summary>
    public const double PeaceDividend = 0.05;

    /// <summary>Diagnostic name, <c>Treaty</c>.</summary>
    public override string Name => "Treaty";
    /// <summary><see cref="StageOrder.Treaty"/> (48).</summary>
    public override int Order => StageOrder.Treaty;
    /// <summary>None: pairs come from the catalog ownership map, adulthood from folk data.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True once at least two polities share the map.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
        => history.PolitiesFormed && history.Polities.Count >= 2;

    /// <summary>Seals or refuses treaties turn by turn over the whole span.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        PeoplesStore? store = PeoplesLoader.LoadStore(history);
        for (int turn = 1; turn < context.TurnCount; turn++)
        {
            for (int a = 0; a < history.Polities.Count; a++)
            {
                var first = history.Polities[a];
                if (first.FoundedTurn >= turn) continue;
                foreach (int b in HistoryNeighbors.Bordering(map.DataStore, history.Ownership, a))
                {
                    if (b <= a || (uint)b >= (uint)history.Polities.Count) continue;
                    var second = history.Polities[b];
                    if (second.FoundedTurn >= turn) continue;
                    if (history.IsWindowed(a, b, turn)) continue;
                    bool aHeir = first.HeirBornTurn >= 0;
                    bool bHeir = second.HeirBornTurn >= 0;
                    if (aHeir != bHeir) continue;
                    var stream = HistoryRng.Create(history.Seed, Name, turn, a * 100003 + b, "bond");
                    double chance = Math.Max(TreatyBase - (XenophobiaOf(first.CultureKey, store)
                        + XenophobiaOf(second.CultureKey, store)) / 2.0 * TreatyXenophobiaScale, MinTreatyChance);
                    bool accepted = stream.NextDouble() < chance;
                    if (!context.TrySpendOperatorDecision(Name, turn)) continue;
                    if (!aHeir) Marry(history, a, b, turn, accepted);
                    else Foster(history, store, context, a, b, turn, accepted);
                }
            }
        }
    }

    /// <summary>True when <paramref name="polity"/>'s treaty heir is an adult at <paramref name="turn"/>.</summary>
    internal static bool IsHeirAdult(Polity polity, int turn, int turnYears, int maturationYears)
        => polity.HeirBornTurn >= 0 && (turn - polity.HeirBornTurn) * turnYears >= maturationYears;

    private static void Marry(HistoryCatalog history, int a, int b, int turn, bool accepted)
    {
        var first = history.Polities[a];
        var second = history.Polities[b];
        if (!accepted)
        {
            Refuse(history, a, b, turn, "marriage");
            return;
        }
        first.HeirBornTurn = turn;
        second.HeirBornTurn = turn;
        history.RepayGrudges(a, b);
        history.AddWindow(a, b, turn + TreatyWindowTurns);
        first.Pressure = Math.Max(0, first.Pressure - PeaceDividend);
        second.Pressure = Math.Max(0, second.Pressure - PeaceDividend);
        history.AppendEvent(HistoryEventKind.Treaty, turn, a, first.CapitalTile,
            new[] { "treaty", "marriage", $"party:{a}", $"party:{b}",
                $"heir:{first.FolkKey}x{second.FolkKey}", "share:0.5", $"claim:{a}-{b}" });
    }

    private static void Foster(HistoryCatalog history, PeoplesStore? store, HistoryContext context, int a, int b, int turn, bool accepted)
    {
        var first = history.Polities[a];
        var second = history.Polities[b];
        var folkA = HistoryDemography.ResolveFolk(first.FolkKey, store);
        var folkB = HistoryDemography.ResolveFolk(second.FolkKey, store);
        if (!accepted || !IsHeirAdult(first, turn, context.TurnYears, folkA.MaturationYears)
            || !IsHeirAdult(second, turn, context.TurnYears, folkB.MaturationYears))
        {
            Refuse(history, a, b, turn, "foster");
            return;
        }
        history.RepayGrudges(a, b);
        history.AddWindow(a, b, turn + TreatyWindowTurns);
        first.Pressure = Math.Max(0, first.Pressure - PeaceDividend);
        second.Pressure = Math.Max(0, second.Pressure - PeaceDividend);
        history.AppendEvent(HistoryEventKind.Treaty, turn, a, first.CapitalTile,
            new[] { "treaty", "foster", $"party:{a}", $"party:{b}", $"claim:{a}-{b}" });
    }

    private static void Refuse(HistoryCatalog history, int a, int b, int turn, string kind)
    {
        history.AddGrudge(a, b, RefusalGrudge);
        history.AppendEvent(HistoryEventKind.Grudge, turn, a, history.Polities[a].CapitalTile,
            new[] { "grudge", $"refused:{kind}", $"against:{b}", $"weight:{history.GrudgeWeight(a, b)}" });
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
}
