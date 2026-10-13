using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Generation;

namespace RoguelikeToolkit.World.Core.Tectonics;

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
    public double Warp { get => _warp; set => _warp = StageKnob.Finite(value, nameof(Warp)); }
    private double _warp = 0.8;

    /// <summary>Spatial frequency of the coarsest warp octave (unit-sphere vectors). Higher means more, smaller wiggles.</summary>
    public double NoiseFrequency { get => _noiseFrequency; set => _noiseFrequency = StageKnob.Finite(value, nameof(NoiseFrequency)); }
    private double _noiseFrequency = 1.5;

    /// <summary>Warp octaves. Each adds a finer scale of wiggle at half the amplitude (fractal borders).</summary>
    public int Octaves { get => _octaves; set => _octaves = StageKnob.NonNegative(value, nameof(Octaves)); }
    private int _octaves = 6;

    /// <summary>Slowest plate growth speed. Default 0.6.</summary>
    public double MinSpeed { get => _minSpeed; set => _minSpeed = StageKnob.Finite(value, nameof(MinSpeed)); }
    private double _minSpeed = 0.6;

    /// <summary>
    /// Spread between the slowest and fastest growth speed: each plate grows at <see cref="MinSpeed"/> plus this times a
    /// per-plate draw in [0,1). Default 1.4 (speeds 0.6 to 2.0). A plate's area scales roughly with speed to the power 2.4
    /// (measured over 96 plates, r = 0.73), so the default spread gives plates from under 1% to over 25% of the globe; a
    /// narrower spread such as 0.5 keeps plates within a small factor of one another. Must be at least 0.
    /// </summary>
    public double SpeedRange
    {
        get => _speedRange;
        set => _speedRange = StageKnob.Finite(value, nameof(SpeedRange)) < 0
            ? throw new ArgumentOutOfRangeException(nameof(SpeedRange), value, "SpeedRange cannot be negative (use 0 for equal speeds).")
            : value;
    }
    private double _speedRange = 1.4;

    // Cached scratch: allocated once per tile-count shape so repeated
    // Execute calls stay allocation-free (steady-state zero-GC).
    private double[] _speeds = Array.Empty<double>();
    private Vector3D[] _warped = Array.Empty<Vector3D>();
    private double[] _dist = Array.Empty<double>();
    private int[] _owner = Array.Empty<int>();
    private double[] _hDist = Array.Empty<double>();
    private int[] _hTile = Array.Empty<int>();

    /// <summary>Assigns each tile to the plate whose warped-space front reaches it first.</summary>
    /// <param name="store">Provides tile count and adjacency.</param>
    /// <param name="positions">Unit-sphere position of every tile.</param>
    /// <param name="seeds">Unit-sphere position of each plate seed; at least one.</param>
    /// <param name="plateIds">Receives the 0-based seed ordinal per tile (tiles never reached get 0); must be at least as long as the tile count.</param>
    /// <param name="seed">Seed for per-plate growth speeds and the warp field.</param>
    /// <exception cref="ArgumentException"><paramref name="seeds"/> is empty, or <paramref name="plateIds"/> is shorter than the tile count.</exception>
    /// <remarks>Reuses internal scratch buffers between calls, so an instance must not be used from several threads at once.</remarks>
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
            _speeds[s] = MinSpeed + SpeedRange * Rng.Create(seed + 5000, s).NextDouble();

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
