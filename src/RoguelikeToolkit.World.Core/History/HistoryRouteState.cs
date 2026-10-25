namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The living state of one trade route (the road segments sharing <see cref="RouteA"/>/<see cref="RouteB"/>): how safe and busy
/// it is, what guards it and who preys on it. <see cref="HistoryCatalog.RouteStates"/> is sorted by (RouteA, RouteB).
/// </summary>
public sealed class HistoryRouteState
{
    /// <summary>Safety a route starts at when its state is first written: neither patrolled nor preyed on.</summary>
    public const double NewRouteSafety = 0.5;

    /// <summary>Route endpoint: the smaller capital tile.</summary>
    public int RouteA { get; init; }
    /// <summary>Route endpoint: the larger capital tile.</summary>
    public int RouteB { get; init; }
    /// <summary>How safe travel is, 0..1.</summary>
    public double Safety { get; set; }
    /// <summary>How busy the route is, 0..1.</summary>
    public double Traffic { get; set; }
    /// <summary>Forts guarding the route.</summary>
    public int Forts { get; set; }
    /// <summary>Waystations along the route.</summary>
    public int Waystations { get; set; }
    /// <summary>Person id of the outlaw band working the route, or -1.</summary>
    public int OutlawBand { get; set; } = -1;
    /// <summary>Polity id secretly sponsoring that band, or -1.</summary>
    public int Sponsor { get; set; } = -1;
    /// <summary>Turn the route was last sabotaged, or -1.</summary>
    public int SabotagedTurn { get; set; } = -1;
    /// <summary>Turn of the last raid or incident, or -1.</summary>
    public int LastIncidentTurn { get; set; } = -1;
}
