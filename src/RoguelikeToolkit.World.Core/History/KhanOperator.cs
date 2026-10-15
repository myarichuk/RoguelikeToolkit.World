using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 46: steppe ultimatums. Every turn, in polity order, each horde polity may demand tribute
/// from one weaker bordering neighbor (population ratio at or above
/// <see cref="KhanAdvantage"/>): each ordered pair is pressed exactly once per simulation, and the
/// weaker side submits or resists by deterministic draw. Submission logs a
/// <see cref="HistoryEventKind.Tribute"/> event, moves pressure from the submitter to the khan,
/// opens a non-aggression window and repays the pair's grudges; resistance logs an
/// <see cref="HistoryEventKind.Ultimatum"/> event, raises a grudge and pressures both sides
/// (the resist-stronger-siege the <see cref="WarOperator"/> then reads through the grudge bonus).
/// Each ultimatum costs one decision from this operator's own per-turn budget. Every draw is a pure
/// function of <c>(seed, turn, khan, purpose)</c>.
/// </summary>
public sealed class KhanOperator : IHistoryOperator
{
    /// <summary>Population ratio at or above which a neighbor counts as weaker.</summary>
    public const double KhanAdvantage = 2.0;

    /// <summary>Per-khan, per-turn chance to press an unpressed weaker neighbor.</summary>
    public const double UltimatumChance = 0.35;

    /// <summary>Base submission chance; stronger khans are harder to refuse (see <see cref="DecideOutcome"/>).</summary>
    public const double SubmitBase = 0.4;

    /// <summary>Submission chance gained per point of population ratio above <see cref="KhanAdvantage"/>.</summary>
    public const double SubmitRatioScale = 0.2;

    /// <summary>Ceiling of the submission chance.</summary>
    public const double MaxSubmitChance = 0.9;

    /// <summary>Non-aggression window a submission opens, in turns past the tribute turn.</summary>
    public const int TributeWindowTurns = 5;

    /// <summary>Grudge weight a resisted ultimatum raises between khan and resister.</summary>
    public const int ResistGrudge = 2;

    /// <summary>Pressure relief a submission brings the tribute-payer.</summary>
    public const double TributeRelief = 0.1;

    /// <summary>Pressure a submission brings the khan (tribute income, abstract).</summary>
    public const double TributeIncome = 0.05;

    /// <summary>Diagnostic name, <c>Khan</c>.</summary>
    public string Name => "Khan";

    /// <summary><see cref="StageOrder.Khan"/> (46).</summary>
    public int Order => StageOrder.Khan;

    /// <summary>Proposes this turn's ultimatums from the snapshot (populations as of the end of the last turn).</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count < 2) return;
        int[] ownership = view.OwnershipSnapshot();
        foreach (var khanPolity in polities)
        {
            if (!khanPolity.Alive || khanPolity.Form != PolityForm.Horde) continue;
            if (khanPolity.FoundedTurn >= view.Turn) continue;
            int khan = khanPolity.Id;
            var pressed = PressedBy(view, khan);
            foreach (int weak in HistoryNeighbors.Bordering(view.Map.DataStore, ownership, khan))
            {
                if (pressed.Contains(weak)) continue;
                if ((uint)weak >= (uint)polities.Count) continue;
                var weakPolity = polities[weak];
                if (!weakPolity.Alive || weakPolity.FoundedTurn >= view.Turn) continue;
                double ratio = weakPolity.Population <= 0 ? double.PositiveInfinity : khanPolity.Population / weakPolity.Population;
                if (ratio < KhanAdvantage) continue;
                var stream = sink.Rng(khan, "ultimatum");
                if (stream.NextDouble() >= UltimatumChance) continue;
                if (!sink.TrySpendDecision()) continue;
                if (DecideOutcome(stream.NextDouble(), ratio))
                    Submit(sink, khan, weak, weakPolity.CapitalTile, view.Turn);
                else
                    Resist(sink, khan, weak, weakPolity.CapitalTile);
                break;
            }
        }
    }

    /// <summary>Weaker polities this khan has already pressed (a tribute or an ultimatum in the log); each ordered pair is pressed once.</summary>
    private static HashSet<int> PressedBy(TurnView view, int khan)
    {
        var pressed = new HashSet<int>();
        foreach (var e in view.EventsOf(khan))
        {
            string prefix = e.Kind == HistoryEventKind.Tribute ? "from:" : e.Kind == HistoryEventKind.Ultimatum ? "weak:" : string.Empty;
            if (prefix.Length == 0) continue;
            foreach (string tag in e.Tags)
            {
                if (tag.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(tag.AsSpan(prefix.Length), out int id))
                    pressed.Add(id);
            }
        }
        return pressed;
    }

    /// <summary>True when a <paramref name="submitRoll"/> below the ratio-scaled submission chance submits.</summary>
    /// <param name="submitRoll">Deterministic draw in [0, 1).</param>
    /// <param name="ratio">Khan-to-weaker population ratio, at or above <see cref="KhanAdvantage"/>.</param>
    internal static bool DecideOutcome(double submitRoll, double ratio)
        => submitRoll < Math.Min(SubmitBase + SubmitRatioScale * (ratio - 1), MaxSubmitChance);

    private static void Submit(ProposalSink sink, int khan, int weak, int weakCapital, int turn)
    {
        sink.Emit(new Proposal.RepayGrudge { EntityId = khan, Other = weak });
        sink.Emit(new Proposal.AddWindow { EntityId = khan, Other = weak, UntilTurn = turn + TributeWindowTurns });
        sink.Adjust(weak, ScalarRegistry.Pressure, -TributeRelief);
        sink.Adjust(khan, ScalarRegistry.Pressure, TributeIncome);
        sink.Event(HistoryEventKind.Tribute, khan, weakCapital, new[] { "tribute", $"from:{weak}", $"to:{khan}" });
    }

    private static void Resist(ProposalSink sink, int khan, int weak, int weakCapital)
    {
        sink.Emit(new Proposal.AddGrudge { EntityId = khan, Other = weak, Weight = ResistGrudge });
        sink.Adjust(weak, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
        sink.Adjust(khan, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
        sink.Event(HistoryEventKind.Ultimatum, khan, weakCapital, new[] { "ultimatum", "resist", $"khan:{khan}", $"weak:{weak}" });
    }
}
