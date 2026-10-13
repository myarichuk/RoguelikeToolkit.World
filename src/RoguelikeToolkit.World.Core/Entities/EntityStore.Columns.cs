using System.Runtime.InteropServices;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Entities;

public sealed partial class EntityStore
{
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

        internal override ulong TypeHash => StableTypeId.Hash(typeof(T));
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
    }
}
