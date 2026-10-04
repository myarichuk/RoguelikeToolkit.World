using System.Runtime.InteropServices;

namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>
/// Columnar (struct-of-arrays) entity storage for very large populations. The library
/// owns ids, slots, growth, persistence and the column plumbing; the game decides what the
/// columns mean (position, species, faction, a name seed...).
/// </summary>
/// <remarks>
/// <para>Storage is chunked: every column is a list of fixed-size chunks (<see cref="ChunkSize"/> slots).
/// Growing adds one chunk per column and never copies or moves existing data, so spawning NPCs on
/// demand (frequent additions) costs no hitch, produces no large-object-heap garbage, and never
/// invalidates a span you are iterating. Iterate with <see cref="Column{T}.ChunkCount"/> and
/// <see cref="Column{T}.Chunk"/>.</para>
/// <para>Every live entity has a value in every column (dense layout). Slot reuse is LIFO and
/// deterministic. Snapshots are raw little-endian column dumps (see <see cref="Save"/>).</para>
/// </remarks>
public sealed class EntityStore
{
    public const int ChunkShift = 10;
    public const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    private const uint SnapshotMagic = 0x32544E45u; // "ENT2"
    private const int SnapshotVersion = 3;
    private const uint DeltaMagic = 0x44544E45u; // "ENTD"
    private const int DeltaVersion = 1;

    // Process-wide dense id per column type so lookups index an array.
    private static class ColumnSlot<T> where T : unmanaged
    {
        public static readonly int Id = Interlocked.Increment(ref _nextId) - 1;
    }
    private static int _nextId;

    private readonly List<ColumnBase> _columns = new();
    private ColumnBase?[] _columnById = new ColumnBase?[8];
    private readonly List<uint[]> _generations = new();
    private int[] _free = new int[256];
    private int _freeCount;
    private int _highWater;
    private int _alive;

    // Change-tracking checkpoint: per-chunk hashes as of the last Save/Load/SaveDelta/LoadDelta.
    private bool _hasCheckpoint;
    private ulong _chainId;
    private ulong _sequence;
    private ulong[] _genBaseline = Array.Empty<ulong>();

    /// <param name="initialCapacity">Slots to pre-allocate (rounded up to whole chunks). Growth beyond it is cheap anyway.</param>
    public EntityStore(int initialCapacity = ChunkSize)
    {
        if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        Reserve(initialCapacity);
    }

    /// <summary>Live entities.</summary>
    public int Count => _alive;

    /// <summary>One past the highest slot ever used; iterate <c>0..HighWater</c> and skip dead slots.</summary>
    public int HighWater => _highWater;

    public int Capacity => _generations.Count << ChunkShift;

    /// <summary>Allocates chunks so at least <paramref name="slots"/> slots exist without further growth.</summary>
    public void Reserve(int slots)
    {
        int chunks = (int)(((long)slots + ChunkSize - 1) >> ChunkShift);
        while (_generations.Count < chunks) AddChunk();
    }

    private void AddChunk()
    {
        _generations.Add(new uint[ChunkSize]);
        foreach (var c in _columns) c.AddChunk();
    }

    /// <summary>Registers a column. Do this before creating entities (or before <see cref="Load"/>).</summary>
    public Column<T> AddColumn<T>() where T : unmanaged
    {
        int id = ColumnSlot<T>.Id;
        if (id < _columnById.Length && _columnById[id] != null)
            return (Column<T>)_columnById[id]!;
        if (_highWater != 0)
            throw new InvalidOperationException("Add all columns before creating entities.");
        if (id >= _columnById.Length) Array.Resize(ref _columnById, Math.Max(id + 1, _columnById.Length * 2));
        var column = new Column<T>(this);
        for (int i = 0; i < _generations.Count; i++) column.AddChunk();
        _columnById[id] = column;
        _columns.Add(column);
        return column;
    }

    public Column<T> GetColumn<T>() where T : unmanaged
    {
        int id = ColumnSlot<T>.Id;
        if ((uint)id >= (uint)_columnById.Length || _columnById[id] == null)
            throw new ArgumentException($"Column {typeof(T).Name} is not registered.");
        return (Column<T>)_columnById[id]!;
    }

    private ref uint GenerationAt(int slot) => ref _generations[slot >> ChunkShift][slot & ChunkMask];

    public bool IsAlive(EntityId id)
        => (uint)id.Index < (uint)_highWater && (id.Generation & 1) == 1 && GenerationAt(id.Index) == id.Generation;

    /// <summary>True when the slot currently holds a live entity.</summary>
    public bool IsAliveSlot(int slot) => (uint)slot < (uint)_highWater && (GenerationAt(slot) & 1) == 1;

    /// <summary>The live handle for a slot (use when iterating spans by index).</summary>
    public EntityId HandleOf(int slot)
    {
        if (!IsAliveSlot(slot)) throw new ArgumentException($"Slot {slot} is not alive.", nameof(slot));
        return new EntityId(slot, GenerationAt(slot));
    }

    /// <summary>Allocates an entity with every column zeroed.</summary>
    public EntityId Create()
    {
        int slot;
        if (_freeCount > 0)
        {
            slot = _free[--_freeCount];
        }
        else
        {
            if (_highWater == Capacity) AddChunk();
            slot = _highWater++;
        }
        ref uint gen = ref GenerationAt(slot);
        gen = unchecked(gen + 1);                    // even (free) -> odd (alive)
        foreach (var c in _columns) c.Clear(slot);
        _alive++;
        return new EntityId(slot, gen);
    }

