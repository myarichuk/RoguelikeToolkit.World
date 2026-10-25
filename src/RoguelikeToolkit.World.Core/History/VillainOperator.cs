using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 76: long-life villains. Every turn, in polity order, an oppressed polity whose folk lives
/// at least <see cref="MinLifespanYears"/> may see a villain rise by deterministic draw: the
/// villain rules it for the rest of the span, so the polity's ruler generation freezes at the rise
/// turn (no succession), and an agenda bumps its pressure once. Rises are capped twice: by
/// <see cref="HistoryContext.MaxVillains"/> overall and by this operator's own per-turn emergence
/// budget. Every draw is a pure function of <c>(seed, turn, polity, purpose)</c>.
/// </summary>
public sealed class VillainOperator : IHistoryOperator
{
    /// <summary>Minimum folk lifespan, in years, for a ruler to outlast succession.</summary>
    public const int MinLifespanYears = 200;

    /// <summary>Crowding pressure a polity needs before a villain can seize it.</summary>
    public const double RisePressure = 0.25;

    /// <summary>Per-polity, per-turn chance to rise once eligible.</summary>
    public const double RiseChance = 0.2;

    /// <summary>Pressure the villain's agenda adds at the rise turn.</summary>
    public const double AgendaBump = 0.1;

    private static readonly string[] Agendas = { "dominion", "ruin", "hunger", "shadow" };

    private PeoplesStore? _store;
    private bool _storeLoaded;

    /// <summary>Diagnostic name, <c>Villain</c>.</summary>
    public string Name => "Villain";

    /// <summary><see cref="StageOrder.Villain"/> (76).</summary>
    public int Order => StageOrder.Villain;

    /// <summary>Proposes this turn's villain rises from the snapshot, up to the cap.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!_storeLoaded)
        {
            _store = view.LoadPeoples();
            _storeLoaded = true;
        }
        int risen = 0;
        foreach (var polity in view.Polities)
        {
            if (view.VillainCount + risen >= view.MaxVillains) return;
            if (!polity.Alive || polity.HasVillain || polity.FoundedTurn >= view.Turn) continue;
            if (view.ScalarOf(polity.Id, ScalarRegistry.Pressure) < RisePressure) continue;
            var folk = HistoryDemography.ResolveFolk(polity.FolkKey, _store);
            if (folk.LifespanYears < MinLifespanYears) continue;
            var stream = sink.Rng(polity.Id, "rise");
            if (stream.NextDouble() >= RiseChance) continue;
            string agenda = Agendas[(int)stream.NextUInt((uint)Agendas.Length)];
            if (!sink.TrySpendEmergence()) continue;
            int villainId = view.VillainCount + risen++;
            var rise = new Proposal.AddVillain
            {
                EntityId = polity.Id,
                Agenda = agenda,
                RulerGeneration = HistoryDemography.SuccessionsBefore(folk.LifespanYears, view.TurnYears, view.Turn - polity.FoundedTurn),
            };
            sink.Emit(rise);
            sink.Emit(new Proposal.AdjustScalar { Requires = rise, EntityId = polity.Id, ScalarId = ScalarRegistry.Pressure, Delta = AgendaBump });
            sink.EventIfApplied(rise, HistoryEventKind.Villain, polity.Id, polity.CapitalTile,
                new[] { "villain", $"villain:{villainId}", $"agenda:{agenda}", $"folk:{polity.FolkKey}" });
        }
    }
}
