namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// A bond between two persons from <see cref="FromTurn"/> until <see cref="ToTurn"/> (-1 while open). The kind is an interned
/// name (<c>parent</c>, <c>spouse</c>, <c>patron</c>, <c>rival</c>, <c>nemesis</c>, <c>mentor</c>, <c>betrayer</c>, <c>sworn</c>,
/// <c>sponsor</c>, or any name data adds). <see cref="HistoryCatalog.Relations"/> is sorted by (A, B, Kind, FromTurn).
/// </summary>
public sealed class HistoryRelation
{
    /// <summary>Person id of the first party (the patron, the parent, the betrayer...).</summary>
    public int A { get; init; }
    /// <summary>Person id of the second party.</summary>
    public int B { get; init; }
    /// <summary>Interned relation kind.</summary>
    public int Kind { get; init; }
    /// <summary>Turn the bond formed.</summary>
    public int FromTurn { get; init; }
    /// <summary>Turn the bond ended, or -1 while open.</summary>
    public int ToTurn { get; set; } = -1;
    /// <summary>Strength of the bond (signed; rivalries deepen with each clash).</summary>
    public int Weight { get; set; }
    /// <summary>True while the bond holds.</summary>
    public bool Open => ToTurn < 0;
}
