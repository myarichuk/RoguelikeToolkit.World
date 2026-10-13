using System.Runtime.InteropServices;

namespace RoguelikeToolkit.World.Core.Entities;

public sealed partial class EntityStore
{
    private const uint SnapshotMagic = 0x32544E45u; // "ENT2"
    private const int SnapshotVersion = 4;
    private const uint DeltaMagic = 0x44544E45u; // "ENTD"
    private const int DeltaVersion = 2;

    // Change-tracking checkpoint: per-chunk hashes as of the last Save/Load/SaveDelta/LoadDelta.
    private bool _hasCheckpoint;
    private ulong _chainId;
    private ulong _sequence;
    private ulong[] _genBaseline = Array.Empty<ulong>();

    private int UsedChunks => (_highWater + ChunkSize - 1) >> ChunkShift;

    private static int UsedInChunk(int chunk, int highWater) => Math.Min(ChunkSize, highWater - (chunk << ChunkShift));

    /// <summary>
    /// Writes a snapshot: header, generations, free list, then each column's raw bytes
    /// (type hash and stride are recorded so a mismatched column set fails loudly on load).
    /// Little-endian only.
    /// </summary>
    public void Save(Stream stream)
    {
        // A full save starts a fresh delta chain; deltas written later apply only to this snapshot.
#if NET6_0_OR_GREATER
        _chainId = (ulong)Random.Shared.NextInt64() | 1ul;
#else
        _chainId = (ulong)RandomCompat.NextInt64() | 1ul;
#endif
        _sequence = 0;
        Span<byte> head = stackalloc byte[40];
        BitConverter.TryWriteBytes(head.Slice(0, 4), SnapshotMagic);
        BitConverter.TryWriteBytes(head.Slice(4, 4), SnapshotVersion);
        BitConverter.TryWriteBytes(head.Slice(8, 4), _highWater);
        BitConverter.TryWriteBytes(head.Slice(12, 4), _freeCount);
        BitConverter.TryWriteBytes(head.Slice(16, 4), _alive);
        BitConverter.TryWriteBytes(head.Slice(20, 4), _columns.Count);
        BitConverter.TryWriteBytes(head.Slice(24, 8), _chainId);
        BitConverter.TryWriteBytes(head.Slice(32, 8), _sequence);
        stream.Write(head);
        for (int k = 0; k < UsedChunks; k++)
            stream.Write(MemoryMarshal.AsBytes(_generations[k].AsSpan(0, UsedInChunk(k, _highWater))));
        stream.Write(MemoryMarshal.AsBytes(_free.AsSpan(0, _freeCount)));
        Span<byte> ch = stackalloc byte[12];
        foreach (var c in _columns)
        {
            BitConverter.TryWriteBytes(ch.Slice(0, 8), c.TypeHash);
            BitConverter.TryWriteBytes(ch.Slice(8, 4), c.Stride);
            stream.Write(ch);
            for (int k = 0; k < UsedChunks; k++) c.WriteChunk(stream, k, UsedInChunk(k, _highWater));
        }
        Checkpoint();
    }

