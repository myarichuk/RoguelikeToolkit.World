using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 76: long-life villains. Every turn, in polity order, an oppressed polity whose folk lives
/// at least <see cref="MinLifespanYears"/> may see a villain rise by deterministic draw: the
/// villain rules it for the rest of the span, so the polity's ruler generation freezes at the rise
/// turn (no succession), and an agenda bumps its pressure once. Rises are capped twice: by
/// <see cref="HistoryContext.MaxVillains"/> overall and by this stage's own per-turn emergence
/// budget. Every draw is a pure function of <c>(seed, turn, polity, purpose)</c>.
/// </summary>
public sealed class VillainStage : HistoryPluginBase
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

    /// <summary>Diagnostic name, <c>Villain</c>.</summary>
    public override string Name => "Villain";
    /// <summary><see cref="StageOrder.Villain"/> (76).</summary>
    public override int Order => StageOrder.Villain;
    /// <summary>None: eligibility comes from polity state and folk data.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True once polities have formed and no villain has risen yet.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
        => history.PolitiesFormed && history.Villains.Count == 0;

    /// <summary>Rises villains turn by turn over the whole span, up to the cap.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        PeoplesStore? store = PeoplesLoader.LoadStore(history);
        for (int turn = 1; turn < context.TurnCount; turn++)
        {
            foreach (var polity in history.Polities)
            {
                if (history.Villains.Count >= context.MaxVillains) return;
                if (polity.HasVillain || polity.FoundedTurn >= turn || polity.Pressure < RisePressure) continue;
                var folk = HistoryDemography.ResolveFolk(polity.FolkKey, store);
                if (folk.LifespanYears < MinLifespanYears) continue;
                var stream = HistoryRng.Create(history.Seed, Name, turn, polity.Id, "rise");
                if (stream.NextDouble() >= RiseChance) continue;
                string agenda = Agendas[(int)stream.NextUInt((uint)Agendas.Length)];
                if (!context.TrySpendOperatorEmergence(Name, turn)) continue;
                var villain = new VillainRecord
                {
                    Id = history.Villains.Count,
                    PolityId = polity.Id,
                    FolkKey = polity.FolkKey,
                    RisenTurn = turn,
                    Agenda = agenda,
                };
                history.Villains.Add(villain);
                polity.HasVillain = true;
                polity.RulerGeneration = HistoryDemography.SuccessionsBefore(folk.LifespanYears, context.TurnYears, turn);
                polity.Pressure = Math.Min(polity.Pressure + AgendaBump, HistoryDemography.MaxPressure);
                history.AppendEvent(HistoryEventKind.Villain, turn, polity.Id, polity.CapitalTile,
                    new[] { "villain", $"villain:{villain.Id}", $"agenda:{agenda}", $"folk:{polity.FolkKey}" });
            }
        }
    }
}
