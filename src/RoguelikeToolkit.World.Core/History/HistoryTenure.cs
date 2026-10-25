namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// A person holding an office of a polity from <see cref="FromTurn"/> until <see cref="ToTurn"/> (-1 while held). The office is an
/// interned name (<c>ruler</c>, <c>chancellor</c>, <c>marshal</c>, <c>spymaster</c>, <c>treasurer</c>, <c>high_priest</c>,
/// <c>council</c>, or any name data adds); at most one tenure per (polity, office) is open.
/// </summary>
public sealed class HistoryTenure
{
    /// <summary>Person id of the holder.</summary>
    public int Person { get; init; }
    /// <summary>Polity id of the office.</summary>
    public int Polity { get; init; }
    /// <summary>Interned office name.</summary>
    public int Office { get; init; }
    /// <summary>Turn the tenure began.</summary>
    public int FromTurn { get; init; }
    /// <summary>Turn it ended, or -1 while held.</summary>
    public int ToTurn { get; set; } = -1;
    /// <summary>True while the office is held.</summary>
    public bool Open => ToTurn < 0;
}
