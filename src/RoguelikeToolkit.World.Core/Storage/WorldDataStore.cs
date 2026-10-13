using System.IO.MemoryMappedFiles;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Storage;

/// <summary>
/// A zero-heap central data store for all layers in an icosahedral spherical hex grid.
/// Pentagons are at indices 0-11.
/// Size determines the subdivision level.
/// </summary>
public unsafe class WorldDataStore : IDisposable
{

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    // In-memory stores on netstandard2.1 (Unity) and on browser/WASI .NET: unmanaged heap instead of an
    // anonymous memory-mapped file, which those platforms do not support (WebGL, browser-wasm, some mobile).
    // File-backed stores still use the mapping.
    private IntPtr _heap;
    private byte* _ptr;
    private readonly int _tileCount;
    private readonly int _size;
    private readonly string? _filePath;
    private readonly TileGrid _topology;

    /// <summary>
    /// Process-wide dense id per layer type, so typed accessors index an array
    /// instead of hashing a <see cref="Type"/> on every call.
    /// </summary>
    private static class LayerSlot<T> where T : unmanaged
    {
        public static readonly int Id = Interlocked.Increment(ref _nextId) - 1;
    }
    private static int _nextId;

    // Offset per LayerSlot id; -1 = not registered in this store.
    private long[] _offsetById = Array.Empty<long>();

    private readonly Dictionary<Type, long> _layerOffsets = new();
    private readonly Dictionary<Type, int> _layerStrides = new();
    private readonly Dictionary<Type, int> _tableLengths = new();
    // Rows per LayerSlot id (tables only; per-tile layers use TileCount).
    private int[] _lengthById = Array.Empty<int>();
    private long _currentTotalBytes = 0;
    private long _dataOffset = 0;

    private const uint FileMagic = 0x31445357u; // "WDS1" little-endian
    private const int FileVersion = 4; // 4: layer ids are namespace-independent (StableTypeId)
    // v2 header: room for 32 field layers (dense per-tile structs).
    // v3: an entry's reserved word holds the row count for fixed-length tables
    // (0 = per-tile layer), so per-plate data need not be repeated on every tile.
    // Sparse feature catalogs (rivers, water bodies, ranges) live outside
    // the memory-mapped store and are not counted here.
    private const int HeaderSize = 1024;
    private const int MaxHeaderLayers = 32;
    private const int HeaderLayerEntrySize = 16; // 8 name hash + 4 stride + 4 table length (0 = per-tile)

    /// <summary>Subdivision level of the icosphere (0 to <see cref="WorldBuilder.MaxSupportedSize"/>); see <see cref="TileCount"/>.</summary>
    public int Size => _size;
    /// <summary>Number of tiles, <c>10 * 4^Size + 2</c>; indices 0-11 are the 12 pentagons, the rest hexagons.</summary>
    public int TileCount => _tileCount;

    /// <summary>Tile count for a subdivision level, <c>10 * 4^size + 2</c>. Throws <see cref="ArgumentOutOfRangeException"/> outside 0..<see cref="WorldBuilder.MaxSupportedSize"/>.</summary>
    public static int GetTileCount(int size)
    {
        // size acts as the recursion level
        // V = 10 * 4^recursionLevel + 2
        if (size < 0 || size > WorldBuilder.MaxSupportedSize)
            throw new ArgumentOutOfRangeException(nameof(size), size,
                $"Planet size must be 0..{WorldBuilder.MaxSupportedSize} (size 8+ is unsupported; see docs/ARCHITECTURE.md).");
        return 10 * (1 << (2 * size)) + 2;
    }

    /// <summary>Creates an unallocated store: register layers and tables, then call <see cref="Allocate"/>. A null <paramref name="filePath"/> gives an in-memory store; otherwise the data is memory-mapped from that file, resolved against the current directory (<see cref="Allocate"/> rejects paths that escape it). Throws <see cref="ArgumentOutOfRangeException"/> for a <paramref name="size"/> outside the supported range.</summary>
    public WorldDataStore(int size, string? filePath = null)
    {
        _size = size;
        _tileCount = GetTileCount(size);
        _filePath = filePath;
        _topology = TileGrid.For(size);
    }

