namespace RoguelikeToolkit.World.Core.History;

/// <summary>Default <see cref="EventImportance"/> per built-in kind, and which kinds are detail.</summary>
public static class EventImportanceMap
{
    /// <summary>
    /// True for the expansion's detail kinds (<see cref="HistoryEventKind.Siege"/> and later). They draw on
    /// <see cref="HistoryContext.MaxDetailEventsPerTurn"/>, not the per-turn budget, and compact before every legacy event,
    /// so turning a feature on never costs the legacy log an event.
    /// </summary>
    public static bool IsDetail(EventKind kind) => kind.Id >= (int)HistoryEventKind.Siege && kind.Id <= (int)HistoryEventKind.Prophecy;

    /// <summary>Importance an event of <paramref name="kind"/> gets unless the appender says otherwise.</summary>
    public static EventImportance Of(EventKind kind) => kind.Id switch
    {
        (int)HistoryEventKind.Founding or (int)HistoryEventKind.Fork => EventImportance.Founding,
        (int)HistoryEventKind.Exchange or (int)HistoryEventKind.Grudge
            or (int)HistoryEventKind.Myth or (int)HistoryEventKind.RoadCut => EventImportance.Minor,
        (int)HistoryEventKind.Siege => EventImportance.Structural,
        >= (int)HistoryEventKind.PersonRose and <= (int)HistoryEventKind.Prophecy => EventImportance.Minor,
        _ => EventImportance.Structural,
    };
}
