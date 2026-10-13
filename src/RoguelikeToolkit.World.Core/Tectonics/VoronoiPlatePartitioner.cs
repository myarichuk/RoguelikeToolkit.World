using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Legacy partitioner: argmax(dot) assignment with Lloyd centroid relaxation.
/// Preserved for compatibility and determinism baselines.
/// </summary>
public sealed class VoronoiPlatePartitioner : IPlatePartitioner
{
    public int LloydIterations { get; set; } = 3;

    // Cached scratch: allocated once per (tileCount, seedCount) shape so
    // repeated Execute calls stay allocation-free.
    private Vector3D[] _working = Array.Empty<Vector3D>();
    private Vector3D[] _sums = Array.Empty<Vector3D>();
    private int[] _counts = Array.Empty<int>();

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
