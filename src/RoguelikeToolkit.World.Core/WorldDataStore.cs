using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// A zero-heap central data store for all layers in an icosahedral spherical hex grid.
/// Pentagons are at indices 0-11.
/// Size determines the subdivision level.
/// </summary>
public unsafe class WorldDataStore : IDisposable
{
    private static readonly Dictionary<int, Vector3D[]> TilePositionCache = new();
    private static readonly object TilePositionCacheLock = new();

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private byte* _ptr;
    private readonly int _tileCount;
    private readonly int _size;
    private readonly string? _filePath;

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
        double normalizedLon = (coord.Longitude + 180.0) / 360.0;
        double normalizedLat = (coord.Latitude + 90.0) / 180.0;

        if (normalizedLon < 0) normalizedLon = 0;
        if (normalizedLon >= 1) normalizedLon = 0.999999;
        if (normalizedLat < 0) normalizedLat = 0;
        if (normalizedLat >= 1) normalizedLat = 0.999999;

        int rings = _size * 3;
        int ringIndex = (int)(normalizedLat * rings);

        int tilesInRing = _tileCount / rings;
        if (tilesInRing == 0) tilesInRing = 1;
        int tileInRing = (int)(normalizedLon * tilesInRing);

        int index = ringIndex * tilesInRing + tileInRing;
        if (index >= _tileCount) index = _tileCount - 1;
        return index;
    }

    public GeoCoord GetGeoCoord(int index)
    {
        if (index < 0) index = 0;
        if (index >= _tileCount) index = _tileCount - 1;

        int rings = _size * 3;
        if (rings == 0) rings = 1;
        int tilesInRing = _tileCount / rings;
        if (tilesInRing == 0) tilesInRing = 1;

        int ringIndex = index / tilesInRing;
        int tileInRing = index % tilesInRing;

        double normalizedLat = (ringIndex + 0.5) / rings;
        double normalizedLon = (tileInRing + 0.5) / tilesInRing;

        double lat = normalizedLat * 180.0 - 90.0;
        double lon = normalizedLon * 360.0 - 180.0;

        return new GeoCoord(lat, lon);
    }

    public ReadOnlySpan<Vector3D> GetTileVectors()
    {
        lock (TilePositionCacheLock)
        {
            if (TilePositionCache.TryGetValue(_size, out var cached))
            {
                return cached;
            }

            var vectors = new Vector3D[_tileCount];
            for (int i = 0; i < _tileCount; i++)
            {
                vectors[i] = Vector3D.FromGeoCoord(GetGeoCoord(i));
            }

            TilePositionCache[_size] = vectors;
            return vectors;
        }
    }

    public int GetAdjacent(int index, Span<int> neighbors)
    {
        int count = 0;
        if (index < 12)
        {
            for (int i = 0; i < 5; i++)
            {
                if (count < neighbors.Length)
                {
                    neighbors[count++] = (index + i + 1) % _tileCount;
                }
            }
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                if (count < neighbors.Length)
                {
                    neighbors[count++] = (index + i + 1) % _tileCount;
                }
            }
        }
        return count;
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
