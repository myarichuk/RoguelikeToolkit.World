namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>
/// Open-addressing map from a stable 64-bit key to an <see cref="EntityId"/>, for idempotent
/// on-demand spawning: derive a key from (world seed, place, index), look it up, and only generate
/// and create the NPC when it is not already present. A destroyed entity's entry is simply
/// overwritten the next time that key is spawned. Allocation-free except when the table doubles.
/// </summary>
/// <example>
/// <code>
/// if (!map.TryGetLive(store, key, out var id)) { id = store.Create(); map.Set(key, id); /* fill columns from the seed */ }
/// </code>
/// </example>
public sealed class EntityKeyMap
{
    private struct Entry { public ulong Key; public int Index; public uint Generation; }   // Generation 0 = empty

    private Entry[] _entries;
    private int _count;

    public EntityKeyMap(int capacity = 1024)
    {
        int size = 16;
        while (size < capacity * 2) size <<= 1;
        _entries = new Entry[size];
    }

    public int Count => _count;

    /// <summary>Derives a key by mixing a world seed with up to two coordinates (e.g. planet tile and spawn index).</summary>
    public static ulong MakeKey(int worldSeed, int a, int b = 0)
    {
        ulong h = 14695981039346656037ul;
        h = (h ^ (uint)worldSeed) * 1099511628211ul;
        h = (h ^ (uint)a) * 1099511628211ul;
        h = (h ^ (uint)b) * 1099511628211ul;
        return h ^ (h >> 29);
    }

    private static int Hash(ulong key, int mask)
    {
        key ^= key >> 33; key *= 0xff51afd7ed558ccdul; key ^= key >> 33;
        return (int)key & mask;
    }

    public bool TryGet(ulong key, out EntityId id)
    {
        int mask = _entries.Length - 1;
        for (int i = Hash(key, mask); ; i = (i + 1) & mask)
        {
            ref var e = ref _entries[i];
            if (e.Generation == 0) { id = default; return false; }
            if (e.Key == key) { id = new EntityId(e.Index, e.Generation); return true; }
        }
    }

    /// <summary>Like <see cref="TryGet"/> but only succeeds when the entity is still alive in <paramref name="store"/>.</summary>
    public bool TryGetLive(EntityStore store, ulong key, out EntityId id)
        => TryGet(key, out id) && store.IsAlive(id);

    public void Set(ulong key, EntityId id)
    {
        if (id.IsNone) throw new ArgumentException("Cannot map a key to no entity.", nameof(id));
        if ((_count + 1) * 2 > _entries.Length) Rehash(_entries.Length * 2);
        int mask = _entries.Length - 1;
        for (int i = Hash(key, mask); ; i = (i + 1) & mask)
        {
            ref var e = ref _entries[i];
            if (e.Generation == 0) { e = new Entry { Key = key, Index = id.Index, Generation = id.Generation }; _count++; return; }
            if (e.Key == key) { e.Index = id.Index; e.Generation = id.Generation; return; }
        }
    }

    public bool Remove(ulong key)
    {
        int mask = _entries.Length - 1;
        int i = Hash(key, mask);
        while (true)
        {
            if (_entries[i].Generation == 0) return false;
            if (_entries[i].Key == key) break;
            i = (i + 1) & mask;
        }
        // Backward-shift deletion keeps probe chains intact without tombstones.
        int hole = i;
        for (int j = (i + 1) & mask; _entries[j].Generation != 0; j = (j + 1) & mask)
        {
            int home = Hash(_entries[j].Key, mask);
            // Move j into the hole if its home is not cyclically within (hole, j].
            bool inRange = hole <= j ? (home > hole && home <= j) : (home > hole || home <= j);
            if (inRange) continue;
            _entries[hole] = _entries[j];
            hole = j;
        }
        _entries[hole] = default;
        _count--;
        return true;
    }

    /// <summary>Rebuilds the map from a key column (0 = no key) after loading a snapshot.</summary>
    public void Rebuild(EntityStore store, EntityStore.Column<ulong> keys)
    {
        Array.Clear(_entries);
        _count = 0;
        for (int k = 0; k < keys.ChunkCount; k++)
        {
            var span = keys.Chunk(k);
            for (int i = 0; i < span.Length; i++)
            {
                int slot = (k << EntityStore.ChunkShift) + i;
                if (span[i] != 0 && store.IsAliveSlot(slot)) Set(span[i], store.HandleOf(slot));
            }
        }
    }

    private void Rehash(int size)
    {
        var old = _entries;
        _entries = new Entry[size];
        _count = 0;
        foreach (var e in old)
            if (e.Generation != 0) Set(e.Key, new EntityId(e.Index, e.Generation));
    }
}
