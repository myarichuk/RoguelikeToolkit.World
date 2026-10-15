namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One non-aggression window between two polities: while <paramref name="turn"/> does not exceed
/// <see cref="UntilTurn"/>, <see cref="WarOperator"/> skips raids between the pair. Written by
/// marriage/foster treaties and by submission tributes; never by a map edit.
/// </summary>
public sealed class TreatyWindow
{
    /// <summary>First polity id of the pair.</summary>
    public int A { get; init; }
    /// <summary>Second polity id of the pair.</summary>
    public int B { get; init; }
    /// <summary>Last history turn the window covers (inclusive).</summary>
    public int UntilTurn { get; init; }
}
