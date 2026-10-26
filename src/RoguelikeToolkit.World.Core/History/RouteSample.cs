namespace RoguelikeToolkit.World.Core.History;

/// <summary>A route's safety and traffic at the end of a turn, for timelines and the scrubbed map overlay.</summary>
/// <param name="Turn">Turn sampled.</param>
/// <param name="RouteA">Route endpoint: the smaller capital tile.</param>
/// <param name="RouteB">Route endpoint: the larger capital tile.</param>
/// <param name="Safety">Safety, 0..1.</param>
/// <param name="Traffic">Traffic, 0..1.</param>
public readonly record struct RouteSample(int Turn, int RouteA, int RouteB, double Safety, double Traffic);
