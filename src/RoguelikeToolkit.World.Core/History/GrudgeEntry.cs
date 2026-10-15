namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One lasting grievance between two polities: non-decaying casus belli until repaid.
/// Canonical order (<see cref="A"/> is the smaller id) so each pair holds at most one entry.
/// Repaid (cleared) by a later treaty, tribute or culture overlay between the pair.
/// </summary>
public sealed class GrudgeEntry
{
    /// <summary>Smaller polity id of the pair.</summary>
    public int A { get; init; }
    /// <summary>Larger polity id of the pair.</summary>
    public int B { get; init; }
    /// <summary>Accumulated weight; higher weights push war chance up (see <see cref="WarOperator"/>).</summary>
    public int Weight { get; set; }
    /// <summary>Turn the grievance was first raised, or -1 when unknown. Not persisted before schema v4.</summary>
    public int FirstTurn { get; set; } = -1;
    /// <summary>Turn weight was last added, or -1 when unknown. Not persisted before schema v4.</summary>
    public int LastTurn { get; set; } = -1;
}