    /// <summary>
    /// Replaces the contents with a snapshot. The same columns must already be registered
    /// (in the same order); a mismatch throws <see cref="InvalidDataException"/> and leaves the store unchanged.
    /// </summary>
    public void Load(Stream stream)
    {
        Span<byte> head = stackalloc byte[40];
        stream.ReadExactly(head);
        if (BitConverter.ToUInt32(head.Slice(0, 4)) != SnapshotMagic || BitConverter.ToInt32(head.Slice(4, 4)) != SnapshotVersion)
            throw new InvalidDataException("Not an entity snapshot, or an unsupported version.");
        int highWater = BitConverter.ToInt32(head.Slice(8, 4));
        int freeCount = BitConverter.ToInt32(head.Slice(12, 4));
        int alive = BitConverter.ToInt32(head.Slice(16, 4));
        int columnCount = BitConverter.ToInt32(head.Slice(20, 4));
        ulong chainId = BitConverter.ToUInt64(head.Slice(24, 8));
        ulong sequence = BitConverter.ToUInt64(head.Slice(32, 8));
        if (highWater < 0 || freeCount < 0 || freeCount > highWater || alive < 0 || alive > highWater)
            throw new InvalidDataException("Corrupt entity snapshot header.");
        if (columnCount != _columns.Count)
            throw new InvalidDataException($"Snapshot has {columnCount} columns but {_columns.Count} are registered.");

        // Read into temporaries first so a bad snapshot cannot half-overwrite the store.
        int chunks = (highWater + ChunkSize - 1) >> ChunkShift;
        var gens = new List<uint[]>(chunks);
        for (int k = 0; k < chunks; k++)
        {
            var g = new uint[ChunkSize];
            stream.ReadExactly(MemoryMarshal.AsBytes(g.AsSpan(0, UsedInChunk(k, highWater))));
            gens.Add(g);
        }
        var free = new int[Math.Max(freeCount, 256)];
        stream.ReadExactly(MemoryMarshal.AsBytes(free.AsSpan(0, freeCount)));
        var staged = new Array[_columns.Count];
        Span<byte> ch = stackalloc byte[12];
        for (int i = 0; i < _columns.Count; i++)
        {
            var c = _columns[i];
            stream.ReadExactly(ch);
            if (BitConverter.ToUInt64(ch.Slice(0, 8)) != c.TypeHash || BitConverter.ToInt32(ch.Slice(8, 4)) != c.Stride)
                throw new InvalidDataException($"Snapshot column {i} does not match registered column {c.Name}.");
            staged[i] = c.ReadChunks(stream, chunks, highWater);
        }

        _generations.Clear();
        _generations.AddRange(gens);
        _free = free;
        _freeCount = freeCount;
        _highWater = highWater;
        _alive = alive;
        for (int i = 0; i < _columns.Count; i++) _columns[i].Adopt(staged[i]);
        _chainId = chainId;
        _sequence = sequence;
        Checkpoint();
    }

    /// <summary>
    /// True once a <see cref="Save"/> or <see cref="Load"/> has established a baseline, which
    /// <see cref="SaveDelta"/> needs.
    /// </summary>
    public bool CanSaveDelta => _hasCheckpoint;

