namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// An interned event kind id. Built-ins are the static members of <see cref="EventKinds"/> and equal the
/// <see cref="HistoryEventKind"/> values; plugin kinds are interned into the catalog's
/// <see cref="StringTable"/>. The enum stays during the kernel transition and converts implicitly.
/// </summary>
public readonly record struct EventKind(int Id)
{
    /// <summary>Maps a built-in enum kind to its id.</summary>
    public static implicit operator EventKind(HistoryEventKind kind) => new((byte)kind);
}
