using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Strategy for assigning tiles to tectonic plate seeds. The previous
/// implementation was a Lloyd-relaxed spherical Voronoi
/// (<see cref="VoronoiPlatePartitioner"/>): exact, but its great-circle
/// bisectors read as unnaturally straight plate borders. The default is now
/// <see cref="NoisyFloodFillPlatePartitioner"/>, which grows plates at
/// per-plate speeds through a fractally warped space so borders come out tortuous.
/// </summary>
public interface IPlatePartitioner
{
    /// <summary>
    /// Assigns every tile to a seed. Writes a 0-based seed ordinal per tile
    /// into <paramref name="plateIds"/> (length must equal
    /// <see cref="WorldDataStore.TileCount"/>). Must be deterministic: a pure
    /// function of the store topology, seed positions, and
    /// <paramref name="seed"/>.
    /// </summary>
    void Partition(
        WorldDataStore store,
        ReadOnlySpan<Vector3D> positions,
        ReadOnlySpan<Vector3D> seeds,
        Span<int> plateIds,
        int seed);
}

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

/// <summary>
/// Default partitioner: simultaneous multi-source Dijkstra growth in a <em>warped</em> space. Each plate spreads
/// from its seed tile at its own speed, and a step costs the chord between the two tiles' warped positions, where
/// the warp is a fractal displacement field shared by every plate. The border between two plates is where their
/// warped distances tie, i.e. a smooth bisector pulled back through the displacement, so it wanders at every
/// scale of the noise: tortuous, jogged and non-convex, but each plate is still one connected region because it
/// is a graph search.
/// <para>
/// Why the warp and not noise on the step cost (the earlier design): a front's distance is a sum of ~20 step
/// costs, and a sum of independent noise averages out. Measured at size 4 over eight seeds, per-plate cost
/// wobble made the border only 3% longer than a plain Voronoi bisector, i.e. the borders were straight. Thin
/// "barrier" ridges did not help either (fronts walk round a ridge segment). A warp is coherent: every front that
/// crosses a stretched area sees the same stretch, so it does not average away.
/// </para>
/// Ties resolve toward the lower plate ordinal, and heap order breaks remaining ties by tile index, keeping
/// output deterministic.
/// </summary>
public sealed class NoisyFloodFillPlatePartitioner : IPlatePartitioner
{
    /// <summary>Warp amplitude, in unit-sphere lengths (one tile is about 0.045 at size 4). 0 collapses to speed-weighted Voronoi.</summary>
    public double Warp { get; set; } = 0.8;

    /// <summary>Spatial frequency of the coarsest warp octave (unit-sphere vectors). Higher means more, smaller wiggles.</summary>
    public double NoiseFrequency { get; set; } = 1.5;

    /// <summary>Warp octaves. Each adds a finer scale of wiggle at half the amplitude (fractal borders).</summary>
    public int Octaves { get; set; } = 6;

    // Cached scratch: allocated once per tile-count shape so repeated
    // Execute calls stay allocation-free (steady-state zero-GC).
    private double[] _speeds = Array.Empty<double>();
    private Vector3D[] _warped = Array.Empty<Vector3D>();
    private double[] _dist = Array.Empty<double>();
    private int[] _owner = Array.Empty<int>();
    private double[] _hDist = Array.Empty<double>();
    private int[] _hTile = Array.Empty<int>();

    public void Partition(
        WorldDataStore store,
        ReadOnlySpan<Vector3D> positions,
        ReadOnlySpan<Vector3D> seeds,
        Span<int> plateIds,
        int seed)
    {
        int n = store.TileCount;
        int p = seeds.Length;
        if (p <= 0) throw new ArgumentException("At least one plate seed is required.", nameof(seeds));
        if (plateIds.Length < n) throw new ArgumentException("plateIds is shorter than the tile count.", nameof(plateIds));

        if (_dist.Length < n)
        {
            _dist = new double[n];
            _warped = new Vector3D[n];
            _owner = new int[n];
            _hDist = new double[Math.Max(16, n * 2)];
            _hTile = new int[_hDist.Length];
        }
        if (_speeds.Length < p) _speeds = new double[p];

        // Per-plate growth speeds: pure function of (seed, plate ordinal).
        for (int s = 0; s < p; s++)
            _speeds[s] = 0.6 + 1.4 * Rng.Create(seed + 5000, s).NextDouble();

        var dist = _dist;
        var owner = _owner;
        var warped = _warped;
        for (int i = 0; i < n; i++) { dist[i] = double.PositiveInfinity; owner[i] = -1; }

        // One shared displacement field, evaluated once per tile (the old per-plate wobble evaluated noise per edge).
        for (int i = 0; i < n; i++) warped[i] = WarpedPosition(positions[i], seed);

        // Seed tiles: nearest tile to each seed position. Later seeds skip
        // tiles already taken so no plate starts orphaned.
        for (int s = 0; s < p; s++)
        {
            int best = -1;
            double bestDot = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (dist[i] == 0.0) continue;
                double d = Vector3D.Dot(positions[i], seeds[s]);
                if (d > bestDot) { bestDot = d; best = i; }
            }
            if (best < 0) break;
            dist[best] = 0.0;
            owner[best] = s;
        }

