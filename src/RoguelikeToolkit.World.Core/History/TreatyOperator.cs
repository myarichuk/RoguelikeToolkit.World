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
/// raises lasting weight. Each attempt costs one decision from this operator's own per-turn budget.
/// Every draw is a pure function of <c>(seed, turn, pair, purpose)</c>.
/// </summary>
public sealed class TreatyOperator : IHistoryOperator
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

    private PeoplesStore? _store;
    private bool _storeLoaded;

    /// <summary>Diagnostic name, <c>Treaty</c>.</summary>
    public string Name => "Treaty";

    /// <summary><see cref="StageOrder.Treaty"/> (48).</summary>
    public int Order => StageOrder.Treaty;

    /// <summary>Proposes this turn's treaties and refusals from the snapshot.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count < 2) return;
        if (!_storeLoaded)
        {
            _store = view.LoadPeoples();
            _storeLoaded = true;
        }
        int[] ownership = view.OwnershipSnapshot();
        var married = new HashSet<int>();
        foreach (var first in polities)
        {
            if (!first.Alive || first.FoundedTurn >= view.Turn) continue;
            int a = first.Id;
            foreach (int b in HistoryNeighbors.Bordering(view.Map.DataStore, ownership, a))
            {
                if (b <= a || (uint)b >= (uint)polities.Count) continue;
                var second = polities[b];
                if (!second.Alive || second.FoundedTurn >= view.Turn) continue;
                if (view.IsWindowed(a, b)) continue;
                bool aHeir = first.HeirBornTurn >= 0 || married.Contains(a);
                bool bHeir = second.HeirBornTurn >= 0 || married.Contains(b);
                if (aHeir != bHeir) continue;
                var stream = sink.Rng(a * 100003 + b, "bond");
                double chance = Math.Max(TreatyBase - (XenophobiaOf(first.CultureKey, _store)
                    + XenophobiaOf(second.CultureKey, _store)) / 2.0 * TreatyXenophobiaScale, MinTreatyChance);
                bool accepted = stream.NextDouble() < chance;
                if (!sink.TrySpendDecision()) continue;
                if (!aHeir)
                {
                    if (accepted)
                    {
                        married.Add(a);
                        married.Add(b);
                        Marry(view, sink, first, second);
                    }
                    else Refuse(view, sink, first, second, "marriage");
                }
                else Foster(view, sink, first, second, accepted);
            }
        }
    }

    /// <summary>True when a treaty heir born on <paramref name="heirBornTurn"/> is an adult at <paramref name="turn"/>.</summary>
    internal static bool IsHeirAdult(int heirBornTurn, int turn, int turnYears, int maturationYears)
        => heirBornTurn >= 0 && (turn - heirBornTurn) * turnYears >= maturationYears;

    private static void Marry(TurnView view, ProposalSink sink, TurnView.PolityState first, TurnView.PolityState second)
    {
        int a = first.Id, b = second.Id;
        sink.Emit(new Proposal.SetHeir { EntityId = a, HeirBornTurn = view.Turn });
        sink.Emit(new Proposal.SetHeir { EntityId = b, HeirBornTurn = view.Turn });
        Seal(view, sink, a, b);
        sink.Event(HistoryEventKind.Treaty, a, first.CapitalTile,
            new[] { "treaty", "marriage", $"party:{a}", $"party:{b}",
                $"heir:{first.FolkKey}x{second.FolkKey}", "share:0.5", $"claim:{a}-{b}" });
    }

    private void Foster(TurnView view, ProposalSink sink, TurnView.PolityState first, TurnView.PolityState second, bool accepted)
    {
        var folkA = HistoryDemography.ResolveFolk(first.FolkKey, _store);
        var folkB = HistoryDemography.ResolveFolk(second.FolkKey, _store);
        if (!accepted || !IsHeirAdult(first.HeirBornTurn, view.Turn, view.TurnYears, folkA.MaturationYears)
            || !IsHeirAdult(second.HeirBornTurn, view.Turn, view.TurnYears, folkB.MaturationYears))
        {
            Refuse(view, sink, first, second, "foster");
            return;
        }
        int a = first.Id, b = second.Id;
        Seal(view, sink, a, b);
        sink.Event(HistoryEventKind.Treaty, a, first.CapitalTile,
            new[] { "treaty", "foster", $"party:{a}", $"party:{b}", $"claim:{a}-{b}" });
    }

    private static void Seal(TurnView view, ProposalSink sink, int a, int b)
    {
        sink.Emit(new Proposal.RepayGrudge { EntityId = a, Other = b });
        sink.Emit(new Proposal.AddWindow { EntityId = a, Other = b, UntilTurn = view.Turn + TreatyWindowTurns });
        sink.Adjust(a, ScalarRegistry.Pressure, -PeaceDividend);
        sink.Adjust(b, ScalarRegistry.Pressure, -PeaceDividend);
    }

    private static void Refuse(TurnView view, ProposalSink sink, TurnView.PolityState first, TurnView.PolityState second, string kind)
    {
        int a = first.Id, b = second.Id;
        sink.Emit(new Proposal.AddGrudge { EntityId = a, Other = b, Weight = RefusalGrudge });
        sink.Event(HistoryEventKind.Grudge, a, first.CapitalTile,
            new[] { "grudge", $"refused:{kind}", $"against:{b}", $"weight:{view.GrudgeWeight(a, b) + RefusalGrudge}" });
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
