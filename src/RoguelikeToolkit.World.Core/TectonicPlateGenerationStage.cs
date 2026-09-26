using System;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace RoguelikeToolkit.World.Core;

[WorldGeneratorStage(10, Writes = new[] { typeof(TectonicPlate) })]
public class TectonicPlateGenerationStage : IWorldGeneratorStage, ISeededStage, IDisposable
{
    public int SeedCount { get; set; } = 12;
    public int Seed { get; set; } = 42;

    /// <summary>Continentality above this is continental crust, else oceanic.</summary>
    public double CrustThreshold { get; set; } = 0.12;

    /// <summary>Convergence above this (in drift-velocity units) is convergent.</summary>
    public double ConvergenceThreshold { get; set; } = 0.08;

    /// <summary>BFS cap for the boundary-distance field, in neighbor rings.</summary>
    public int MaxBoundaryDistance { get; set; } = 8;

    /// <summary>
    /// Plate area strategy. Defaults to noisy flood-fill growth (organic
    /// borders); set to <see cref="VoronoiPlatePartitioner"/> for the legacy
    /// straight-edged Lloyd-relaxed Voronoi layout.
    /// </summary>
    public IPlatePartitioner Partitioner { get; set; } = new NoisyFloodFillPlatePartitioner();

    /// <summary>
    /// When true, the convergent uplift driver is modulated along strike by a
    /// seamless noise field, breaking uniform mountain walls into peaks and
    /// gaps. Disable for the legacy uniform-belt behavior.
    /// </summary>
    public bool SegmentOrogeny { get; set; } = true;

    /// <summary>Spatial frequency of the along-strike orogeny modulation.</summary>
    public double SegmentationFrequency { get; set; } = 2.6;

    /// <summary>
    /// Along-strike modulation depth: the driver scales by [1-S, 1+S].
    /// </summary>
    public double SegmentationStrength { get; set; } = 0.65;

    private readonly ArenaAllocator _arena;

    // Cached scratch so repeated Execute calls stay allocation-free.
    private int[] _plateIds = Array.Empty<int>();
    private Vector3D[] _seedCopy = Array.Empty<Vector3D>();
    private static readonly VoronoiPlatePartitioner FallbackPartitioner = new();

    public TectonicPlateGenerationStage()
    {
        _arena = ArenaDefaults.Create();
    }

    public TectonicPlateGenerationStage(int seedCount, int seed = 42, ArenaAllocator? arena = null)
    {
        SeedCount = seedCount;
        Seed = seed;
        _arena = arena ?? ArenaDefaults.Create();
    }

    public TectonicPlateGenerationStage(int seedCount, int seed, IPlatePartitioner partitioner)
        : this(seedCount, seed)
    {
        Partitioner = partitioner;
    }

