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
public sealed partial class EntityStore
{
    /// <summary>log2 of <see cref="ChunkSize"/>: a slot's chunk is <c>slot &gt;&gt; ChunkShift</c>.</summary>
    public const int ChunkShift = 10;
    /// <summary>Slots per chunk (1024). The generation table and every column grow in whole chunks of this size.</summary>
    public const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

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

    /// <summary>Slots allocated so far, always a whole number of chunks and at least <see cref="HighWater"/>. Grows by one chunk when a spawn finds no free slot.</summary>
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

    /// <summary>The column registered for <typeparamref name="T"/>. A store holds at most one column per element type. Throws <see cref="ArgumentException"/> when <see cref="AddColumn{T}"/> was never called for it.</summary>
    public Column<T> GetColumn<T>() where T : unmanaged
    {
        int id = ColumnSlot<T>.Id;
        if ((uint)id >= (uint)_columnById.Length || _columnById[id] == null)
            throw new ArgumentException($"Column {typeof(T).Name} is not registered.");
        return (Column<T>)_columnById[id]!;
    }

    private ref uint GenerationAt(int slot) => ref _generations[slot >> ChunkShift][slot & ChunkMask];

    /// <summary>True when <paramref name="id"/> names a currently live entity: false for <see cref="EntityId.None"/>, for destroyed or recycled handles (stale generation) and for indices beyond <see cref="HighWater"/>.</summary>
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
}
