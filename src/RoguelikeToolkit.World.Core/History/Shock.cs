namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// A harm event, resolved by <see cref="ShockResolver"/> against the settlements in its footprint. Anything can build one
/// (a hazard operator, a war, a plugin rule); the resolver decides who is struck and what is lost.
/// </summary>
public sealed class Shock
{
    /// <summary>Interned-style kind key (<c>earthquake</c>, <c>plague</c>, <c>flood</c>...); must be non-blank.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Strength in [0, 1]; scaled per settlement by role and tier.</summary>
    public double Severity { get; init; }

    /// <summary>Which settlements are in reach.</summary>
    public ShockFootprint Footprint { get; init; }

    /// <summary>Epicentre tile (<see cref="ShockFootprint.Tile"/> and <see cref="ShockFootprint.Radius"/>).</summary>
    public int Tile { get; init; } = -1;

    /// <summary>Reach in degrees of arc for <see cref="ShockFootprint.Radius"/>.</summary>
    public double RadiusDegrees { get; init; }

    /// <summary>Target polity for <see cref="ShockFootprint.Polity"/>.</summary>
    public int PolityId { get; init; } = -1;

    /// <summary>Share of a struck settlement's people who die at full intensity (plague is high, quake moderate).</summary>
    public double Casualty { get; init; } = 0.2;

    /// <summary>Share of a struck settlement's people who flee into the displaced pool at full intensity.</summary>
    public double Displacement { get; init; } = 0.4;

    /// <summary>Extra tags carried on the shock event (<c>magical</c>, <c>divine</c>...).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
