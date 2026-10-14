using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 60: resolves wars into culture overlays. For every war the <see cref="WarStage"/> logged,
/// in log order, a sufficiently stronger attacker (see
/// <see cref="HistoryDemography.ConquestAdvantage"/>) may impose its culture on the loser: the
/// loser's <see cref="Polity.CultureOverlay"/> takes the winner's culture key and a
/// <see cref="HistoryEventKind.Conquest"/> event records the transfer for the <c>Names</c> leaf,
/// which renders the losing polity through the winning table late. Conquests cost one decision
/// from this stage's own per-turn budget. No tile changes hands: conquest is a cultural overlay,
/// so the ownership projection stays valid.
/// </summary>
public sealed class ConquestStage : HistoryPluginBase
{
    /// <summary>Chance a qualifying victory becomes an overlay (the rest stay raids).</summary>
    public const double ConquestChance = 0.5;

    /// <summary>Diagnostic name, <c>Conquest</c>.</summary>
    public override string Name => "Conquest";
    /// <summary><see cref="StageOrder.Conquest"/> (60).</summary>
    public override int Order => StageOrder.Conquest;
    /// <summary>None: wars come from the catalog event log, populations from shared demography.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True once polities have formed.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history) => history.PolitiesFormed;

    /// <summary>Resolves every logged war, oldest first.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        PeoplesStore? store = null;
        try
        {
            store = PeoplesStore.Load();
        }
        catch (PeoplesDataException)
        {
            store = null;
        }

        double[] capacities = HistoryDemography.CarryingCapacities(map.DataStore, history.Polities, history.Ownership);
        var wars = new List<HistoryEvent>();
        foreach (var entry in history.Events)
            if (entry.Kind == HistoryEventKind.War) wars.Add(entry);

        foreach (var war in wars)
        {
            int winner = war.PolityId;
            if (!TryDefender(war, out int loser)) continue;
            if ((uint)winner >= (uint)history.Polities.Count || (uint)loser >= (uint)history.Polities.Count) continue;
            if (winner == loser) continue;
            var victor = history.Polities[winner];
            var fallen = history.Polities[loser];
            if (fallen.CultureOverlay == victor.CultureKey) continue;
            var folkW = HistoryDemography.ResolveFolk(victor.FolkKey, store);
            var folkL = HistoryDemography.ResolveFolk(fallen.FolkKey, store);
            double popW = HistoryDemography.PopulationAtTurn(
                capacities[winner], HistoryDemography.AnnualToTurnRate(folkW.GrowthR, context.TurnYears), war.Turn);
            double popL = HistoryDemography.PopulationAtTurn(
                capacities[loser], HistoryDemography.AnnualToTurnRate(folkL.GrowthR, context.TurnYears), war.Turn);
            if (popW < HistoryDemography.ConquestAdvantage * popL) continue;
            var stream = HistoryRng.Create(history.Seed, Name, war.Turn, loser, "overlay");
            if (stream.NextDouble() >= ConquestChance) continue;
            if (!context.TrySpendOperatorDecision(Name, war.Turn)) continue;
            fallen.CultureOverlay = victor.CultureKey;
            history.AppendEvent(HistoryEventKind.Conquest, war.Turn, winner, fallen.CapitalTile,
                new[] { "conquest", $"winner:{winner}", $"loser:{loser}", victor.CultureKey });
        }
    }

    private static bool TryDefender(HistoryEvent war, out int defender)
    {
        defender = -1;
        foreach (string tag in war.Tags)
        {
            if (tag.StartsWith("defender:", StringComparison.Ordinal)
                && int.TryParse(tag.Substring(9), out int id))
            {
                defender = id;
                return true;
            }
        }
        return false;
    }
}
