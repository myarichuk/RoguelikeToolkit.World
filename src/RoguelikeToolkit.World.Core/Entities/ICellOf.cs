namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>Maps a column value to the cell it occupies (negative = unplaced). Implement as a struct so the call inlines.</summary>
public interface ICellOf<T> where T : unmanaged
{
    /// <summary>The cell <paramref name="value"/> occupies. Negative, or at or beyond the <c>cellCount</c> given to <see cref="CellIndex"/>, means unplaced and the slot is left out of the index.</summary>
    int Cell(in T value);
}
