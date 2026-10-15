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
/// (the resist-stronger-siege the <see cref="WarStage"/> then reads through the grudge bonus).
/// Each ultimatum costs one decision from this stage's own per-turn budget. Every draw is a pure
/// function of <c>(seed, turn, khan, purpose)</c>.
/// </summary>
public sealed class KhanStage : HistoryPluginBase
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
    public override string Name => "Khan";
    /// <summary><see cref="StageOrder.Khan"/> (46).</summary>
    public override int Order => StageOrder.Khan;
    /// <summary>None: neighbors come from the catalog ownership map, populations from shared demography.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True once polities have formed.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history) => history.PolitiesFormed;

    /// <summary>Presses weaker neighbors turn by turn over the whole span.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        PeoplesStore? store = PeoplesLoader.LoadStore(history);
        double[] capacities = HistoryDemography.CarryingCapacities(map.DataStore, history.Polities, history.Ownership);
        var pressed = new HashSet<(int Khan, int Weak)>();
        for (int turn = 1; turn < context.TurnCount; turn++)
        {
            for (int khan = 0; khan < history.Polities.Count; khan++)
            {
                var khanPolity = history.Polities[khan];
                if (khanPolity.Form != PolityForm.Horde) continue;
                if (khanPolity.FoundedTurn >= turn) continue;
                var folkK = HistoryDemography.ResolveFolk(khanPolity.FolkKey, store);
                double rateK = HistoryDemography.AnnualToTurnRate(folkK.GrowthR, context.TurnYears);
                double popK = HistoryDemography.PopulationAtTurn(capacities[khan], rateK, turn);
                foreach (int weak in HistoryNeighbors.Bordering(map.DataStore, history.Ownership, khan))
                {
                    if (pressed.Contains((khan, weak))) continue;
                    if ((uint)weak >= (uint)history.Polities.Count) continue;
                    var weakPolity = history.Polities[weak];
                    if (weakPolity.FoundedTurn >= turn) continue;
                    var folkW = HistoryDemography.ResolveFolk(weakPolity.FolkKey, store);
                    double rateW = HistoryDemography.AnnualToTurnRate(folkW.GrowthR, context.TurnYears);
                    double popW = HistoryDemography.PopulationAtTurn(capacities[weak], rateW, turn);
                    double ratio = popW <= 0 ? double.PositiveInfinity : popK / popW;
                    if (ratio < KhanAdvantage) continue;
                    var stream = HistoryRng.Create(history.Seed, Name, turn, khan, "ultimatum");
                    if (stream.NextDouble() >= UltimatumChance) continue;
                    if (!context.TrySpendOperatorDecision(Name, turn)) continue;
                    pressed.Add((khan, weak));
                    if (DecideOutcome(stream.NextDouble(), ratio))
                        Submit(history, khan, weak, turn);
                    else
                        Resist(history, khan, weak, turn);
                    break;
                }
            }
        }
    }

    /// <summary>True when a <paramref name="submitRoll"/> below the ratio-scaled submission chance submits.</summary>
    /// <param name="submitRoll">Deterministic draw in [0, 1).</param>
    /// <param name="ratio">Khan-to-weaker population ratio, at or above <see cref="KhanAdvantage"/>.</param>
    internal static bool DecideOutcome(double submitRoll, double ratio)
        => submitRoll < Math.Min(SubmitBase + SubmitRatioScale * (ratio - 1), MaxSubmitChance);

    private static void Submit(HistoryCatalog history, int khan, int weak, int turn)
    {
        history.RepayGrudges(khan, weak);
        history.AddWindow(khan, weak, turn + TributeWindowTurns);
        history.Polities[weak].Pressure = Math.Max(0, history.Polities[weak].Pressure - TributeRelief);
        history.Polities[khan].Pressure =
            Math.Min(history.Polities[khan].Pressure + TributeIncome, HistoryDemography.MaxPressure);
        history.AppendEvent(HistoryEventKind.Tribute, turn, khan, history.Polities[weak].CapitalTile,
            new[] { "tribute", $"from:{weak}", $"to:{khan}" });
    }

    private static void Resist(HistoryCatalog history, int khan, int weak, int turn)
    {
        history.AddGrudge(khan, weak, ResistGrudge);
        history.Polities[weak].Pressure =
            Math.Min(history.Polities[weak].Pressure + HistoryDemography.WarPressureBump, HistoryDemography.MaxPressure);
        history.Polities[khan].Pressure =
            Math.Min(history.Polities[khan].Pressure + HistoryDemography.WarPressureBump, HistoryDemography.MaxPressure);
        history.AppendEvent(HistoryEventKind.Ultimatum, turn, khan, history.Polities[weak].CapitalTile,
            new[] { "ultimatum", "resist", $"khan:{khan}", $"weak:{weak}" });
    }
}
