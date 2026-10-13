namespace RoguelikeToolkit.World.Core.Primitives;

/// <summary>
/// Index orderings used by the flow-accumulation and routing passes. Replaces
/// <c>Array.Sort(order, comparison)</c> over n indices (one delegate call and
/// two array loads per comparison) with a single primitive sort.
/// </summary>
public static class TileOrdering
{
    /// <summary>
    /// Indices sorted by <paramref name="values"/> descending, ties broken by
    /// ascending index. Identical to the comparison
    /// <c>values[b].CompareTo(values[a]) ?? a.CompareTo(b)</c>: -0 and +0 tie,
    /// NaN sorts last.
    /// </summary>
    public static int[] DescendingByValue(ReadOnlySpan<float> values)
    {
        int n = values.Length;
        var keys = new ulong[n];
        for (int i = 0; i < n; i++)
            keys[i] = ((ulong)DescendingKey(values[i]) << 32) | (uint)i;
        Array.Sort(keys);
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = (int)(keys[i] & 0xFFFFFFFFUL);
        return order;
    }

    /// <summary>
    /// <see cref="DescendingByValue"/> with a second key: tiles of equal value are ordered by
    /// <paramref name="thenDescending"/> descending (non-negative), then by ascending index. Identical to the
    /// three-way comparison it replaces, without a delegate call per comparison: the value sort is one primitive
    /// sort, and only runs of tied values (flats, lake surfaces) get a second one.
    /// </summary>
    internal static int[] DescendingByValueThenDescending(ReadOnlySpan<float> values, ReadOnlySpan<int> thenDescending)
    {
        int n = values.Length;
        var keys = new ulong[n];
        for (int i = 0; i < n; i++)
            keys[i] = ((ulong)DescendingKey(values[i]) << 32) | (uint)i;
        Array.Sort(keys);

        for (int start = 0; start < n;)
        {
            ulong run = keys[start] >> 32;
            int end = start + 1;
            while (end < n && keys[end] >> 32 == run) end++;
            if (end - start > 1)
            {
                // Rewrite the run's keys as (depth descending, index); the run is self-contained from here on.
                for (int k = start; k < end; k++)
                {
                    int tile = (int)(keys[k] & 0xFFFFFFFFUL);
                    keys[k] = ((ulong)(uint)(int.MaxValue - thenDescending[tile]) << 32) | (uint)tile;
                }
                Array.Sort(keys, start, end - start);
            }
            start = end;
        }

        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = (int)(keys[i] & 0xFFFFFFFFUL);
        return order;
    }

    /// <summary>
    /// Heap key for a min-priority queue over floats with an index tie-break: ascending key order is ascending
    /// value order, then ascending index. Replaces a <c>(double, int)</c> tuple priority, whose default comparer
    /// goes through <c>IComparable</c> on every sift step.
    /// </summary>
    internal static ulong AscendingKey(float value, int index)
        => ((ulong)(~DescendingKey(value)) << 32) | (uint)index;

    // Ascending order of the result == descending order of the float.
    private static uint DescendingKey(float f)
    {
        if (float.IsNaN(f)) return uint.MaxValue; // last, like CompareTo's NaN-smallest under descending sort
        if (f == 0f) f = 0f; // fold -0 into +0 so they tie
        uint bits = (uint)BitConverter.SingleToInt32Bits(f);
        uint ascending = (bits & 0x80000000u) != 0 ? ~bits : bits | 0x80000000u;
        return ~ascending;
    }
}