    /// <summary>
    /// Writes only what changed since the last <see cref="Save"/>, <see cref="Load"/>, <see cref="SaveDelta"/>
    /// or <see cref="LoadDelta"/>: the free list plus every generation and column chunk whose bytes differ
    /// (a new chunk always counts). Writes go through spans and refs, so changes are found by hashing each
    /// chunk, not by hooks; you never mark anything dirty. Cost is one pass over memory (about a millisecond
    /// for 300k entities) and output proportional to the chunks touched. A delta applies only to the
    /// chain it was written from: restore with <see cref="Load"/> on the full snapshot, then
    /// <see cref="LoadDelta"/> each delta in order.
    /// </summary>
    public void SaveDelta(Stream stream)
    {
        if (!_hasCheckpoint) throw new InvalidOperationException("Call Save or Load first: a delta needs a baseline.");
        int chunks = UsedChunks;
        var genHash = HashGenerations(chunks);
        var genDirty = DirtyChunks(_genBaseline, genHash);
        var colHash = new ulong[_columns.Count][];
        var colDirty = new int[_columns.Count][];
        for (int i = 0; i < _columns.Count; i++)
        {
            colHash[i] = _columns[i].HashChunks(chunks, _highWater);
            colDirty[i] = DirtyChunks(_columns[i].Baseline, colHash[i]);
        }

        ulong next = _sequence + 1;
        Span<byte> head = stackalloc byte[48];
        BitConverter.TryWriteBytes(head.Slice(0, 4), DeltaMagic);
        BitConverter.TryWriteBytes(head.Slice(4, 4), DeltaVersion);
        BitConverter.TryWriteBytes(head.Slice(8, 8), _chainId);
        BitConverter.TryWriteBytes(head.Slice(16, 8), _sequence);
        BitConverter.TryWriteBytes(head.Slice(24, 8), next);
        BitConverter.TryWriteBytes(head.Slice(32, 4), _highWater);
        BitConverter.TryWriteBytes(head.Slice(36, 4), _freeCount);
        BitConverter.TryWriteBytes(head.Slice(40, 4), _alive);
        BitConverter.TryWriteBytes(head.Slice(44, 4), _columns.Count);
        stream.Write(head);
        stream.Write(MemoryMarshal.AsBytes(_free.AsSpan(0, _freeCount)));

        WriteInt(stream, genDirty.Length);
        foreach (int k in genDirty)
        {
            WriteInt(stream, k);
            stream.Write(MemoryMarshal.AsBytes(_generations[k].AsSpan(0, UsedInChunk(k, _highWater))));
        }
        Span<byte> ch = stackalloc byte[12];
        for (int i = 0; i < _columns.Count; i++)
        {
            var c = _columns[i];
            BitConverter.TryWriteBytes(ch.Slice(0, 8), c.TypeHash);
            BitConverter.TryWriteBytes(ch.Slice(8, 4), c.Stride);
            stream.Write(ch);
            WriteInt(stream, colDirty[i].Length);
            foreach (int k in colDirty[i])
            {
                WriteInt(stream, k);
                c.WriteChunk(stream, k, UsedInChunk(k, _highWater));
            }
        }

        _genBaseline = genHash;
        for (int i = 0; i < _columns.Count; i++) _columns[i].Baseline = colHash[i];
        _sequence = next;
    }

    /// <summary>
    /// Applies the next delta in the chain (see <see cref="SaveDelta"/>). Throws <see cref="InvalidDataException"/>,
    /// leaving the store unchanged, when the delta belongs to another chain, is out of order or does not match the
    /// registered columns.
    /// </summary>
    public void LoadDelta(Stream stream)
    {
        if (!_hasCheckpoint) throw new InvalidOperationException("Load the full snapshot first.");
        Span<byte> head = stackalloc byte[48];
        stream.ReadExactly(head);
        if (BitConverter.ToUInt32(head.Slice(0, 4)) != DeltaMagic || BitConverter.ToInt32(head.Slice(4, 4)) != DeltaVersion)
            throw new InvalidDataException("Not an entity delta, or an unsupported version.");
        ulong chainId = BitConverter.ToUInt64(head.Slice(8, 8));
        ulong baseSeq = BitConverter.ToUInt64(head.Slice(16, 8));
        ulong newSeq = BitConverter.ToUInt64(head.Slice(24, 8));
        int highWater = BitConverter.ToInt32(head.Slice(32, 4));
        int freeCount = BitConverter.ToInt32(head.Slice(36, 4));
        int alive = BitConverter.ToInt32(head.Slice(40, 4));
        int columnCount = BitConverter.ToInt32(head.Slice(44, 4));
        if (chainId != _chainId)
            throw new InvalidDataException("Delta belongs to a different snapshot chain.");
        if (baseSeq != _sequence)
            throw new InvalidDataException($"Delta applies after step {baseSeq} but the store is at step {_sequence}.");
        if (highWater < _highWater || freeCount < 0 || freeCount > highWater || alive < 0 || alive > highWater)
            throw new InvalidDataException("Corrupt entity delta header.");
        if (columnCount != _columns.Count)
            throw new InvalidDataException($"Delta has {columnCount} columns but {_columns.Count} are registered.");

        int chunks = (highWater + ChunkSize - 1) >> ChunkShift;
        var free = new int[Math.Max(freeCount, 256)];
        stream.ReadExactly(MemoryMarshal.AsBytes(free.AsSpan(0, freeCount)));

        int genCount = ReadCount(stream, chunks);
        var genIdx = new int[genCount];
        var genData = new uint[genCount][];
        for (int j = 0; j < genCount; j++)
        {
            genIdx[j] = ReadChunkIndex(stream, chunks, j == 0 ? -1 : genIdx[j - 1]);
            genData[j] = new uint[ChunkSize];
            stream.ReadExactly(MemoryMarshal.AsBytes(genData[j].AsSpan(0, UsedInChunk(genIdx[j], highWater))));
        }
        var colIdx = new int[_columns.Count][];
        var colData = new Array[_columns.Count];
        Span<byte> ch = stackalloc byte[12];
        for (int i = 0; i < _columns.Count; i++)
        {
            var c = _columns[i];
            stream.ReadExactly(ch);
            if (BitConverter.ToUInt64(ch.Slice(0, 8)) != c.TypeHash || BitConverter.ToInt32(ch.Slice(8, 4)) != c.Stride)
                throw new InvalidDataException($"Delta column {i} does not match registered column {c.Name}.");
            int n = ReadCount(stream, chunks);
            colIdx[i] = new int[n];
            for (int j = 0; j < n; j++) colIdx[i][j] = -1;
            colData[i] = c.ReadDirty(stream, colIdx[i], chunks, highWater);
        }

        while (_generations.Count < chunks) AddChunk();
        for (int j = 0; j < genCount; j++) _generations[genIdx[j]] = genData[j];
        for (int i = 0; i < _columns.Count; i++) _columns[i].ApplyDirty(colIdx[i], colData[i]);
        _free = free;
        _freeCount = freeCount;
        _highWater = highWater;
        _alive = alive;
        Checkpoint();
        _sequence = newSeq;
    }