    /// <summary>
    /// Registers a layer of type T. This calculates the necessary byte offset for the layer.
    /// Note: Call Allocate() after registering all layers to actually create the memory mapped file.
    /// </summary>
    public void RegisterLayer<T>() where T : unmanaged => Register<T>(_tileCount, isTable: false);

    /// <summary>
    /// Registers a fixed-length table of <paramref name="length"/> rows (e.g. one row per
    /// tectonic plate) in the same mapped block as the per-tile layers. Read it with
    /// <see cref="GetTable{T}"/>. A type is either a layer or a table, never both.
    /// </summary>
    public void RegisterTable<T>(int length) where T : unmanaged
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Register<T>(length, isTable: true);
    }

    private void Register<T>(int rows, bool isTable) where T : unmanaged
    {
        var type = typeof(T);
        if (_layerOffsets.ContainsKey(type))
        {
            if (isTable != _tableLengths.ContainsKey(type) || (isTable && _tableLengths[type] != rows))
                throw new InvalidOperationException($"{type.Name} is already registered with a different shape.");
            return; // Already registered
        }

        if (IsAllocated)
        {
            throw new InvalidOperationException("Cannot register layers after Allocate() has been called.");
        }

        int id = LayerSlot<T>.Id;
        if (id >= _offsetById.Length)
        {
            int old = _offsetById.Length;
            Array.Resize(ref _offsetById, Math.Max(id + 1, Math.Max(8, old * 2)));
            Array.Fill(_offsetById, -1L, old, _offsetById.Length - old);
            Array.Resize(ref _lengthById, _offsetById.Length);
        }
        // 16-byte alignment keeps doubles in tables aligned after any preceding layer.
        _currentTotalBytes = (_currentTotalBytes + 15) & ~15L;
        _offsetById[id] = _currentTotalBytes;
        _lengthById[id] = rows;
        _layerOffsets[type] = _currentTotalBytes;
        _layerStrides[type] = sizeof(T);
        if (isTable) _tableLengths[type] = rows;
        _currentTotalBytes += (long)rows * sizeof(T);
    }

    /// <summary>
    /// Allocates the memory mapped file with the total byte size of all registered layers.
    /// File-backed stores carry a versioned header; reopening a file whose header does
    /// not match (size, tile count, layer set) throws InvalidDataException instead of
    /// silently aliasing incompatible bytes.
    /// </summary>
    public void Allocate()
    {
        if (_currentTotalBytes == 0)
            throw new InvalidOperationException("No layers registered. Call RegisterLayer<T>() before Allocate().");
        if (IsAllocated) return;

        string? fullPath = null;
        if (_filePath != null)
        {
            fullPath = ResolveStorePath(_filePath);

            if (_layerOffsets.Count > MaxHeaderLayers)
                throw new InvalidOperationException($"File-backed stores support at most {MaxHeaderLayers} layers.");

            long totalBytes = HeaderSize + _currentTotalBytes;
            bool exists = File.Exists(fullPath) && new FileInfo(fullPath).Length > 0;
            if (exists && new FileInfo(fullPath).Length != totalBytes)
                throw new InvalidDataException($"Store file '{_filePath}' has an unexpected size and does not match this layer set.");

            _mmf = MemoryMappedFile.CreateFromFile(fullPath, FileMode.OpenOrCreate, null, totalBytes);
            _dataOffset = HeaderSize;
        }
        else
        {
            _dataOffset = 0;
            if (UseHeapForInMemory)
            {
                _heap = System.Runtime.InteropServices.Marshal.AllocHGlobal(new IntPtr(_currentTotalBytes));
                _ptr = (byte*)_heap;
                new Span<byte>(_ptr, (int)_currentTotalBytes).Clear(); // AllocHGlobal memory is not zeroed
                return;
            }
            _mmf = MemoryMappedFile.CreateNew(null, _currentTotalBytes);
        }

        long viewBytes = _filePath != null ? HeaderSize + _currentTotalBytes : _currentTotalBytes;
        _accessor = _mmf.CreateViewAccessor(0, viewBytes);
        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _ptr = ptr;

        if (_filePath != null)
        {
            if (new FileInfo(fullPath!).Length == HeaderSize + _currentTotalBytes
                && !IsFreshFile(fullPath!))
            {
                ValidateFileHeader();
            }
            else
            {
                WriteFileHeader();
            }
        }
        else
        {
            // Zero initialize new memory
            new Span<byte>(_ptr, (int)_currentTotalBytes).Clear();
        }
    }

    /// <summary>
    /// Resolves <paramref name="path"/> against the current directory and proves it stays inside it. The check is on the
    /// real location, not the spelling: on .NET 6+ every symbolic link or junction along the path is followed first, so a
    /// link inside the directory that points outside is rejected like a literal <c>..</c>. The returned path is the one to
    /// open (the resolved target), so nothing is re-resolved between the check and the open.
    /// </summary>
    private static string ResolveStorePath(string path)
    {
        var baseDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, path));
