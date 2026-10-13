using System;

namespace RoguelikeToolkit.World.Core;

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
