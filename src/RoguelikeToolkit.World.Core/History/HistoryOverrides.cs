namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The user-override input layer beside <c>UserEditInfo</c>: polity renames and pinned tile
/// ownership. No stage writes it; the game (or editor) writes it directly and it survives both
/// <c>World.Recompute</c> and <c>World.RegenerateHistory</c>, re-applied after every territory
/// re-resolution. Pins reference polity ids and are best-effort: a pin naming a polity that no
/// longer exists is kept but ignored.
/// </summary>
public sealed class HistoryOverrides
{
    /// <summary>Display-name overrides by polity id.</summary>
    public Dictionary<int, string> Renames { get; } = new();
    /// <summary>Forced tile ownership by tile index (value: polity id).</summary>
    public Dictionary<int, int> PinnedTiles { get; } = new();

    /// <summary>Names <paramref name="polityId"/> <paramref name="name"/> for display. Throws <see cref="ArgumentException"/> for a blank name.</summary>
    public void RenamePolity(int polityId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Polity name must be non-blank.", nameof(name));
        Renames[polityId] = name;
    }

    /// <summary>Forgets the display name of <paramref name="polityId"/>; returns false when there was none.</summary>
    public bool ClearRename(int polityId) => Renames.Remove(polityId);

    /// <summary>Forces <paramref name="tileIndex"/> to belong to <paramref name="polityId"/> through every re-resolution.</summary>
    public void PinTile(int tileIndex, int polityId)
    {
        if (tileIndex < 0) throw new ArgumentOutOfRangeException(nameof(tileIndex), "Tile index cannot be negative.");
        if (polityId < 0) throw new ArgumentOutOfRangeException(nameof(polityId), "Polity id cannot be negative.");
        PinnedTiles[tileIndex] = polityId;
    }

    /// <summary>Releases the pin on <paramref name="tileIndex"/>; returns false when there was none.</summary>
    public bool UnpinTile(int tileIndex) => PinnedTiles.Remove(tileIndex);

    /// <summary>Clears every rename and pin.</summary>
    public void Clear()
    {
        Renames.Clear();
        PinnedTiles.Clear();
    }
}
