namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One polity's state at the end of <see cref="Turn"/>: its population and every declared scalar, in
/// <see cref="ScalarRegistry"/> id order (pressure first, plugin scalars after).
/// </summary>
public sealed class PolitySample
{
    /// <summary>Turn the sample closes (0 is the formed state before the first simulated turn).</summary>
    public int Turn { get; init; }
    /// <summary>Sampled polity id.</summary>
    public int PolityId { get; init; }
    /// <summary>Polity population at the end of the turn.</summary>
    public double Population { get; init; }
    /// <summary>Every scalar declared when the sample was taken, indexed by scalar id (a scalar declared later is absent: read its default).</summary>
    public double[] Scalars { get; init; } = Array.Empty<double>();
}
