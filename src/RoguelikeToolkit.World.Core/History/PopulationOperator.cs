using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 32: grows every living polity's population one logistic step per turn
/// (<c>p + R·p·(1 - p/K)</c>, with <c>R</c> the folk's annual <c>GrowthR</c> compounded over the turn
/// and <c>K</c> the carrying capacity of the tiles it owns at the start of the turn), moves crowding
/// pressure by the change in crowding, and ticks ruler successions once per folk generation
/// (<see cref="HistoryDemography.GenerationTurns"/>). A polity with no population yet is seeded at
/// <see cref="HistoryDemography.InitialShare"/> of its capacity and steps in the same turn. No RNG,
/// no budgets.
/// </summary>
public sealed class PopulationOperator : IHistoryOperator
{
    private PeoplesStore? _store;
    private bool _storeLoaded;

    /// <summary>Diagnostic name, <c>Population</c>.</summary>
    public string Name => "Population";

    /// <summary><see cref="StageOrder.Population"/> (32).</summary>
    public int Order => StageOrder.Population;

    /// <summary>Steps each living polity's population and derives pressure and succession count.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count == 0) return;
        if (!_storeLoaded)
        {
            _store = view.LoadPeoples();
            _storeLoaded = true;
        }

        double[] capacities = HistoryDemography.CarryingCapacities(view.Map.DataStore, polities.Count, view.OwnershipSnapshot());
        foreach (var polity in polities)
        {
            if (!polity.Alive) continue;
            var folk = HistoryDemography.ResolveFolk(polity.FolkKey, _store);
            double rate = HistoryDemography.AnnualToTurnRate(folk.GrowthR, view.TurnYears);
            double capacity = capacities[polity.Id];
            double before = polity.Population > 0
                ? polity.Population
                : Math.Max(capacity * HistoryDemography.InitialShare, HistoryDemography.MinPopulation);
            double after = HistoryDemography.StepPopulation(before, rate, capacity);
            sink.Emit(new Proposal.SetPopulation
            {
                EntityId = polity.Id,
                Population = after,
                RulerGeneration = HistoryDemography.SuccessionsBefore(folk.LifespanYears, view.TurnYears, view.Turn + 1),
            });
            double pressureDelta = HistoryDemography.PressureOf(after, capacity) - HistoryDemography.PressureOf(before, capacity);
            if (pressureDelta != 0) sink.Adjust(polity.Id, ScalarRegistry.Pressure, pressureDelta);
        }
    }
}