    /// <summary>Destroys an entity. Returns false for a stale or unknown handle.</summary>
    public bool Destroy(EntityId id)
    {
        if (!IsAlive(id)) return false;
        GenerationAt(id.Index) = unchecked(id.Generation + 1);   // odd -> even
        if (_freeCount == _free.Length) Array.Resize(ref _free, _free.Length * 2);
        _free[_freeCount++] = id.Index;
        _alive--;
        return true;
    }

    // ------------------------------------------------------------------ persistence

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
        _chainId = (ulong)Random.Shared.NextInt64() | 1ul;
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

    // ------------------------------------------------------------------ incremental snapshots

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

    // ------------------------------------------------------------------ columns

    public abstract class ColumnBase
    {
        internal abstract ulong TypeHash { get; }
        internal abstract int Stride { get; }
        internal abstract string Name { get; }
        internal abstract void AddChunk();
        internal abstract void Clear(int slot);
        internal abstract void WriteChunk(Stream stream, int chunk, int used);
        internal abstract Array ReadChunks(Stream stream, int chunks, int highWater);
        internal abstract void Adopt(Array chunks);
        internal abstract ulong[] HashChunks(int chunks, int highWater);
        internal abstract Array ReadDirty(Stream stream, int[] indices, int chunks, int highWater);
        internal abstract void ApplyDirty(int[] indices, Array staged);
        internal ulong[] Baseline = Array.Empty<ulong>();
    }

    /// <summary>One unmanaged value per entity slot, stored in fixed-size chunks that never move.</summary>
    public sealed class Column<T> : ColumnBase where T : unmanaged
    {
        private readonly EntityStore _store;
        private readonly List<T[]> _chunks = new();

        internal Column(EntityStore store) => _store = store;

        /// <summary>Chunks that hold in-use slots (<c>ceil(HighWater / ChunkSize)</c>).</summary>
        public int ChunkCount => _store.UsedChunks;

        /// <summary>
        /// The in-use part of one chunk; its first element is slot <c>chunk * ChunkSize</c>. Dead slots hold
        /// stale data (check <see cref="IsAliveSlot"/>). Stays valid across spawns: chunks never move.
        /// </summary>
        public Span<T> Chunk(int chunk)
        {
            if ((uint)chunk >= (uint)ChunkCount) throw new ArgumentOutOfRangeException(nameof(chunk));
            return _chunks[chunk].AsSpan(0, UsedInChunk(chunk, _store._highWater));
        }

        /// <summary>Checked access by handle; throws for a stale or destroyed entity.</summary>
        public ref T this[EntityId id]
        {
            get
            {
                if (!_store.IsAlive(id)) throw new ArgumentException($"{id} is not alive.", nameof(id));
                return ref _chunks[id.Index >> ChunkShift][id.Index & ChunkMask];
            }
        }

        /// <summary>Access by slot (bounds-checked against the high-water mark, not the generation), for tight loops.</summary>
        public ref T At(int slot)
        {
            if ((uint)slot >= (uint)_store._highWater) throw new IndexOutOfRangeException();
            return ref _chunks[slot >> ChunkShift][slot & ChunkMask];
        }

        internal override ulong TypeHash => Fnv(typeof(T).FullName ?? typeof(T).Name);
        internal override unsafe int Stride => sizeof(T);
        internal override string Name => typeof(T).Name;

        internal override void AddChunk() => _chunks.Add(new T[ChunkSize]);
        internal override void Clear(int slot) => _chunks[slot >> ChunkShift][slot & ChunkMask] = default;

        internal override void WriteChunk(Stream stream, int chunk, int used)
            => stream.Write(MemoryMarshal.AsBytes(_chunks[chunk].AsSpan(0, used)));

        internal override Array ReadChunks(Stream stream, int chunks, int highWater)
        {
            var staged = new T[chunks][];
            for (int k = 0; k < chunks; k++)
            {
                staged[k] = new T[ChunkSize];
                stream.ReadExactly(MemoryMarshal.AsBytes(staged[k].AsSpan(0, UsedInChunk(k, highWater))));
            }
            return staged;
        }

        internal override void Adopt(Array chunks)
        {
            _chunks.Clear();
            _chunks.AddRange((T[][])chunks);
        }

        internal override ulong[] HashChunks(int chunks, int highWater)
        {
            var h = new ulong[chunks];
            for (int k = 0; k < chunks; k++)
                h[k] = Hash(MemoryMarshal.AsBytes(_chunks[k].AsSpan(0, UsedInChunk(k, highWater))));
            return h;
        }

        // Fills `indices` (pre-sized to the dirty count) while reading each chunk's payload.
        internal override Array ReadDirty(Stream stream, int[] indices, int chunks, int highWater)
        {
            var staged = new T[indices.Length][];
            int previous = -1;
            for (int j = 0; j < indices.Length; j++)
            {
                indices[j] = ReadChunkIndex(stream, chunks, previous);
                previous = indices[j];
                staged[j] = new T[ChunkSize];
                stream.ReadExactly(MemoryMarshal.AsBytes(staged[j].AsSpan(0, UsedInChunk(indices[j], highWater))));
            }
            return staged;
        }

        internal override void ApplyDirty(int[] indices, Array staged)
        {
            var data = (T[][])staged;
            for (int j = 0; j < indices.Length; j++) _chunks[indices[j]] = data[j];
        }

        private static ulong Fnv(string s)
        {
            ulong hash = 14695981039346656037ul;
            foreach (char ch in s) { hash ^= (byte)ch; hash *= 1099511628211ul; }
            return hash;
        }
    }
}
