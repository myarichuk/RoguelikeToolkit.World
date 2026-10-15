namespace RoguelikeToolkit.World.Core.History;

/// <summary>A pass over the finished event log, run once after the last turn (faith, city projects).</summary>
public interface IHistoryPostPass
{
    /// <summary>Unique name.</summary>
    string Name { get; }

    /// <summary>Runs over the finished <paramref name="history"/>.</summary>
    void Run(HistoryCatalog history, HistoryContext context);
}
