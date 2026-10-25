namespace RoguelikeToolkit.World.Core.History;

/// <summary>A field of <see cref="HistoryRouteState"/> that <see cref="Proposal.SetRouteFlag"/> sets outright.</summary>
public enum RouteFlag : byte
{
    /// <summary><see cref="HistoryRouteState.OutlawBand"/>.</summary>
    OutlawBand = 0,
    /// <summary><see cref="HistoryRouteState.Sponsor"/>.</summary>
    Sponsor = 1,
    /// <summary><see cref="HistoryRouteState.SabotagedTurn"/>.</summary>
    SabotagedTurn = 2,
    /// <summary><see cref="HistoryRouteState.LastIncidentTurn"/>.</summary>
    LastIncidentTurn = 3,
}