    public void Execute(WorldMap map)
    {
        _arena.Reset();
        var store = map.DataStore;
        var span = store.GetSpan<TectonicPlate>();

        for (int i = 0; i < span.Length; i++)
        {
            span[i] = new TectonicPlate { Id = -1 };
        }

        var seeds = new ArenaList<Vector3D>(_arena, SeedCount);
        var seedElevations = new ArenaList<double>(_arena, SeedCount);
        var seedDriftSpeeds = new ArenaList<double>(_arena, SeedCount);

        // Per-plate sub-streams: each plate's parameters depend only on (Seed, plate
        // ordinal), never on iteration order, so output is reproducible on demand.
        var seedDriftDirs = new ArenaList<Vector3D>(_arena, SeedCount);
        for (int i = 0; i < SeedCount; i++)
        {
            var r = Rng.Create(Seed, i);
            double lat = r.NextDouble() * 180.0 - 90.0;
            double lon = r.NextDouble() * 360.0 - 180.0;
            seeds.Add(Vector3D.FromGeoCoord(new GeoCoord(lat, lon)));
            seedElevations.Add(r.NextDouble());
            seedDriftSpeeds.Add(r.NextDouble());

            // Uniform random unit vector (drawn after the legacy parameters so the
            // first four draws — and therefore existing plate layouts — are unchanged).
            double u = r.NextDouble() * 2.0 - 1.0;
            double theta = r.NextDouble() * 2.0 * Math.PI;
            double s = Math.Sqrt(Math.Max(0.0, 1.0 - u * u));
            seedDriftDirs.Add(new Vector3D(s * Math.Cos(theta), s * Math.Sin(theta), u));
        }

        ReadOnlySpan<Vector3D> tilePositions = store.GetTileVectors();

        // Plate areas come from the configured partition strategy (default:
        // noisy flood-fill growth for organic borders). The strategy writes
        // 0-based seed ordinals; per-plate parameters below stay pure
        // functions of (Seed, plate ordinal) for reproducibility.
        if (_plateIds.Length < store.TileCount) _plateIds = new int[store.TileCount];
        if (_seedCopy.Length < SeedCount) _seedCopy = new Vector3D[SeedCount];
        var plateIds = _plateIds;
        var seedArray = _seedCopy;
        for (int s = 0; s < SeedCount; s++) seedArray[s] = seeds[s];
        var seedSpan = new ReadOnlySpan<Vector3D>(seedArray, 0, SeedCount);
        var idSpan = new Span<int>(plateIds, 0, store.TileCount);
        (Partitioner ?? (IPlatePartitioner)FallbackPartitioner).Partition(store, tilePositions, seedSpan, idSpan, Seed);

        for (int i = 0; i < store.TileCount; i++)
        {
            int bestSeed = Math.Clamp(plateIds[i], 0, SeedCount - 1);
            var drift = seedDriftDirs[bestSeed];
            // Mixed plates: a plate-scale offset plus broad seamless swells, so
            // each plate carries both continental and oceanic tiles like Earth.
            double continentality = (seedElevations[bestSeed] - 0.45) * 1.2
                + SphereNoise.Fbm(tilePositions[i] * 1.6, Seed + 77) * 0.55;
            span[i] = new TectonicPlate
            {
                Id = bestSeed + 1,
                Elevation = seedElevations[bestSeed],
                DriftSpeed = seedDriftSpeeds[bestSeed],
                DriftX = drift.X,
                DriftY = drift.Y,
                DriftZ = drift.Z,
                Crust = continentality > CrustThreshold ? CrustType.Continental : CrustType.Oceanic,
                Boundary = PlateBoundaryType.None,
                Continentality = (float)continentality,
                BoundaryDistance = float.MaxValue,
                NearestBoundary = PlateBoundaryType.None,
                Orogeny = 0f
            };
        }

        // Hypsometric calibration: plate offsets are arbitrary, so recenter the
        // field to a fixed mean. Land fraction then stays Earth-like for every
        // seed instead of swinging with the plate draw.
        double contMean = 0;
        for (int i = 0; i < store.TileCount; i++) contMean += span[i].Continentality;
        contMean /= store.TileCount;
        float shift = (float)(contMean - (-0.10));
        for (int i = 0; i < store.TileCount; i++)
        {
            float c = span[i].Continentality - shift;
            span[i].Continentality = c;
            span[i].Crust = c > CrustThreshold ? CrustType.Continental : CrustType.Oceanic;
        }

        ClassifyBoundaries(store, span, tilePositions, ConvergenceThreshold);
        PropagateOrogeny(store, span, _arena, MaxBoundaryDistance);
    }

    /// <summary>
    /// Along-strike modulation in [1-S, 1+S]: breaks a uniform convergent
    /// wall into peaks and saddles. Pure function of position and seed.
    /// </summary>
    internal double SegmentFactor(Vector3D pos)
    {
        if (!SegmentOrogeny) return 1.0;
        double strength = Math.Clamp(SegmentationStrength, 0.0, 1.0);
        double x = SphereNoise.Fbm(pos * SegmentationFrequency, Seed + 4242) * 0.5 + 0.5;
        return 1.0 + strength * (2.0 * x - 1.0);
    }

