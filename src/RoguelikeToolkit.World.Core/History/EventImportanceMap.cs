namespace RoguelikeToolkit.World.Core.History;

/// <summary>Default <see cref="EventImportance"/> per built-in kind.</summary>
public static class EventImportanceMap
{
    /// <summary>Importance an event of <paramref name="kind"/> gets unless the appender says otherwise.</summary>
    public static EventImportance Of(EventKind kind) => kind.Id switch
    {
        (int)HistoryEventKind.Founding or (int)HistoryEventKind.Fork => EventImportance.Founding,
        (int)HistoryEventKind.Exchange or (int)HistoryEventKind.Grudge
            or (int)HistoryEventKind.Myth or (int)HistoryEventKind.RoadCut => EventImportance.Minor,
        _ => EventImportance.Structural,
    };
}
