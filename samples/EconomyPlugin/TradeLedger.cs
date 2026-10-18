using System.Buffers.Binary;
using RoguelikeToolkit.World.Core.History;

namespace EconomyPlugin;

/// <summary>
/// Cumulative trade volume by resource for every polity pair joined by a standing road, kept in the plugin state
/// bag and rewritten by the economy operator each turn. Format v1: route count, then per route
/// <c>polityA, polityB, volume[4]</c> as little-endian int32 / float64, routes sorted by (A, B).
/// </summary>
public static class TradeLedger
{
    /// <summary>State bag key.</summary>
    public const string Key = EconomyScalars.Namespace + "/trade";
    /// <summary>Blob format version.</summary>
    public const int Version = 1;

    /// <summary>Volumes keyed by the polity pair (A &lt; B), each of length <see cref="Resources.Kinds"/>.</summary>
    public static SortedDictionary<(int A, int B), double[]> Decode(byte[] data)
    {
        var result = new SortedDictionary<(int, int), double[]>();
        if (data.Length < 4) return result;
        int count = BinaryPrimitives.ReadInt32LittleEndian(data);
        int at = 4;
        for (int i = 0; i < count; i++)
        {
            int a = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)); at += 4;
            int b = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)); at += 4;
            var volume = new double[Resources.Kinds];
            for (int k = 0; k < volume.Length; k++)
            {
                volume[k] = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at)));
                at += 8;
            }
            result[(a, b)] = volume;
        }
        return result;
    }

    /// <summary>Encodes the ledger in key order.</summary>
    public static byte[] Encode(SortedDictionary<(int A, int B), double[]> ledger)
    {
        var buffer = new byte[4 + ledger.Count * (8 + Resources.Kinds * 8)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, ledger.Count);
        int at = 4;
        foreach (var ((a, b), volume) in ledger)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(at), a); at += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(at), b); at += 4;
            foreach (double v in volume)
            {
                BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(at), BitConverter.DoubleToInt64Bits(v));
                at += 8;
            }
        }
        return buffer;
    }

    /// <summary>Reads the finished ledger from a catalog, or an empty one when absent.</summary>
    public static SortedDictionary<(int A, int B), double[]> Read(HistoryCatalog history)
        => history.PluginState.TryRead(Key, Version, out var data) == PluginStateLookup.Found
            ? Decode(data) : new SortedDictionary<(int, int), double[]>();
}
