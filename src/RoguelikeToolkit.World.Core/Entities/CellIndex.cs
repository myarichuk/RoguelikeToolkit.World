namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>Maps a column value to the cell it occupies (negative = unplaced). Implement as a struct so the call inlines.</summary>
public interface ICellOf<T> where T : unmanaged
{
    int Cell(in T value);
}

/// <summary>
/// Counting-sort spatial bucket index: maps a cell (typically a planet tile index) to the
/// entity slots inside it as one contiguous span. Rebuilt in O(entities + cells) from a
/// per-slot cell array and allocation-free once warmed up, so rebuilding it every tick is
/// cheaper than maintaining per-cell lists.
/// </summary>
public sealed class CellIndex
{
    private int[] _offsets = Array.Empty<int>();
    private int[] _cursors = Array.Empty<int>();
    private int[] _items = Array.Empty<int>();
    private int _cellCount;

    public int CellCount => _cellCount;

    /// <summary>Slots currently indexed.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Rebuilds the index. <paramref name="cellBySlot"/>[slot] is the cell for that slot;
    /// negative or out-of-range values (dead or unplaced entities) are skipped.
    /// </summary>
    public void Rebuild(int cellCount, ReadOnlySpan<int> cellBySlot)
    {
        if (cellCount < 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
        PrepareOffsets(cellCount);

        int placed = 0;
        for (int s = 0; s < cellBySlot.Length; s++)
        {
            int cell = cellBySlot[s];
            if ((uint)cell >= (uint)cellCount) continue;
            _offsets[cell + 1]++;
            placed++;
        }
        PrefixSum(cellCount, placed);
        for (int s = 0; s < cellBySlot.Length; s++)
        {
            int cell = cellBySlot[s];
            if ((uint)cell >= (uint)cellCount) continue;
            _items[_cursors[cell]++] = s;
        }
        Count = placed;
    }

    /// <summary>
    /// Rebuilds straight from a chunked column: dead slots are skipped and each live value is mapped
    /// to a cell by <paramref name="selector"/>. No intermediate array, no allocation once warm.
    /// </summary>
    public void Rebuild<T, TSel>(int cellCount, EntityStore store, EntityStore.Column<T> column, TSel selector)
        where T : unmanaged where TSel : struct, ICellOf<T>
    {
        if (cellCount < 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
        PrepareOffsets(cellCount);
        int placed = 0;
        for (int k = 0; k < column.ChunkCount; k++)
        {
            var span = column.Chunk(k);
            int baseSlot = k << EntityStore.ChunkShift;
            for (int i = 0; i < span.Length; i++)
            {
                if (!store.IsAliveSlot(baseSlot + i)) continue;
                int cell = selector.Cell(in span[i]);
                if ((uint)cell >= (uint)cellCount) continue;
                _offsets[cell + 1]++;
                placed++;
            }
        }
        PrefixSum(cellCount, placed);
        for (int k = 0; k < column.ChunkCount; k++)
        {
            var span = column.Chunk(k);
            int baseSlot = k << EntityStore.ChunkShift;
            for (int i = 0; i < span.Length; i++)
            {
                if (!store.IsAliveSlot(baseSlot + i)) continue;
                int cell = selector.Cell(in span[i]);
                if ((uint)cell >= (uint)cellCount) continue;
                _items[_cursors[cell]++] = baseSlot + i;
            }
        }
        Count = placed;
    }

    private void PrepareOffsets(int cellCount)
    {
        if (_offsets.Length < cellCount + 1)
        {
            _offsets = new int[cellCount + 1];
            _cursors = new int[cellCount + 1];
        }
        else
        {
            Array.Clear(_offsets, 0, cellCount + 1);
        }
        _cellCount = cellCount;
    }

    private void PrefixSum(int cellCount, int placed)
    {
        for (int c = 0; c < cellCount; c++) _offsets[c + 1] += _offsets[c];
        if (_items.Length < placed) _items = new int[Math.Max(placed, _items.Length * 2)];
        Array.Copy(_offsets, _cursors, cellCount);
    }

    /// <summary>Entity slots in a cell, in ascending slot order (deterministic).</summary>
    public ReadOnlySpan<int> InCell(int cell)
    {
        if ((uint)cell >= (uint)_cellCount) return default;
        return _items.AsSpan(_offsets[cell], _offsets[cell + 1] - _offsets[cell]);
    }
}
