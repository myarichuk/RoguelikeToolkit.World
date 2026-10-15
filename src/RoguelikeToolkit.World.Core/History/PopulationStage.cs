using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 32: grows every polity's population along its logistic curve and ticks successions.
/// Each polity starts at <see cref="HistoryDemography.InitialShare"/> of its territory's carrying
/// capacity and steps <c>p + R·p·(1 - p/K)</c> per turn, where the per-turn rate compounds the
/// folk's annual <c>GrowthR</c> over the turn length; crowding pressure follows from density.
/// Successions tick once per folk generation (<see cref="HistoryDemography.GenerationTurns"/>).
/// Fully determined by the catalog (no RNG draws, no budgets), so re-running is a no-op once
/// every polity has a population.
/// </summary>
public sealed class PopulationStage : HistoryPluginBase
{
    /// <summary>Diagnostic name, <c>Population</c>.</summary>
    public override string Name => "Population";
    /// <summary><see cref="StageOrder.Population"/> (32).</summary>
    public override int Order => StageOrder.Population;
    /// <summary>None: capacity is read through the store only when its layer is registered.</summary>
    public override Type[] Reads => Array.Empty<Type>();
    /// <summary>Land capacity (real surplus) and biomes (fallback surplus); absent layers hold population at its floor.</summary>
    public override Type[] ReadsOptional => new[] { typeof(LandCapacityInfo), typeof(BiomeInfo) };

    /// <summary>True once polities have formed and at least one still has no population.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
    {
        if (!history.PolitiesFormed) return false;
        foreach (var polity in history.Polities)
            if (polity.Population <= 0) return true;
        return false;
    }

    /// <summary>Grows populations, derives pressures and counts successions over the whole span.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        PeoplesStore? store = PeoplesLoader.LoadStore(history);

        double[] capacities = HistoryDemography.CarryingCapacities(map.DataStore, history.Polities, history.Ownership);
        int turns = context.TurnCount;
        for (int id = 0; id < history.Polities.Count; id++)
        {
            var polity = history.Polities[id];
            if (polity.Population > 0) continue;
            var folk = HistoryDemography.ResolveFolk(polity.FolkKey, store);
            double rate = HistoryDemography.AnnualToTurnRate(folk.GrowthR, context.TurnYears);
            double capacity = capacities[id];
            double population = Math.Max(capacity * HistoryDemography.InitialShare, HistoryDemography.MinPopulation);
            for (int turn = 1; turn < turns; turn++)
                population = HistoryDemography.StepPopulation(population, rate, capacity);
            polity.Population = population;
            polity.Pressure = HistoryDemography.PressureOf(population, capacity);
            polity.RulerGeneration = HistoryDemography.SuccessionsBefore(folk.LifespanYears, context.TurnYears, turns);
        }
    }
}
