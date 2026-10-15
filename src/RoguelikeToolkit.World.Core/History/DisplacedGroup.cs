namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// People who left a settlement and have not settled anywhere yet: the pool between a release (a settlement
/// ended or shed people) and an absorption (a settlement took them in). Population is conserved through the
/// pool; the only sink is <see cref="HistoryCatalog.PopulationLost"/> when a stale group has nowhere to go.
/// </summary>
public sealed class DisplacedGroup
{
    /// <summary>Polity the people belonged to when they left.</summary>
    public int PolityId { get; init; }
    /// <summary>Culture key of that polity at the time.</summary>
    public string Culture { get; init; } = string.Empty;
    /// <summary>History turn they were displaced on.</summary>
    public int Turn { get; init; }
    /// <summary>People still unsettled, in capacity units.</summary>
    public double Amount { get; set; }
    /// <summary>Index into <see cref="HistoryCatalog.Settlements"/> they left.</summary>
    public int Origin { get; init; }
    /// <summary>Id of the event that displaced them, or -1.</summary>
    public int Cause { get; init; } = -1;
}