    private void ClassifyBoundaries(
        WorldDataStore store, Span<TectonicPlate> span, ReadOnlySpan<Vector3D> positions, double threshold)
    {
        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < store.TileCount; i++)
        {
            var plate = span[i];
            int adjacent = store.GetAdjacent(i, neighbors);
            bool hasForeign = false;
            double maxConvergence = double.NegativeInfinity;
            double minConvergence = double.PositiveInfinity;
            // Winning convergent pair (for subduction polarity + belt width).
            CrustType acrossCrust = plate.Crust;
            for (int k = 0; k < adjacent; k++)
            {
                int j = neighbors[k];
                var other = span[j];
                if (other.Id == plate.Id) continue;
                hasForeign = true;
                var dir = (positions[j] - positions[i]).Normalize();
                double relX = other.DriftX * other.DriftSpeed - plate.DriftX * plate.DriftSpeed;
                double relY = other.DriftY * other.DriftSpeed - plate.DriftY * plate.DriftSpeed;
                double relZ = other.DriftZ * other.DriftSpeed - plate.DriftZ * plate.DriftSpeed;
                double convergence = -(relX * dir.X + relY * dir.Y + relZ * dir.Z);
                if (convergence > maxConvergence) { maxConvergence = convergence; acrossCrust = other.Crust; }
                if (convergence < minConvergence) minConvergence = convergence;
            }
            if (!hasForeign)
            {
                plate.Boundary = PlateBoundaryType.None;
                plate.Orogeny = 0f;
            }
            else if (maxConvergence > threshold)
            {
                plate.Boundary = PlateBoundaryType.Convergent;
                plate.Orogeny = (float)(ConvergentDriver(plate.Crust, acrossCrust, maxConvergence) * SegmentFactor(positions[i]));
            }
            else if (minConvergence < -threshold)
            {
                plate.Boundary = PlateBoundaryType.Divergent;
                // Mid-ocean ridge (mostly submarine) vs continental rift valley.
                plate.Orogeny = plate.Crust == CrustType.Oceanic ? 0.5f : -0.55f;
            }
            else
            {
                plate.Boundary = PlateBoundaryType.Transform;
                plate.Orogeny = 0.12f;
            }
            span[i] = plate;
        }
    }

    /// <summary>
    /// Signed convergent driver with subduction polarity: the oceanic side dives
    /// (trench, negative) while the overriding side piles up (arc/orogen, positive).
    /// </summary>
    internal static double ConvergentDriver(CrustType mine, CrustType across, double convergence)
    {
        if (mine == CrustType.Continental && across == CrustType.Oceanic)
            return convergence * 1.8; // Andes-style arc
        if (mine == CrustType.Oceanic && across == CrustType.Continental)
            return -convergence * 1.0; // trench side dives
        if (mine == CrustType.Continental && across == CrustType.Continental)
            return convergence * 2.4; // Himalaya-style fold belt
        return convergence * 1.4; // island arc
    }

    /// <summary>
    /// Orogenic belt width in neighbor rings: continent-continent collision builds
    /// the widest plateaus, trenches and transforms stay narrow.
    /// </summary>
    internal static double BeltWidth(PlateBoundaryType boundary, float driver, CrustType mine, CrustType across)
    {
        return boundary switch
        {
            PlateBoundaryType.Convergent when driver < 0 => 0.8, // trench: narrow, deep
            PlateBoundaryType.Convergent when mine == CrustType.Continental && across == CrustType.Continental => 2.8,
            PlateBoundaryType.Convergent when mine == CrustType.Continental => 1.9, // Andean arc
            PlateBoundaryType.Convergent => 1.5, // island arc
            PlateBoundaryType.Divergent when driver < 0 => 1.3, // rift valley
            PlateBoundaryType.Divergent => 1.1, // mid-ocean ridge
            PlateBoundaryType.Transform => 0.8,
            _ => 1.0,
        };
    }

    /// <summary>
    /// Multi-source BFS from every boundary tile: each tile learns its distance to
    /// the nearest boundary, that boundary's type, and the orogeny driver decayed
    /// by exp(-d/w). Deterministic (index-ordered seeding, sorted neighbors) and
    /// allocation-free (all scratch lives in the stage arena).
    /// </summary>
    private static void PropagateOrogeny(
        WorldDataStore store, Span<TectonicPlate> span, ArenaAllocator arena, int maxDistance)
    {
        int n = store.TileCount;
        var dist = new ArenaList<int>(arena, n);
        var srcDriver = new ArenaList<float>(arena, n);
        var srcWidth = new ArenaList<float>(arena, n);
        var srcType = new ArenaList<byte>(arena, n);
        var queue = new ArenaList<int>(arena, n);
        for (int i = 0; i < n; i++)
        {
            dist.Add(-1);
            srcDriver.Add(0f);
            srcWidth.Add(1f);
            srcType.Add((byte)PlateBoundaryType.None);
        }

        // Seed in index order so ties resolve deterministically.
        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < n; i++)
        {
            if (span[i].Boundary == PlateBoundaryType.None) continue;
            dist[i] = 0;
            srcDriver[i] = span[i].Orogeny;
            srcType[i] = (byte)span[i].Boundary;
            srcWidth[i] = (float)SourceWidth(store, span, i, neighbors);
            queue.Add(i);
        }

        int head = 0;
        while (head < queue.Length)
        {
            int cur = queue[head++];
            int nd = dist[cur] + 1;
            if (nd > maxDistance) continue;
            int adjacent = store.GetAdjacent(cur, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int nb = neighbors[k];
                if (dist[nb] >= 0) continue;
                dist[nb] = nd;
                srcDriver[nb] = srcDriver[cur];
                srcWidth[nb] = srcWidth[cur];
                srcType[nb] = srcType[cur];
                queue.Add(nb);
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (dist[i] < 0)
            {
                span[i].BoundaryDistance = float.MaxValue;
                span[i].NearestBoundary = PlateBoundaryType.None;
                span[i].Orogeny = 0f;
                continue;
            }
            double decay = Math.Exp(-dist[i] / Math.Max(0.25, (double)srcWidth[i]));
            span[i].BoundaryDistance = dist[i];
            span[i].NearestBoundary = (PlateBoundaryType)srcType[i];
            span[i].Orogeny = (float)(srcDriver[i] * decay);
        }
    }

    private static double SourceWidth(WorldDataStore store, Span<TectonicPlate> span, int i, Span<int> neighbors)
    {
        var plate = span[i];
        if (plate.Boundary != PlateBoundaryType.Convergent)
            return BeltWidth(plate.Boundary, plate.Orogeny, plate.Crust, plate.Crust);
        // Convergent width needs the across-strike crust; reuse the winning pair
        // convention from classification (first max-convergence foreign neighbor).
        int adjacent = store.GetAdjacent(i, neighbors);
        CrustType across = plate.Crust;
        double best = double.NegativeInfinity;
        for (int k = 0; k < adjacent; k++)
        {
            int j = neighbors[k];
            var other = span[j];
            if (other.Id == plate.Id) continue;
            // Recompute convergence cheaply from stored drift (positions cancel out
            // of the argmax only if directions align; use the true projection).
            var dir = (store.GetTileVectors()[j] - store.GetTileVectors()[i]).Normalize();
            double relX = other.DriftX * other.DriftSpeed - plate.DriftX * plate.DriftSpeed;
            double relY = other.DriftY * other.DriftSpeed - plate.DriftY * plate.DriftSpeed;
            double relZ = other.DriftZ * other.DriftSpeed - plate.DriftZ * plate.DriftSpeed;
            double convergence = -(relX * dir.X + relY * dir.Y + relZ * dir.Z);
            if (convergence > best) { best = convergence; across = other.Crust; }
        }
        return BeltWidth(plate.Boundary, plate.Orogeny, plate.Crust, across);
    }

    public void Dispose()
    {
        _arena.Dispose();
    }
}
