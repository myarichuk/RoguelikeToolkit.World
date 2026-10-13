namespace RoguelikeToolkit.World.Core;

/// <summary>Offset-grid helpers for square (size x size) region/local maps in row-major order.</summary>
public static class OffsetGrid
{
    /// <summary>Row-major index for axial (q, r), or -1 when outside the grid.</summary>
    public static int ToIndex(int q, int r, int size)
        => (uint)q >= (uint)size || (uint)r >= (uint)size ? -1 : r * size + q;

    public static (int Q, int R) FromIndex(int index, int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (index < 0 || index >= size * size) throw new IndexOutOfRangeException();
        return (index % size, index / size);
    }
}
