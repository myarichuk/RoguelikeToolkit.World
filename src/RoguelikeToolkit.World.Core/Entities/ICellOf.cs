namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>Maps a column value to the cell it occupies (negative = unplaced). Implement as a struct so the call inlines.</summary>
public interface ICellOf<T> where T : unmanaged
{
    int Cell(in T value);
}