        // Binary min-heap over (dist, tileIndex) snapshots. Stale pops are
        // skipped by comparing against the live dist table.
        var hDist = _hDist;
        var hTile = _hTile;
        int heapCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (owner[i] >= 0)
                Push(ref hDist, ref hTile, ref heapCount, 0.0, i);
        }

        Span<int> neighbors = stackalloc int[6];

        while (heapCount > 0)
        {
            double curDist = hDist[0];
            int cur = hTile[0];
            heapCount--;
            hDist[0] = hDist[heapCount];
            hTile[0] = hTile[heapCount];
            if (heapCount > 0) SiftDown(hDist, hTile, heapCount, 0);
            if (curDist > dist[cur]) continue; // stale snapshot
            curDist = dist[cur];
            int plate = owner[cur];

            int adjacent = store.GetAdjacent(cur, neighbors);
            double invSpeed = 1.0 / Math.Max(0.05, _speeds[plate]);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = neighbors[k];
                double step = (warped[nb] - warped[cur]).Length * invSpeed;
                double nd = curDist + step;
                double od = dist[nb];
                if (nd < od - 1e-12 || (Math.Abs(nd - od) <= 1e-12 && plate < owner[nb]))
                {
                    dist[nb] = nd;
                    owner[nb] = plate;
                    Push(ref hDist, ref hTile, ref heapCount, nd, nb);
                }
            }
        }

        for (int i = 0; i < n; i++) plateIds[i] = owner[i] < 0 ? 0 : owner[i];

        // Push may have grown the heap; publish the arrays so the next call
        // reuses them instead of reallocating.
        _hDist = hDist;
        _hTile = hTile;
    }

    /// <summary>Position plus a fractal vector displacement. Pure function of position and seed.</summary>
    private Vector3D WarpedPosition(Vector3D pos, int seed)
    {
        if (Warp <= 0.0) return pos;
        var q = pos * NoiseFrequency;
        int oct = Math.Max(1, Octaves);
        double dx = SphereNoise.Fbm(q, seed + 6100, oct);
        double dy = SphereNoise.Fbm(q, seed + 6200, oct);
        double dz = SphereNoise.Fbm(q, seed + 6300, oct);
        return new Vector3D(pos.X + dx * Warp, pos.Y + dy * Warp, pos.Z + dz * Warp);
    }

    private static void Push(ref double[] hDist, ref int[] hTile, ref int count, double d, int tile)
    {
        if (count == hDist.Length)
        {
            Array.Resize(ref hDist, hDist.Length * 2);
            Array.Resize(ref hTile, hTile.Length * 2);
        }
        hDist[count] = d;
        hTile[count] = tile;
        SiftUp(hDist, hTile, count);
        count++;
    }

    private static void SiftUp(double[] hDist, int[] hTile, int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (Less(hDist, hTile, i, parent))
            {
                (hDist[i], hDist[parent]) = (hDist[parent], hDist[i]);
                (hTile[i], hTile[parent]) = (hTile[parent], hTile[i]);
                i = parent;
            }
            else break;
        }
    }

    private static void SiftDown(double[] hDist, int[] hTile, int count, int i)
    {
        while (true)
        {
            int left = i * 2 + 1;
            int right = left + 1;
            int smallest = i;
            if (left < count && Less(hDist, hTile, left, smallest)) smallest = left;
            if (right < count && Less(hDist, hTile, right, smallest)) smallest = right;
            if (smallest == i) break;
            (hDist[i], hDist[smallest]) = (hDist[smallest], hDist[i]);
            (hTile[i], hTile[smallest]) = (hTile[smallest], hTile[i]);
            i = smallest;
        }
    }

    private static bool Less(double[] hDist, int[] hTile, int a, int b)
    {
        if (hDist[a] != hDist[b]) return hDist[a] < hDist[b];
        return hTile[a] < hTile[b];
    }
}
