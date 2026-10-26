using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 26: seeds the world's peoples roster from <see cref="HistoryContext.Peoples"/> into the
/// catalog and syncs the event budgets. Reads no tile layer (cultures are data, not geography);
/// once seeded, re-running is a no-op so only <see cref="HistoryCatalog.Reset"/> re-arms it.
/// </summary>
public sealed class CultureSeedStage : HistoryPluginBase
{
    /// <summary>Diagnostic name, <c>CultureSeed</c>.</summary>
    public override string Name => "CultureSeed";
    /// <summary><see cref="StageOrder.CultureSeed"/> (26).</summary>
    public override int Order => StageOrder.CultureSeed;
    /// <summary>None: the roster comes from the attached <see cref="HistoryContext"/>.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True when the catalog still needs its roster.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history) => !history.CultureSeeded;

    /// <summary>Copies the configured roster (blank keys rejected, exact duplicates folded) and syncs budgets.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        history.SyncBudgets(context.MaxEventsPerTurn, context.MaxEvents, context.MaxDetailEventsPerTurn);
        var seen = new HashSet<(string, string)>();
        foreach (var people in context.Peoples)
        {
            if (people is null) continue;
            string folk = (people.FolkKey ?? string.Empty).Trim();
            string culture = (people.CultureKey ?? string.Empty).Trim();
            if (folk.Length == 0) throw new InvalidDataException("History peoples roster has a blank folk key.");
            if (culture.Length == 0) throw new InvalidDataException("History peoples roster has a blank culture key.");
            var key = (folk.ToLowerInvariant(), culture.ToLowerInvariant());
            if (!seen.Add(key)) continue;
            history.Seeds.Add(new HistoryPeople { FolkKey = folk, CultureKey = culture });
        }
        history.CultureSeeded = true;
    }
}
