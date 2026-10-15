using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One shared peoples-table load for the history operators. Returns the tables, or null (and
/// counts one <see cref="HistoryCatalog.PeoplesFallbacks"/>) when they fail to load, so the
/// caller falls back to the built-in folk numbers in <see cref="HistoryDemography.ResolveFolk"/>
/// and the culture fallbacks beside it. The count keeps a corrupt data layer visible instead of
/// silently simulating on fallback numbers.
/// </summary>
internal static class PeoplesLoader
{
    /// <summary>Loads the peoples tables, counting a fallback on <paramref name="history"/> when they fail.</summary>
    public static PeoplesStore? LoadStore(HistoryCatalog history)
    {
        try
        {
            return PeoplesStore.Load();
        }
        catch (PeoplesDataException)
        {
            history.PeoplesFallbacks++;
            return null;
        }
    }
}
