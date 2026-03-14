using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public interface IMapOverlay<T> where T : unmanaged
{
    HexSphereStore<T> Store { get; }
    void Generate();
    T GetValue(GeoCoord coord);
}

public class OverlayManager
{
    // Keeping a list of references to overlays
    private readonly List<object> _overlays = new();

    public void Register<T>(IMapOverlay<T> overlay) where T : unmanaged
    {
        _overlays.Add(overlay);
    }

    /// <summary>
    /// Gets a list of overlays covering the specific coordinates.
    /// This allocates a span buffer instead of heap arrays for zero GC allocation.
    /// </summary>
    public int GetOverlaysAt(GeoCoord coord, Span<object> resultBuffer)
    {
        int count = 0;
        foreach (var overlay in _overlays)
        {
            if (count >= resultBuffer.Length) break;

            // Simplified mock - in reality we'd have a HasValue or GetValue checking default,
            // or we track bounds.
            resultBuffer[count++] = overlay;
        }

        return count;
    }
}
