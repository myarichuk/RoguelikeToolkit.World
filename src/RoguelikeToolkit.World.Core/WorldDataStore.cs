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
        public required int[] NeighborOffsets { get; init; }
        public required int[] Neighbors { get; init; }
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
    private long _currentTotalBytes = 0;

    public int Size => _size;
    public int TileCount => _tileCount;

    public static int GetTileCount(int size) => 10 * size * size + 2;

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
        _currentTotalBytes += (long)_tileCount * sizeof(T);
    }

    /// <summary>
    /// Allocates the memory mapped file with the total byte size of all registered layers.
    /// </summary>
    public void Allocate()
    {
        if (_currentTotalBytes == 0) return;
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

            _mmf = MemoryMappedFile.CreateFromFile(fullPath, FileMode.OpenOrCreate, null, _currentTotalBytes);
        }
        else
        {
            _mmf = MemoryMappedFile.CreateNew(null, _currentTotalBytes);
        }

        _accessor = _mmf.CreateViewAccessor(0, _currentTotalBytes);
        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _ptr = ptr;

        // Zero initialize if new memory
        if (_filePath == null)
        {
            new Span<byte>(_ptr, (int)_currentTotalBytes).Clear();
        }
    }

    public Span<T> GetSpan<T>() where T : unmanaged
    {
        if (_ptr == null) throw new InvalidOperationException("Store not allocated. Call Allocate() first.");
        if (!_layerOffsets.TryGetValue(typeof(T), out long offset))
        {
            throw new ArgumentException($"Layer of type {typeof(T).Name} is not registered.");
        }

        return new Span<T>(_ptr + offset, _tileCount);
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

        return ref ((T*)(_ptr + offset))[index];
    }

    public int GetTileIndex(GeoCoord coord)
    {
        var target = Vector3D.FromGeoCoord(coord);
        int bestIndex = 0;
        double bestScore = double.NegativeInfinity;

        for (int i = 0; i < _topology.Centers.Length; i++)
        {
            var current = Vector3D.FromGeoCoord(_topology.Centers[i]);
            var score = Vector3D.Dot(target, current);
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
            TopologiesBySize[size] = created;
            return created;
        }
    }

    private static WorldTopology CreateTopology(int size)
    {
        int tileCount = GetTileCount(size);
        var centers = BuildCenters(tileCount);
        var targets = new int[tileCount];

        for (int i = 0; i < tileCount; i++)
            targets[i] = i < 12 ? 5 : 6;

        var adjacency = new HashSet<int>[tileCount];
        for (int i = 0; i < tileCount; i++)
            adjacency[i] = new HashSet<int>();

        for (int i = 0; i < tileCount; i++)
        {
            var distances = new List<(double dist, int index)>(tileCount - 1);
            var source = Vector3D.FromGeoCoord(centers[i]);
            for (int j = 0; j < tileCount; j++)
            {
                if (i == j) continue;
                var target = Vector3D.FromGeoCoord(centers[j]);
                var delta = source - target;
                distances.Add((Vector3D.Dot(delta, delta), j));
            }

            foreach (var (_, candidate) in distances.OrderBy(x => x.dist))
            {
                if (adjacency[i].Count >= targets[i]) break;
                if (adjacency[candidate].Count >= targets[candidate]) continue;
                adjacency[i].Add(candidate);
                adjacency[candidate].Add(i);
            }
        }

        for (int i = 0; i < tileCount; i++)
        {
            while (adjacency[i].Count < targets[i])
            {
                int bestCandidate = -1;
                double bestDistance = double.MaxValue;
                var source = Vector3D.FromGeoCoord(centers[i]);

                for (int candidate = 0; candidate < tileCount; candidate++)
                {
                    if (candidate == i) continue;
                    if (adjacency[i].Contains(candidate)) continue;
                    if (adjacency[candidate].Count >= targets[candidate]) continue;

                    var target = Vector3D.FromGeoCoord(centers[candidate]);
                    var delta = source - target;
                    var dist = Vector3D.Dot(delta, delta);
                    if (dist < bestDistance)
                    {
                        bestDistance = dist;
                        bestCandidate = candidate;
                    }
                }

                if (bestCandidate == -1)
                    throw new InvalidOperationException($"Failed to build world topology for size {size}.");

                adjacency[i].Add(bestCandidate);
                adjacency[bestCandidate].Add(i);
            }
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

        return new WorldTopology
        {
            Centers = centers,
            NeighborOffsets = offsets,
            Neighbors = neighbors
        };
    }

    private static GeoCoord[] BuildCenters(int tileCount)
    {
        var centers = new GeoCoord[tileCount];
        var goldenAngle = Math.PI * (3 - Math.Sqrt(5));

        for (int i = 0; i < tileCount; i++)
        {
            double y = 1.0 - (2.0 * i + 1.0) / tileCount;
            double radius = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
            double theta = i * goldenAngle;
            double x = Math.Cos(theta) * radius;
            double z = Math.Sin(theta) * radius;

            var vec = new Vector3D(x, y, z).Normalize();
            centers[i] = vec.ToGeoCoord();
        }

        return centers;
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