    private void Checkpoint()
    {
        int chunks = UsedChunks;
        _genBaseline = HashGenerations(chunks);
        foreach (var c in _columns) c.Baseline = c.HashChunks(chunks, _highWater);
        _hasCheckpoint = true;
    }

    private ulong[] HashGenerations(int chunks)
    {
        var h = new ulong[chunks];
        for (int k = 0; k < chunks; k++)
            h[k] = Hash(MemoryMarshal.AsBytes(_generations[k].AsSpan(0, UsedInChunk(k, _highWater))));
        return h;
    }

    private static int[] DirtyChunks(ulong[] baseline, ulong[] current)
    {
        var list = new List<int>();
        for (int k = 0; k < current.Length; k++)
            if (k >= baseline.Length || baseline[k] != current[k]) list.Add(k);
        return list.ToArray();
    }

    /// <summary>64-bit multiply/xor-shift hash over the bytes; the length is mixed in so a grown chunk always differs.</summary>
    internal static ulong Hash(ReadOnlySpan<byte> data)
    {
        ulong h = 0x9E3779B97F4A7C15ul ^ (ulong)data.Length;
        var words = MemoryMarshal.Cast<byte, ulong>(data);
        foreach (ulong w in words)
        {
            h = (h ^ w) * 0xFF51AFD7ED558CCDul;
            h ^= h >> 32;
        }
        for (int i = words.Length * 8; i < data.Length; i++)
            h = (h ^ data[i]) * 0x100000001B3ul;
        h ^= h >> 29;
        return h;
    }

    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BitConverter.TryWriteBytes(b, value);
        stream.Write(b);
    }

    private static int ReadInt(Stream stream)
    {
        Span<byte> b = stackalloc byte[4];
        stream.ReadExactly(b);
        return BitConverter.ToInt32(b);
    }

    private static int ReadCount(Stream stream, int chunks)
    {
        int n = ReadInt(stream);
        if ((uint)n > (uint)chunks) throw new InvalidDataException("Corrupt entity delta chunk list.");
        return n;
    }

    internal static int ReadChunkIndex(Stream stream, int chunks, int previous)
    {
        int k = ReadInt(stream);
        if (k <= previous || k >= chunks) throw new InvalidDataException("Corrupt entity delta chunk index.");
        return k;
    }
}
