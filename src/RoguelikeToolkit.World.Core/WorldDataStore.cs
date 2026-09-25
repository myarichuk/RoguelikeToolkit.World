using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// A zero-heap central data store for all layers in an icosahedral spherical hex grid.
/// Pentagons are at indices 0-11.
/// Size determines the subdivision level.
/// </summary>
public unsafe class WorldDataStore : IDisposable
{
    private sealed class WorldTopology
    {
        public required GeoCoord[] Centers { get; init; }
        public required Vector3D[] TileVectors { get; init; }
        public required int[] NeighborOffsets { get; init; }
        public required int[] Neighbors { get; init; }
        // Coarse lat/lon bucket index accelerating GetTileIndex. Longitude search
        // radius widens toward the poles where meridians converge (see GetTileIndex).
        public required int LatCells { get; init; }
        public required int LonCells { get; init; }
        public required int LatRadiusCells { get; init; }
        public required double CoverDeg { get; init; }
        public required double LonCellDeg { get; init; }
        public required int[] CellOffsets { get; init; }
        public required int[] CellTiles { get; init; }
    }

    private static readonly object TopologyLock = new();
    private static readonly Dictionary<int, WorldTopology> TopologiesBySize = new();

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private byte* _ptr;
    private readonly int _tileCount;
    private readonly int _size;
    private readonly string? _filePath;
    private readonly WorldTopology _topology;

    private readonly Dictionary<Type, long> _layerOffsets = new();
    private readonly Dictionary<Type, int> _layerStrides = new();
    private long _currentTotalBytes = 0;
    private long _dataOffset = 0;

    private const uint FileMagic = 0x31445357u; // "WDS1" little-endian
    private const int FileVersion = 1;
    private const int HeaderSize = 512;
    private const int MaxHeaderLayers = 8;
    private const int HeaderLayerEntrySize = 16; // 8 name hash + 4 stride + 4 reserved

    public int Size => _size;
    public int TileCount => _tileCount;

    public static int GetTileCount(int size)
    {
        // size acts as the recursion level
        // V = 10 * 4^recursionLevel + 2
        return 10 * (1 << (2 * size)) + 2;
    }

    public WorldDataStore(int size, string? filePath = null)
    {
        _size = size;
        _tileCount = GetTileCount(size);
        _filePath = filePath;
        _topology = GetOrCreateTopology(size);
    }

    /// <summary>
    /// Registers a layer of type T. This calculates the necessary byte offset for the layer.
    /// Note: Call Allocate() after registering all layers to actually create the memory mapped file.
    /// </summary>
    public void RegisterLayer<T>() where T : unmanaged
    {
        var type = typeof(T);
        if (_layerOffsets.ContainsKey(type)) return; // Already registered

        if (_mmf != null)
        {
            throw new InvalidOperationException("Cannot register layers after Allocate() has been called.");
        }

        _layerOffsets[type] = _currentTotalBytes;
        _layerStrides[type] = sizeof(T);
        _currentTotalBytes += (long)_tileCount * sizeof(T);
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
        if (_mmf != null) return;

        if (_filePath != null)
        {
            var baseDirectory = Path.GetFullPath(Environment.CurrentDirectory);
            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, _filePath));

            string baseDirectoryWithSeparator = baseDirectory;
            if (!baseDirectoryWithSeparator.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                baseDirectoryWithSeparator += Path.DirectorySeparatorChar;
            }

