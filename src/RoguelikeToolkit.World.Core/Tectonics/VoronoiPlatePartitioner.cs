using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Tectonics;

/// <summary>
/// Legacy partitioner: argmax(dot) assignment with Lloyd centroid relaxation.
/// Preserved for compatibility and determinism baselines.
/// </summary>
public sealed class VoronoiPlatePartitioner : IPlatePartitioner
{
    /// <summary>Number of assign-then-relax rounds (at least 1 is run). Default 3.</summary>
    public int LloydIterations { get; set; } = 3;

    // Cached scratch: allocated once per (tileCount, seedCount) shape so
    // repeated Execute calls stay allocation-free.
    private Vector3D[] _working = Array.Empty<Vector3D>();
    private Vector3D[] _sums = Array.Empty<Vector3D>();
    private int[] _counts = Array.Empty<int>();

    /// <summary>Assigns each tile to the seed with the greatest dot product, relaxing the seed positions to cluster centroids between rounds.</summary>
    /// <param name="store">Provides the tile count.</param>
    /// <param name="positions">Unit-sphere position of every tile.</param>
    /// <param name="seeds">Initial plate seed positions; the caller's span is not modified.</param>
    /// <param name="plateIds">Receives the 0-based seed ordinal per tile.</param>
    /// <param name="seed">Ignored; the result is a pure function of the positions and seeds.</param>
    /// <remarks>Reuses internal scratch buffers between calls, so an instance must not be used from several threads at once.</remarks>
    public void Partition(
        WorldDataStore store,
        ReadOnlySpan<Vector3D> positions,
        ReadOnlySpan<Vector3D> seeds,
        Span<int> plateIds,
        int seed)
    {
        int count = seeds.Length;
        if (_working.Length < count)
        {
            _working = new Vector3D[count];
            _sums = new Vector3D[count];
            _counts = new int[count];
        }
        seeds.CopyTo(_working);
        int iterations = Math.Max(1, LloydIterations);

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i < store.TileCount; i++)
            {
                Vector3D pos = positions[i];
                int best = 0;
                double maxDot = double.NegativeInfinity;
                for (int s = 0; s < count; s++)
                {
                    double d = Vector3D.Dot(pos, _working[s]);
                    if (d > maxDot) { maxDot = d; best = s; }
                }
                plateIds[i] = best;
            }

            Array.Clear(_sums, 0, count);
            Array.Clear(_counts, 0, count);
            for (int i = 0; i < store.TileCount; i++)
            {
                int s = plateIds[i];
                _sums[s] += positions[i];
                _counts[s]++;
            }
            for (int s = 0; s < count; s++)
            {
                if (_counts[s] > 0)
                    _working[s] = (_sums[s] / _counts[s]).Normalize();
            }
        }
    }
}