#if NET6_0_OR_GREATER
        baseDirectory = ResolveLinks(baseDirectory, 0);
        fullPath = ResolveLinks(fullPath, 0);
#endif
        string baseWithSeparator = baseDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? baseDirectory
            : baseDirectory + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(baseWithSeparator, StringComparison.Ordinal) && fullPath != baseDirectory)
            throw new UnauthorizedAccessException("Path traversal is not allowed.");
        return fullPath;
    }

#if NET6_0_OR_GREATER
    private const int MaxLinkHops = 40;

    // Walks the path one segment at a time and follows each link it meets, whether the link is the file itself or a
    // directory in the middle. A link's target is itself re-walked, since it may sit under another link.
    // Segments that do not exist yet (a store file about to be created) are kept as spelled.
    private static string ResolveLinks(string fullPath, int hops)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = root;
        var segments = fullPath.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists || info.LinkTarget == null) continue;
            if (hops >= MaxLinkHops) throw new UnauthorizedAccessException("Too many symbolic links in the store path.");
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target == null) continue;
            current = ResolveLinks(Path.GetFullPath(target.FullName), hops + 1);
        }
        return current;
    }
#endif

    private static bool IsFreshFile(string fullPath)
    {
        // A freshly created (or truncated) file reads all zeros; treat zero magic as "new".
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> magic = stackalloc byte[4];
        return fs.Read(magic) < 4 || BitConverter.ToUInt32(magic) == 0;
    }

    private void WriteFileHeader()
    {
        var head = new Span<byte>(_ptr, HeaderSize);
        head.Clear();
        BitConverter.TryWriteBytes(head.Slice(0, 4), FileMagic);
        BitConverter.TryWriteBytes(head.Slice(4, 4), FileVersion);
        BitConverter.TryWriteBytes(head.Slice(8, 4), _size);
        BitConverter.TryWriteBytes(head.Slice(12, 4), _tileCount);
        BitConverter.TryWriteBytes(head.Slice(16, 4), _layerOffsets.Count);

        int entry = 20;
        foreach (var (type, _) in _layerOffsets.OrderBy(kv => kv.Value))
        {
            BitConverter.TryWriteBytes(head.Slice(entry, 8), LayerNameHash(type));
            BitConverter.TryWriteBytes(head.Slice(entry + 8, 4), _layerStrides[type]);
            BitConverter.TryWriteBytes(head.Slice(entry + 12, 4), _tableLengths.GetValueOrDefault(type));
            entry += HeaderLayerEntrySize;
        }
    }

    private static ulong LayerNameHash(Type type) => StableTypeId.Hash(type);

    private void ValidateFileHeader()
    {
        var head = new Span<byte>(_ptr, HeaderSize);
        uint magic = BitConverter.ToUInt32(head.Slice(0, 4));
        int version = BitConverter.ToInt32(head.Slice(4, 4));
        int size = BitConverter.ToInt32(head.Slice(8, 4));
        int tileCount = BitConverter.ToInt32(head.Slice(12, 4));
        int layerCount = BitConverter.ToInt32(head.Slice(16, 4));

        if (magic != FileMagic || version != FileVersion || size != _size ||
            tileCount != _tileCount || layerCount != _layerOffsets.Count)
            throw new InvalidDataException("Store file header does not match this world configuration.");

        int entry = 20;
        foreach (var (type, _) in _layerOffsets.OrderBy(kv => kv.Value))
        {
            ulong hash = BitConverter.ToUInt64(head.Slice(entry, 8));
            int stride = BitConverter.ToInt32(head.Slice(entry + 8, 4));
            int tableLength = BitConverter.ToInt32(head.Slice(entry + 12, 4));
            if (hash != LayerNameHash(type) || stride != _layerStrides[type] || tableLength != _tableLengths.GetValueOrDefault(type))
                throw new InvalidDataException($"Store file layer table mismatch at entry {entry}.");
            entry += HeaderLayerEntrySize;
        }
    }

    /// <summary>Zeroes one registered layer (all tiles).</summary>
    public void ClearLayer(Type type)
    {
        if (_ptr == null) throw new InvalidOperationException("Store not allocated. Call Allocate() first.");
        if (!_layerOffsets.TryGetValue(type, out long offset))
            throw new ArgumentException($"Layer of type {type.Name} is not registered.");
        long bytes = (long)_layerStrides[type] * _tableLengths.GetValueOrDefault(type, _tileCount);
        new Span<byte>(_ptr + _dataOffset + offset, checked((int)bytes)).Clear();
    }

    /// <summary>True when a layer or table of this type is registered. Non-generic form of <see cref="IsLayerRegistered{T}"/>.</summary>
    public bool IsLayerRegistered(Type type) => _layerOffsets.ContainsKey(type);

    /// <summary>True when a layer or table of type <typeparamref name="T"/> is registered with this store.</summary>
    public bool IsLayerRegistered<T>() where T : unmanaged
    {
        int id = LayerSlot<T>.Id;
        return (uint)id < (uint)_offsetById.Length && _offsetById[id] >= 0;
    }

    private long OffsetOf<T>() where T : unmanaged
    {
        if (_ptr == null) throw new InvalidOperationException("Store not allocated. Call Allocate() first.");
        var offsets = _offsetById;
        int id = LayerSlot<T>.Id;
        if ((uint)id >= (uint)offsets.Length || offsets[id] < 0)
            throw new ArgumentException($"Layer of type {typeof(T).Name} is not registered. Call RegisterLayer<{typeof(T).Name}>() before Allocate().");
        return offsets[id];
    }

    /// <summary>All tiles of the <typeparamref name="T"/> layer, indexed by tile index, as a span over the store's memory (no copy: writes go straight to the store or its file). The span is invalid after <see cref="Dispose"/>. Throws <see cref="InvalidOperationException"/> if the store is not allocated or <typeparamref name="T"/> is a table, and <see cref="ArgumentException"/> if it is not registered.</summary>
    public Span<T> GetSpan<T>() where T : unmanaged
    {
        long offset = OffsetOf<T>();
        if (_lengthById[LayerSlot<T>.Id] != _tileCount)
            throw new InvalidOperationException($"{typeof(T).Name} is a table; use GetTable<{typeof(T).Name}>().");
        return new Span<T>(_ptr + _dataOffset + offset, _tileCount);
    }

    /// <summary>The rows of a table registered with <see cref="RegisterTable{T}"/>.</summary>
    public Span<T> GetTable<T>() where T : unmanaged
    {
        long offset = OffsetOf<T>();
        return new Span<T>(_ptr + _dataOffset + offset, _lengthById[LayerSlot<T>.Id]);
    }

    /// <summary>True when <typeparamref name="T"/> was registered with <see cref="RegisterTable{T}"/> (as opposed to a per-tile layer).</summary>
    public bool IsTableRegistered<T>() where T : unmanaged => _tableLengths.ContainsKey(typeof(T));

    /// <summary>A reference to one tile's <typeparamref name="T"/> value in the store's memory; writes are visible to everyone reading the layer. <typeparamref name="T"/> must be a per-tile layer. Throws <see cref="IndexOutOfRangeException"/> for an index outside <c>0..TileCount-1</c>, and the same errors as <see cref="GetSpan{T}"/> for an unallocated or unregistered type or a table.</summary>
    public ref T GetRef<T>(int index) where T : unmanaged
    {
        if ((uint)index >= (uint)_tileCount)
            throw new IndexOutOfRangeException();

        long offset = OffsetOf<T>();
        if (_lengthById[LayerSlot<T>.Id] != _tileCount)
            throw new InvalidOperationException($"{typeof(T).Name} is a table; use GetTable<{typeof(T).Name}>().");
        return ref ((T*)(_ptr + _dataOffset + offset))[index];
    }

    /// <summary>Nearest tile center via the shared <see cref="TileGrid"/> bucket index.</summary>
    public int GetTileIndex(GeoCoord coord) => _topology.NearestTile(coord);

    /// <summary>Exhaustive nearest-center search. The verification path for <see cref="GetTileIndex"/>.</summary>
    public int GetTileIndexExact(GeoCoord coord) => _topology.NearestTileExact(coord);

    /// <summary>Centre of a tile. Throws <see cref="IndexOutOfRangeException"/> for an index outside <c>0..TileCount-1</c>.</summary>
    public GeoCoord GetGeoCoord(int index)
    {
        if ((uint)index >= (uint)_tileCount)
            throw new IndexOutOfRangeException();

        return _topology.Centers[index];
    }


    /// <summary>The shared topology array itself (no copy); treat as read-only.</summary>
    internal Vector3D[] TileVectorArray => _topology.TileVectors;

    /// <summary>Unit-vector centres of all tiles, indexed by tile index. This is the shared topology array for the store's size (no copy; every store of that size sees the same one), so it is read-only.</summary>
    public ReadOnlySpan<Vector3D> GetTileVectors()
    {
        return _topology.TileVectors;
    }

    /// <summary>Writes the neighbouring tile indices of <paramref name="index"/> into <paramref name="neighbors"/> and returns how many were written: 5 for the pentagons (0-11), 6 for hexagons, fewer if the span is shorter. Throws <see cref="IndexOutOfRangeException"/> for an invalid index.</summary>
    public int GetAdjacent(int index, Span<int> neighbors)
    {
        if ((uint)index >= (uint)_tileCount)
            throw new IndexOutOfRangeException();

        return _topology.Adjacent(index, neighbors);
    }

    /// <summary>
    /// True where anonymous memory-mapped files are unavailable: netstandard2.1 (Unity players, WebGL)
    /// and browser/WASI .NET, where <see cref="MemoryMappedFile.CreateNew(string, long)"/> throws
    /// PlatformNotSupportedException.
    /// </summary>
    private static bool UseHeapForInMemory =>
#if NET
        OperatingSystem.IsBrowser() || OperatingSystem.IsWasi();
#else
        true;
#endif

    private bool IsAllocated => _mmf != null || _heap != IntPtr.Zero;

    /// <summary>Releases the heap block or the file mapping. Spans and references obtained earlier must not be used afterwards. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_heap != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(_heap);
            _heap = IntPtr.Zero;
            _ptr = null;
        }
        if (_ptr != null && _accessor != null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
        _accessor?.Dispose();
        _mmf?.Dispose();

        _ptr = null;
        _accessor = null;
        _mmf = null;
    }}