            if (!fullPath.StartsWith(baseDirectoryWithSeparator, StringComparison.Ordinal) &&
                fullPath != baseDirectory)
            {
                throw new UnauthorizedAccessException("Path traversal is not allowed.");
            }

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
            _mmf = MemoryMappedFile.CreateNew(null, _currentTotalBytes);
            _dataOffset = 0;
        }

        long viewBytes = _filePath != null ? HeaderSize + _currentTotalBytes : _currentTotalBytes;
        _accessor = _mmf.CreateViewAccessor(0, viewBytes);
        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _ptr = ptr;

        if (_filePath != null)
        {
            if (new FileInfo(Path.GetFullPath(Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), _filePath))).Length == HeaderSize + _currentTotalBytes
                && !IsFreshFile(Path.GetFullPath(Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), _filePath))))
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
            entry += HeaderLayerEntrySize;
        }
    }

    private static ulong LayerNameHash(Type type)
    {
        // FNV-1a 64 over the type name: fixed width, no truncation ambiguity for
        // nested/generic names, collision chance negligible for a count check.
        ulong hash = 14695981039346656037ul;
        string name = type.FullName ?? type.Name;
        for (int i = 0; i < name.Length; i++)
        {
            hash ^= (byte)name[i];
            hash *= 1099511628211ul;
        }
        return hash;
    }

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
            if (hash != LayerNameHash(type) || stride != _layerStrides[type])
                throw new InvalidDataException($"Store file layer table mismatch at entry {entry}.");
            entry += HeaderLayerEntrySize;
        }
    }

    public Span<T> GetSpan<T>() where T : unmanaged
    {
        if (_ptr == null) throw new InvalidOperationException("Store not allocated. Call Allocate() first.");
        if (!_layerOffsets.TryGetValue(typeof(T), out long offset))
        {
            throw new ArgumentException($"Layer of type {typeof(T).Name} is not registered.");
        }

        return new Span<T>(_ptr + _dataOffset + offset, _tileCount);
    }

    public ref T GetRef<T>(int index) where T : unmanaged
    {
        if ((uint)index >= (uint)_tileCount)
            throw new IndexOutOfRangeException();

        if (_ptr == null) throw new InvalidOperationException("Store not allocated. Call Allocate() first.");
        if (!_layerOffsets.TryGetValue(typeof(T), out long offset))
        {
            throw new ArgumentException($"Layer of type {typeof(T).Name} is not registered.");
        }

        return ref ((T*)(_ptr + _dataOffset + offset))[index];
    }

    /// <summary>
    /// Nearest tile center via the per-topology lat/lon bucket index. Falls back to
    /// the exhaustive scan only when the query neighborhood holds no candidates
    /// (possible solely for tiny topologies with empty cells).
    /// </summary>
    public int GetTileIndex(GeoCoord coord)
    {
        var target = Vector3D.FromGeoCoord(coord);
        var topo = _topology;

        int latC = (int)((coord.Latitude + 90.0) / 180.0 * topo.LatCells);
        if (latC < 0) latC = 0;
        else if (latC >= topo.LatCells) latC = topo.LatCells - 1;

        int lonC = (int)((coord.Longitude + 180.0) / 360.0 * topo.LonCells) % topo.LonCells;
        if (lonC < 0) lonC += topo.LonCells;

        int bestIndex = -1;
        double bestScore = double.NegativeInfinity;
        bool any = false;
        int latRadius = topo.LatRadiusCells;

        // Meridians converge toward the poles, so the same physical covering angle
        // spans more longitude cells there. When the latitude window touches a polar
        // row, scan the full circle: longitude is degenerate at the poles, so a tile
        // in the polar cap (e.g. the pole tile itself) can sit at any longitude while
        // being a fraction of a degree away. Otherwise use the smallest cosine over
        // the window rows (no clamp: the polar case is handled above, so cos > 0).
        double rowH = 180.0 / topo.LatCells;
        int topRow = Math.Min(latC + latRadius, topo.LatCells - 1);
        int botRow = Math.Max(latC - latRadius, 0);
        int lonRadius;
        if (topRow == topo.LatCells - 1 || botRow == 0)
        {
            lonRadius = topo.LonCells;
        }
        else
        {
            double edgeLat = Math.Max(Math.Abs((topRow + 1) * rowH - 90.0), Math.Abs(botRow * rowH - 90.0));
            double cosEdge = Math.Abs(Math.Cos(edgeLat * GeoCoord.Deg2Rad));
            lonRadius = (int)Math.Ceiling(topo.CoverDeg / (topo.LonCellDeg * cosEdge)) + 1;
            if (lonRadius >= topo.LonCells) lonRadius = topo.LonCells;
        }

        for (int dLa = -latRadius; dLa <= latRadius; dLa++)
        {
            int la = latC + dLa;
            if (la < 0 || la >= topo.LatCells) continue;
            for (int dLo = -lonRadius; dLo <= lonRadius; dLo++)
            {
                int lo = (lonC + dLo) % topo.LonCells;
                if (lo < 0) lo += topo.LonCells;
                int cell = la * topo.LonCells + lo;
                for (int k = topo.CellOffsets[cell]; k < topo.CellOffsets[cell + 1]; k++)
                {
                    any = true;
                    int tile = topo.CellTiles[k];
                    var score = Vector3D.Dot(target, topo.TileVectors[tile]);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = tile;
                    }
                }
            }
        }

        return any ? bestIndex : GetTileIndexExact(coord);
    }

    /// <summary>
    /// Exhaustive nearest-center search. The verification path for <see cref="GetTileIndex"/>.
    /// </summary>
    public int GetTileIndexExact(GeoCoord coord)
    {
        var target = Vector3D.FromGeoCoord(coord);
        int bestIndex = 0;
        double bestScore = double.NegativeInfinity;

        var vecs = _topology.TileVectors;
        for (int i = 0; i < vecs.Length; i++)
        {
            var score = Vector3D.Dot(target, vecs[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    public GeoCoord GetGeoCoord(int index)
    {
        if ((uint)index >= (uint)_tileCount)
            throw new IndexOutOfRangeException();

        return _topology.Centers[index];
    }


    public ReadOnlySpan<Vector3D> GetTileVectors()
    {
        return _topology.TileVectors;
    }

    public int GetAdjacent(int index, Span<int> neighbors)
    {
        if ((uint)index >= (uint)_tileCount)
            throw new IndexOutOfRangeException();

        int start = _topology.NeighborOffsets[index];
        int end = _topology.NeighborOffsets[index + 1];
        int count = 0;

        for (int i = start; i < end && count < neighbors.Length; i++)
            neighbors[count++] = _topology.Neighbors[i];

        return count;
    }

    private static WorldTopology GetOrCreateTopology(int size)
    {
        lock (TopologyLock)
        {
            if (TopologiesBySize.TryGetValue(size, out var existing))
                return existing;

            var created = CreateTopology(size);
            // Bounded cache: live stores hold their topology by reference, so clearing
            // only drops cached entries, never memory in use.
            if (TopologiesBySize.Count >= 16)
                TopologiesBySize.Clear();
            TopologiesBySize[size] = created;
            return created;
        }
    }

    private static WorldTopology CreateTopology(int recursionLevel)
    {
        IcosphereGenerator.Generate(recursionLevel, out Vector3D[] vertices, out TriangleIndices[] faces);

        int tileCount = vertices.Length;

        // Build adjacency from faces
        var adjacency = new HashSet<int>[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            adjacency[i] = new HashSet<int>();
        }

        foreach (var face in faces)
        {
            adjacency[face.v1].Add(face.v2);
            adjacency[face.v1].Add(face.v3);

            adjacency[face.v2].Add(face.v1);
            adjacency[face.v2].Add(face.v3);

            adjacency[face.v3].Add(face.v1);
            adjacency[face.v3].Add(face.v2);
        }

        var offsets = new int[tileCount + 1];
        int totalNeighbors = 0;
        for (int i = 0; i < tileCount; i++)
        {
            offsets[i] = totalNeighbors;
            totalNeighbors += adjacency[i].Count;
        }
        offsets[tileCount] = totalNeighbors;

        var neighbors = new int[totalNeighbors];
        int write = 0;
        for (int i = 0; i < tileCount; i++)
        {
            foreach (var neighbor in adjacency[i].OrderBy(n => n))
                neighbors[write++] = neighbor;
        }

        var centers = new GeoCoord[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            centers[i] = vertices[i].ToGeoCoord();
        }

        // Coarse lat/lon bucket index. Cells are at most half the minimum neighbor
        // spacing, so a bounded neighborhood around the query cell always contains
        // the nearest center for these near-regular grids.
        double minSpacing = double.MaxValue;
        double maxSpacing = 0.0;
        for (int i = 0; i < tileCount; i++)
        {
            for (int k = offsets[i]; k < offsets[i + 1]; k++)
            {
                double ang = Math.Acos(Math.Clamp(Vector3D.Dot(vertices[i], vertices[neighbors[k]]), -1.0, 1.0));
                if (ang < minSpacing) minSpacing = ang;
                if (ang > maxSpacing) maxSpacing = ang;
            }
        }

        double cellRad = Math.Max(minSpacing * 0.5, 1e-6);
        double cellDeg = cellRad * (180.0 / Math.PI);
        int latCells = (int)Math.Clamp(Math.Ceiling(180.0 / cellDeg), 4, 180);
        int lonCells = (int)Math.Clamp(Math.Ceiling(360.0 / cellDeg), 4, 360);
        // Covering bound: any query's nearest center lies within maxSpacing of it
        // (a query sits in some triangle; its nearest vertex is at most the longest
        // edge away, and every triangle edge is a neighbor pair).
        double coverDeg = maxSpacing * (180.0 / Math.PI);
        int latRadius = (int)Math.Clamp(Math.Ceiling(coverDeg / (180.0 / latCells)) + 1, 2, 8);

        int cellCount = latCells * lonCells;
        var cellSizes = new int[cellCount];
        var tileCell = new int[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            int cell = CellOf(centers[i], latCells, lonCells);
            tileCell[i] = cell;
            cellSizes[cell]++;
        }

        var cellOffsets = new int[cellCount + 1];
        for (int c = 0; c < cellCount; c++)
            cellOffsets[c + 1] = cellOffsets[c] + cellSizes[c];

        var cellTiles = new int[tileCount];
        var cursors = new int[cellCount];
        Array.Copy(cellOffsets, cursors, cellCount);
        for (int i = 0; i < tileCount; i++)
            cellTiles[cursors[tileCell[i]]++] = i;

        return new WorldTopology
        {
            Centers = centers,
            TileVectors = vertices,
            NeighborOffsets = offsets,
            Neighbors = neighbors,
            LatCells = latCells,
            LonCells = lonCells,
            LatRadiusCells = latRadius,
            CoverDeg = coverDeg,
            LonCellDeg = 360.0 / lonCells,
            CellOffsets = cellOffsets,
            CellTiles = cellTiles
        };
    }

    private static int CellOf(GeoCoord coord, int latCells, int lonCells)
    {
        int latC = (int)((coord.Latitude + 90.0) / 180.0 * latCells);
        if (latC < 0) latC = 0;
        else if (latC >= latCells) latC = latCells - 1;

        int lonC = (int)((coord.Longitude + 180.0) / 360.0 * lonCells) % lonCells;
        if (lonC < 0) lonC += lonCells;

        return latC * lonCells + lonC;
    }

    public void Dispose()
    {
        if (_ptr != null && _accessor != null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
        _accessor?.Dispose();
        _mmf?.Dispose();

        _ptr = null;
        _accessor = null;
        _mmf = null;
    }
}
