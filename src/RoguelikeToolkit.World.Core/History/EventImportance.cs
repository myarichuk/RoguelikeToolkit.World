namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// How much an event matters to the timeline when the log hits its cap. Compaction drops
/// <see cref="Minor"/> first, then <see cref="Structural"/>, and <see cref="Founding"/> last; an event
/// that a retained event cites as its cause is never dropped while anything else can go.
/// </summary>
public enum EventImportance : byte
{
    /// <summary>Colour and diffusion (exchange, grudges, myths, route cuts): lost first.</summary>
    Minor = 0,
    /// <summary>Events that change the map or the cast (wars, conquests, treaties, rebellions...).</summary>
    Structural = 1,
    /// <summary>The timeline skeleton: foundings and fork markers.</summary>
    Founding = 2,
}
