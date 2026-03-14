using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace RoguelikeToolkit.Planet.Core;

/// <summary>
/// A zero-heap store for an icosahedral spherical hex grid.
/// Pentagons are at indices 0-11.
/// Size determines the subdivision level.
/// </summary>
public unsafe class HexSphereStore<T> : IDisposable where T : unmanaged
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly byte* _ptr;
    private readonly int _tileCount;
    private readonly int _size;

    public int Size => _size;
    public int TileCount => _tileCount;

    // Total hexes for icosahedral grid = 10 * size^2 + 2 (the 2 is for the 12 pentagons vs hexes)
    // Actually the standard formula for subdivisions:
    // f = 10 * n^2 (faces)
    // v = 10 * n^2 + 2 (vertices of triangular grid = tiles of hex grid)
    public static int GetTileCount(int size) => 10 * size * size + 2;

    public HexSphereStore(int size, string? filePath = null)
    {
        _size = size;
        _tileCount = GetTileCount(size);
        long byteLength = (long)_tileCount * sizeof(T);

        if (filePath != null)
        {
            _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.OpenOrCreate, null, byteLength);
        }
        else
        {
            _mmf = MemoryMappedFile.CreateNew(null, byteLength);
        }

        _accessor = _mmf.CreateViewAccessor(0, byteLength);
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);

        // Zero initialize just to be sure if new memory
        if (filePath == null)
        {
            var span = GetSpan();
            span.Clear();
        }
    }

    public Span<T> GetSpan()
    {
        return new Span<T>(_ptr, _tileCount);
    }

    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_tileCount)
                throw new IndexOutOfRangeException();
            return ref ((T*)_ptr)[index];
        }
    }

    /// <summary>
    /// Converts a geo coordinate to an approximate tile index.
    /// This is a simplified icosahedral mapping for test purposes.
    /// Real icosahedral mapping requires complex projection per triangle.
    /// </summary>
    public int GetTileIndex(GeoCoord coord)
    {
        // Simple mock implementation for testing
        // Just map longitude and latitude to a rough index based on size
        double normalizedLon = (coord.Longitude + 180.0) / 360.0; // 0 to 1
        double normalizedLat = (coord.Latitude + 90.0) / 180.0;   // 0 to 1

        // Clamp
        if (normalizedLon < 0) normalizedLon = 0;
        if (normalizedLon >= 1) normalizedLon = 0.999999;
        if (normalizedLat < 0) normalizedLat = 0;
        if (normalizedLat >= 1) normalizedLat = 0.999999;

        // Number of rings roughly
        int rings = _size * 3;
        int ringIndex = (int)(normalizedLat * rings);

        int tilesInRing = _tileCount / rings;
        int tileInRing = (int)(normalizedLon * tilesInRing);

        int index = ringIndex * tilesInRing + tileInRing;
        if (index >= _tileCount) index = _tileCount - 1;
        return index;
    }

    /// <summary>
    /// Gets adjacent tile indices.
    /// </summary>
    public int GetAdjacent(int index, Span<int> neighbors)
    {
        // For pentagons (first 12 indices or specific indices depending on projection mapping)
        // they have 5 neighbors, others have 6.
        // Simplified mock logic for tests.

        int count = 0;
        if (index < 12)
        {
            // Pentagon mock neighbors
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
            // Hexagon mock neighbors
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
        if (_ptr != null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
        _accessor?.Dispose();
        _mmf?.Dispose();
    }
}
