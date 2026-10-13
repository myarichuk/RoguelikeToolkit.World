using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tectonics;

/// <summary>Generates the plate layout: partitions the sphere into plates with random drift, assigns continentality and crust, classifies plate boundaries by relative drift, and propagates the orogeny driver outwards from them. Writes <see cref="TectonicPlate"/> and fills the <see cref="PlateInfo"/> table.</summary>
[Preserve] // created via reflection by WorldGenerationPipeline.Discover; keep it through Unity code stripping
[WorldGeneratorStage(StageOrder.Tectonics, Writes = new[] { typeof(TectonicPlate) })]
public class TectonicPlateGenerationStage : IWorldGeneratorStage, ISeededStage, IDisposable
{
    /// <summary>Number of plates, 1 .. <see cref="TectonicPlate.MaxPlates"/>; must not exceed the rows reserved by <see cref="TectonicPlateLayer"/>. Default 12.</summary>
    public int SeedCount { get; set; } = 12;
    /// <summary>Seeds plate positions, drift, partitioning and noise; per-plate draws depend only on the seed and the plate's ordinal. Default 42.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Continentality above this is continental crust, else oceanic.</summary>
    public double CrustThreshold { get; set; } = 0.12;

    /// <summary>Convergence above this (in drift-velocity units) is convergent.</summary>
    public double ConvergenceThreshold { get; set; } = 0.08;

    /// <summary>
    /// When true, boundaries are classified on the whole relative motion rather than on its component across the border
    /// alone, and the switch between boundary kinds is a ramp instead of a step. A boundary whose plates mostly slide past
    /// one another (shear well above the across-border motion) is a transform fault however large its small convergent
    /// component is, and two plates that barely move relative to each other leave only a faint scar instead of the full
    /// fault ridge. The driver blends from the transform value to the convergent or divergent one over
    /// <see cref="ConvergenceThreshold"/> to three times that, where the legacy classification jumps from a ridge of 0.12
    /// to a rift valley of -0.55 across a single threshold. Off by default: it moves every boundary's relief.
    /// </summary>
    public bool ShearTransform { get; set; }

    /// <summary>
    /// When true, the colliding plates of an oceanic-oceanic boundary get a polarity instead of the same arc on both sides:
    /// the plate with the lower base elevation draw (the older, denser one; the lower plate id on a tie) dives and takes the
    /// trench, the other keeps the island arc. Off by default: it moves the relief of every oceanic convergent border.
    /// </summary>
    public bool OceanicPolarity { get; set; }

    /// <summary>
    /// BFS cap for the boundary-distance field, in neighbor rings. Kept small
    /// on purpose: a plate at the default 12-plate density is only ~4 rings in
    /// radius, so a wider cap lets every boundary's uplift apron meet in the
    /// plate interior and paints whole plates as mountain belts.
    /// </summary>
    public int MaxBoundaryDistance { get; set; } = 4;

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

    /// <summary>
    /// Strike-gap floor: when the along-strike noise field falls below this
    /// (in [0,1] field units), the convergent driver is set to exactly zero
    /// instead of merely weakening. Without true zeros a modulated wall is
    /// still a wall — one connected mountain blurb along the whole boundary.
    /// Gaps break it into discrete ranges with passes between them.
    /// </summary>
    public double SegmentGapFloor { get; set; } = 0.25;

    /// <summary>
    /// When true, plate seeds are spread evenly over the sphere instead of drawn with uniform latitude, and a seed that
    /// lands too close to an earlier plate's is redrawn. Uniform latitude puts 28% of seeds above 60 degrees (a uniform
    /// sphere has 13%), which crowds the poles with slivers. Off by default: it moves every seed's plate layout.
    /// Plate <c>i</c> then depends on plates <c>0..i-1</c> as well as on <see cref="Seed"/>.
    /// </summary>
    public bool BalancedSeeding { get; set; }

    /// <summary>Minimum seed separation for <see cref="BalancedSeeding"/>, as a fraction of the even-spacing arc <c>sqrt(4π/plates)</c>; it relaxes slightly with each redraw so placement always terminates. Default 0.75.</summary>
    public double SeedSpacing { get => _seedSpacing; set => _seedSpacing = StageKnob.Finite(value, nameof(SeedSpacing)); }
    private double _seedSpacing = 0.75;

    /// <summary>
    /// Weight of a plate's own continentality offset (<c>seedElevation - 0.45</c>) in the crust field. Default 1.2, which
    /// dwarfs <see cref="CrustNoiseWeight"/> and makes most plates entirely land or entirely sea, so coastlines follow plate
    /// borders. Lower values let the noise decide where the coast runs inside a plate.
    /// </summary>
    public double PlateOffsetWeight { get => _plateOffsetWeight; set => _plateOffsetWeight = StageKnob.Finite(value, nameof(PlateOffsetWeight)); }
    private double _plateOffsetWeight = 1.2;

    /// <summary>Weight of the broad noise swells in the crust field. Default 0.55.</summary>
    public double CrustNoiseWeight { get => _crustNoiseWeight; set => _crustNoiseWeight = StageKnob.Finite(value, nameof(CrustNoiseWeight)); }
    private double _crustNoiseWeight = 0.55;

    /// <summary>
    /// Amplitude, in unit-sphere lengths, of a fractal displacement applied to the finished crust field before it is
    /// thresholded: each tile takes the crust value of the tile its displaced position lands on. The plate offset is a hard
    /// step along every border, and blurring a step only gives a smooth curve that still traces the border; displacing it
    /// detaches coastline and shelf from the plate outline. 0 (default) disables it.
    /// </summary>
    public double CrustWarp { get => _crustWarp; set => _crustWarp = StageKnob.Finite(value, nameof(CrustWarp)); }
    private double _crustWarp;

    /// <summary>Spatial frequency of the coarsest <see cref="CrustWarp"/> octave. Default 1.2.</summary>
    public double CrustWarpFrequency { get => _crustWarpFrequency; set => _crustWarpFrequency = StageKnob.Finite(value, nameof(CrustWarpFrequency)); }
    private double _crustWarpFrequency = 1.2;

    /// <summary>Neighbour-averaging passes over the crust field after <see cref="CrustWarp"/>, rounding off what is left of the step. 0 (default) disables it.</summary>
    public int CrustBlurPasses { get => _crustBlurPasses; set => _crustBlurPasses = StageKnob.NonNegative(value, nameof(CrustBlurPasses)); }
    private int _crustBlurPasses;

    /// <summary>
    /// Share of tiles that end up as continental crust (continentality above <see cref="CrustThreshold"/>), set exactly by
    /// a quantile of the crust field. Continental crust is what rides above sea level: the elevation stage lowers every
    /// oceanic tile by 0.1, so this share is within a few points of the land share (orogeny, hotspots and erosion move it
    /// slightly). 0 (default) keeps the legacy calibration, which recentres the field to a fixed mean instead: that fixes
    /// the mean but not the share, so changing <see cref="PlateOffsetWeight"/> or the crust warp moves how much land there
    /// is (37.6% to 26.5% over 12 seeds when the offset weight went from 1.2 to 0.6). Must be in [0, 1).
    /// </summary>
    public double LandFraction
    {
        get => _landFraction;
        set => _landFraction = StageKnob.Finite(value, nameof(LandFraction)) is < 0 or >= 1
            ? throw new ArgumentOutOfRangeException(nameof(LandFraction), value, "LandFraction must be in [0, 1) (0 keeps the legacy mean calibration).")
            : value;
    }
    private double _landFraction;

    /// <summary>
    /// Stretches the calibrated crust field away from <see cref="CrustThreshold"/>: continental tiles ride higher and
    /// oceanic tiles sit deeper by this factor, and the continental share is unchanged. Needed because a smaller
    /// <see cref="PlateOffsetWeight"/> narrows the field (std 0.33 to 0.19), which flattens continents and parks the sea
    /// floor near sea level where erosion fills it. Default 1 (no change).
    /// </summary>
    public double CrustContrast { get => _crustContrast; set => _crustContrast = StageKnob.Finite(value, nameof(CrustContrast)); }
    private double _crustContrast = 1.0;

    /// <summary>
    /// <see cref="CrustContrast"/> for the oceanic side of the threshold. Deepening the sea floor on its own keeps volcanic
    /// arcs and ridges built on oceanic crust under water, so oceanic plate borders stop raising long thin ribbons of land,
    /// without lifting the continents. Default 1 (no change).
    /// </summary>
    public double OceanContrast { get => _oceanContrast; set => _oceanContrast = StageKnob.Finite(value, nameof(OceanContrast)); }
    private double _oceanContrast = 1.0;

    /// <summary>
    /// When true, the plates that carry continents are chosen as the heaviest set of plates that do not touch one another
    /// (weighted by area and by each plate's drawn elevation), instead of letting every plate's random offset decide on its
    /// own. Independent offsets leave continental plates adjacent more often than not, and adjacent continental plates fuse:
    /// the largest landmass held 79% of the land over 15 seeds at size 6 (96% on some), against 44% with this on. Chosen plates
    /// get a bias of +0.35 and the rest -0.35 (in units of the plate offset) on top of their own draw, and the quantile
    /// calibration then lays the coast out inside and around them. Off by default.
    /// </summary>
    public bool DisperseContinents { get; set; }

    private const double ContinentBias = 0.35;

    // Cached scratch so repeated Execute calls stay allocation-free: buffers grow
    // to the largest map/plate count seen and are reused (never shrunk) after that.
    private int[] _plateIds = Array.Empty<int>();
    private Vector3D[] _seedCopy = Array.Empty<Vector3D>();
    private double[] _seedElevations = Array.Empty<double>();
    private float[] _crustRaw = Array.Empty<float>();
    private float[] _crustTmp = Array.Empty<float>();
    private bool[] _continental = Array.Empty<bool>();
    private bool[] _touches = Array.Empty<bool>();
    private int[] _plateArea = Array.Empty<int>();
    private readonly OrogenyScratch _orogeny = new();
    private static readonly VoronoiPlatePartitioner FallbackPartitioner = new();

    /// <summary>Creates a stage with the default 12 plates and seed 42; this is the constructor <see cref="WorldGenerationPipeline.Discover(string?, Action{StageDiscoveryDiagnostic}?)"/> uses.</summary>
    public TectonicPlateGenerationStage()
    {
    }

    /// <summary>Creates a stage with the given plate count and seed.</summary>
    /// <param name="seedCount">Value for <see cref="SeedCount"/>.</param>
    /// <param name="seed">Value for <see cref="Seed"/>.</param>
    public TectonicPlateGenerationStage(int seedCount, int seed = 42)
    {
        SeedCount = seedCount;
        Seed = seed;
    }

    /// <summary>Creates a stage with the given plate count, seed and partitioner.</summary>
    /// <param name="seedCount">Value for <see cref="SeedCount"/>.</param>
    /// <param name="seed">Value for <see cref="Seed"/>.</param>
    /// <param name="partitioner">Value for <see cref="Partitioner"/>.</param>
    public TectonicPlateGenerationStage(int seedCount, int seed, IPlatePartitioner partitioner)
        : this(seedCount, seed)
    {
        Partitioner = partitioner;
    }

    /// <summary>Regenerates the plate table and the <see cref="TectonicPlate"/> layer of <paramref name="map"/>.</summary>
    /// <exception cref="InvalidOperationException">The store has no <see cref="PlateInfo"/> table with at least <see cref="SeedCount"/> rows, or <see cref="SeedCount"/> is outside 1 .. <see cref="TectonicPlate.MaxPlates"/>.</exception>
    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var span = store.GetSpan<TectonicPlate>();
        if (!store.IsTableRegistered<PlateInfo>() || store.GetTable<PlateInfo>().Length < SeedCount)
            throw new InvalidOperationException(
                $"The store has no plate table for {SeedCount} plates. Create the TectonicPlateLayer (reserves max(seedCount, 256) rows) before Allocate().");
        if (SeedCount is < 1 or > TectonicPlate.MaxPlates)
            throw new InvalidOperationException($"SeedCount must be 1..{TectonicPlate.MaxPlates}.");
        var table = store.GetTable<PlateInfo>();
        table.Clear();
        span.Clear();

        Grow(ref _seedCopy, SeedCount);
        Grow(ref _seedElevations, SeedCount);
        var seeds = _seedCopy;
        var seedElevations = _seedElevations;

        // Per-plate sub-streams: each plate's parameters depend only on (Seed, plate
        // ordinal), never on iteration order, so output is reproducible on demand.
        for (int i = 0; i < SeedCount; i++)
        {
            var r = Rng.Create(Seed, i);
            double latDraw = r.NextDouble();
            double lonDraw = r.NextDouble();
            seeds[i] = BalancedSeeding
                ? BalancedSeed(i, latDraw, lonDraw, seeds)
                : Vector3D.FromGeoCoord(new GeoCoord(latDraw * 180.0 - 90.0, lonDraw * 360.0 - 180.0));
            seedElevations[i] = r.NextDouble();
            double driftSpeed = r.NextDouble();

            // Uniform random unit vector (drawn after the legacy parameters so the
            // first four draws — and therefore existing plate layouts — are unchanged).
            double u = r.NextDouble() * 2.0 - 1.0;
            double theta = r.NextDouble() * 2.0 * Math.PI;
            double s = Math.Sqrt(Math.Max(0.0, 1.0 - u * u));
            var dir = new Vector3D(s * DetMath.Cos(theta), s * DetMath.Sin(theta), u);
            table[i] = new PlateInfo
            {
                Elevation = seedElevations[i],
                DriftSpeed = driftSpeed,
                DriftX = dir.X,
                DriftY = dir.Y,
                DriftZ = dir.Z,
            };
        }

        ReadOnlySpan<Vector3D> tilePositions = store.GetTileVectors();

        // Plate areas come from the configured partition strategy (default:
        // noisy flood-fill growth for organic borders). The strategy writes
        // 0-based seed ordinals; per-plate parameters below stay pure
        // functions of (Seed, plate ordinal) for reproducibility.
        Grow(ref _plateIds, store.TileCount);
        var plateIds = _plateIds;
        var seedSpan = new ReadOnlySpan<Vector3D>(seeds, 0, SeedCount);
        var idSpan = new Span<int>(plateIds, 0, store.TileCount);
        (Partitioner ?? (IPlatePartitioner)FallbackPartitioner).Partition(store, tilePositions, seedSpan, idSpan, Seed);

        if (DisperseContinents) ChooseContinentalPlates(store, plateIds, seedElevations);

        for (int i = 0; i < store.TileCount; i++)
        {
            int bestSeed = Math.Clamp(plateIds[i], 0, SeedCount - 1);
            double plateTerm = DisperseContinents
                ? ((seedElevations[bestSeed] - 0.45) + (_continental[bestSeed] ? ContinentBias : -ContinentBias)) * PlateOffsetWeight
                : (seedElevations[bestSeed] - 0.45) * PlateOffsetWeight;
            // Mixed plates: a plate-scale offset plus broad seamless swells, so
            // each plate carries both continental and oceanic tiles like Earth.
            double continentality = plateTerm
                + SphereNoise.Fbm(tilePositions[i] * 1.6, Seed + 77) * CrustNoiseWeight;
            span[i] = new TectonicPlate
            {
                Id = (ushort)(bestSeed + 1),
                Crust = continentality > CrustThreshold ? CrustType.Continental : CrustType.Oceanic,
                Boundary = PlateBoundaryType.None,
                Continentality = (float)continentality,
                BoundaryRings = byte.MaxValue,
                NearestBoundary = PlateBoundaryType.None,
                Orogeny = 0f
            };
        }

        if (CrustWarp > 0.0 || CrustBlurPasses > 0) ReshapeCrust(store, span, tilePositions);

        // Hypsometric calibration: plate offsets are arbitrary, so recenter the
        // field to a fixed mean. Land fraction then stays Earth-like for every
        // seed instead of swinging with the plate draw.
        float shift;
        if (LandFraction > 0.0)
        {
            // Quantile calibration: continental crust is exactly the top LandFraction of tiles by continentality.
            int count = store.TileCount;
            Grow(ref _crustRaw, count);
            for (int i = 0; i < count; i++) _crustRaw[i] = span[i].Continentality;
            Array.Sort(_crustRaw, 0, count);
            int cut = Math.Clamp((int)Math.Floor((1.0 - LandFraction) * count), 0, count - 1);
            shift = (float)(_crustRaw[cut] - CrustThreshold);
        }
        else
        {
            double contMean = 0;
            for (int i = 0; i < store.TileCount; i++) contMean += span[i].Continentality;
            contMean /= store.TileCount;
            shift = (float)(contMean - (-0.10));
        }
        for (int i = 0; i < store.TileCount; i++)
        {
            float c = span[i].Continentality - shift;
            double contrast = c > CrustThreshold ? CrustContrast : OceanContrast;
            if (contrast != 1.0) c = (float)(CrustThreshold + (c - CrustThreshold) * contrast);
            span[i].Continentality = c;
            span[i].Crust = c > CrustThreshold ? CrustType.Continental : CrustType.Oceanic;
        }

        ClassifyBoundaries(store, span, table, tilePositions, ConvergenceThreshold);
        PropagateOrogeny(store, span, table, _orogeny, MaxBoundaryDistance);
    }

    /// <summary>
    /// Marks which plates carry continents under <see cref="DisperseContinents"/>: the heaviest set of plates, no two of which
    /// share a border.
    /// </summary>
    private void ChooseContinentalPlates(WorldDataStore store, int[] plateIds, double[] seedElevations)
    {
        int plates = SeedCount;
        if (_continental.Length < plates) _continental = new bool[plates];
        if (_plateArea.Length < plates) _plateArea = new int[plates];
        if (_touches.Length < plates * plates) _touches = new bool[plates * plates];
        Array.Clear(_continental, 0, plates);
        Array.Clear(_plateArea, 0, plates);
        Array.Clear(_touches, 0, plates * plates);

        Span<int> nb = stackalloc int[6];
        int count = store.TileCount;
        for (int i = 0; i < count; i++)
        {
            int a = Math.Clamp(plateIds[i], 0, plates - 1);
            _plateArea[a]++;
            int adjacent = store.GetAdjacent(i, nb);
            for (int k = 0; k < adjacent; k++)
            {
                int b = Math.Clamp(plateIds[nb[k]], 0, plates - 1);
                if (a != b) { _touches[a * plates + b] = true; _touches[b * plates + a] = true; }
            }
        }

        // Heaviest set of mutually non-touching plates, weighting each by area and by its drawn elevation so the pick is
        // seeded rather than fixed by the layout. Exhaustive search with a node budget (the budget only bites past ~40
        // plates, and the best set found so far is kept, so the result stays a pure function of the draws).
        int usable = Math.Min(plates, 62);
        var weight = new double[usable];
        var touchMask = new ulong[usable];
        for (int p = 0; p < usable; p++)
        {
            weight[p] = _plateArea[p] * (0.75 + 0.5 * seedElevations[p]);
            for (int q = 0; q < usable; q++)
                if (_touches[p * plates + q]) touchMask[p] |= 1UL << q;
        }
        var suffix = new double[usable + 1];
        for (int p = usable - 1; p >= 0; p--) suffix[p] = suffix[p + 1] + weight[p];
        ulong best = 0;
        double bestWeight = -1;
        int budget = 400_000;
        void Search(int p, ulong taken, double sum)
        {
            if (sum > bestWeight) { bestWeight = sum; best = taken; }
            if (p == usable || budget-- <= 0 || sum + suffix[p] <= bestWeight) return;
            if ((touchMask[p] & taken) == 0) Search(p + 1, taken | (1UL << p), sum + weight[p]);
            Search(p + 1, taken, sum);
        }
        Search(0, 0UL, 0.0);
        double have = 0;
        for (int p = 0; p < usable; p++)
            if ((best & (1UL << p)) != 0) { _continental[p] = true; have += _plateArea[p]; }
        // Never leave the world without a continent (a single plate has nothing to touch, so this is only a safeguard).
        if (have == 0) _continental[0] = true;
    }

    /// <summary>
    /// Seed position for plate <paramref name="i"/> under <see cref="BalancedSeeding"/>: uniform on the sphere (latitude
    /// from the arcsine of a uniform draw), redrawn from fresh per-plate sub-streams while it sits closer than the minimum
    /// spacing to an earlier seed. The spacing relaxes 0.5% per redraw, so 200 attempts always end in a placement.
    /// </summary>
    private Vector3D BalancedSeed(int i, double latDraw, double lonDraw, Vector3D[] placed)
    {
        double minArc = SeedSpacing * Math.Sqrt(4.0 * Math.PI / SeedCount);
        Vector3D candidate = default;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (attempt > 0)
            {
                var retry = Rng.Create(Seed + 9100, i * 256 + attempt);
                latDraw = retry.NextDouble();
                lonDraw = retry.NextDouble();
            }
            double z = latDraw * 2.0 - 1.0;
            double ring = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
            double lon = lonDraw * 2.0 * Math.PI;
            candidate = new Vector3D(ring * DetMath.Cos(lon), ring * DetMath.Sin(lon), z);

            double cosMin = DetMath.Cos(Math.Min(minArc, Math.PI));
            bool clear = true;
            for (int k = 0; k < i && clear; k++)
                if (Vector3D.Dot(candidate, placed[k]) > cosMin) clear = false;
            if (clear) break;
            minArc *= 0.995;
        }
        return candidate;
    }

    /// <summary>
    /// Detaches the coastline from the plate outline: every tile takes the crust value found at its displaced position
    /// (fractal displacement of <see cref="CrustWarp"/>), then <see cref="CrustBlurPasses"/> neighbour-averaging passes round
    /// off the remaining step. The displaced position is resolved by walking the adjacency graph from the tile toward it,
    /// so it needs no spatial index.
    /// </summary>
    private void ReshapeCrust(WorldDataStore store, Span<TectonicPlate> span, ReadOnlySpan<Vector3D> positions)
    {
        int n = store.TileCount;
        Grow(ref _crustRaw, n);
        Grow(ref _crustTmp, n);
        var raw = _crustRaw;
        var tmp = _crustTmp;
        for (int i = 0; i < n; i++) raw[i] = span[i].Continentality;

        Span<int> nb = stackalloc int[6];
        if (CrustWarp > 0.0)
        {
            for (int i = 0; i < n; i++)
            {
                var q = positions[i] * CrustWarpFrequency;
                var target = (positions[i] + new Vector3D(
                    SphereNoise.Fbm(q, Seed + 7100, 4),
                    SphereNoise.Fbm(q, Seed + 7200, 4),
                    SphereNoise.Fbm(q, Seed + 7300, 4)) * CrustWarp).Normalize();
                int cur = i;
                double best = Vector3D.Dot(positions[cur], target);
                for (int step = 0; step < 4096; step++)
                {
                    int adjacent = store.GetAdjacent(cur, nb);
                    int next = cur;
                    for (int k = 0; k < adjacent; k++)
                    {
                        double d = Vector3D.Dot(positions[nb[k]], target);
                        if (d > best) { best = d; next = nb[k]; }
                    }
                    if (next == cur) break;
                    cur = next;
                }
                tmp[i] = raw[cur];
            }
            (raw, tmp) = (tmp, raw);
        }

        for (int pass = 0; pass < CrustBlurPasses; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                int adjacent = store.GetAdjacent(i, nb);
                float sum = 0f;
                for (int k = 0; k < adjacent; k++) sum += raw[nb[k]];
                tmp[i] = adjacent == 0 ? raw[i] : 0.5f * raw[i] + 0.5f * (sum / adjacent);
            }
            (raw, tmp) = (tmp, raw);
        }

        for (int i = 0; i < n; i++) span[i].Continentality = raw[i];
    }

    /// <summary>
    /// Along-strike modulation: the [0,1] noise field below
    /// <see cref="SegmentGapFloor"/> maps to exactly 0 (a pass with no
    /// uplift), the remainder rescales to [1-S, 1+S] (peaks and saddles).
    /// Pure function of position and seed.
    /// </summary>
    internal double SegmentFactor(Vector3D pos)
    {
        if (!SegmentOrogeny) return 1.0;
        double strength = Math.Clamp(SegmentationStrength, 0.0, 1.0);
        double floor = Math.Clamp(SegmentGapFloor, 0.0, 0.9);
        double x = SphereNoise.Fbm(pos * SegmentationFrequency, Seed + 4242) * 0.5 + 0.5;
        if (x < floor) return 0.0;
        double t = (x - floor) / (1.0 - floor);
        return (1.0 - strength) + t * 2.0 * strength;
    }

    private void ClassifyBoundaries(
        WorldDataStore store, Span<TectonicPlate> span, ReadOnlySpan<PlateInfo> table, ReadOnlySpan<Vector3D> positions, double threshold)
    {
        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < store.TileCount; i++)
        {
            var plate = span[i];
            var mine = table[plate.Id - 1];
            int adjacent = store.GetAdjacent(i, neighbors);
            bool hasForeign = false;
            double maxConvergence = double.NegativeInfinity;
            double minConvergence = double.PositiveInfinity;
            double maxShear = 0.0, minShear = 0.0;
            // Winning convergent pair (for subduction polarity + belt width).
            CrustType acrossCrust = plate.Crust;
            int acrossPlate = plate.Id;
            for (int k = 0; k < adjacent; k++)
            {
                int j = neighbors[k];
                var other = span[j];
                if (other.Id == plate.Id) continue;
                hasForeign = true;
                var theirs = table[other.Id - 1];
                var dir = (positions[j] - positions[i]).Normalize();
                double relX = theirs.DriftX * theirs.DriftSpeed - mine.DriftX * mine.DriftSpeed;
                double relY = theirs.DriftY * theirs.DriftSpeed - mine.DriftY * mine.DriftSpeed;
                double relZ = theirs.DriftZ * theirs.DriftSpeed - mine.DriftZ * mine.DriftSpeed;
                double convergence = -(relX * dir.X + relY * dir.Y + relZ * dir.Z);
                double shear = ShearTransform ? ShearComponent(relX, relY, relZ, positions[i], convergence) : 0.0;
                if (convergence > maxConvergence) { maxConvergence = convergence; maxShear = shear; acrossCrust = other.Crust; acrossPlate = other.Id; }
                if (convergence < minConvergence) { minConvergence = convergence; minShear = shear; }
            }
            if (!hasForeign)
            {
                plate.Boundary = PlateBoundaryType.None;
                plate.Orogeny = 0f;
            }
            else if (ShearTransform)
            {
                ClassifySheared(ref plate, mine, table[acrossPlate - 1], acrossPlate, acrossCrust, positions[i], threshold,
                    maxConvergence, maxShear, minConvergence, minShear);
            }
            else if (maxConvergence > threshold)
            {
                plate.Boundary = PlateBoundaryType.Convergent;
                plate.Orogeny = (float)(ConvergentDriver(plate.Crust, acrossCrust, maxConvergence, Subducts(plate, plate.Id, acrossCrust, acrossPlate, mine, table)) * SegmentFactor(positions[i]));
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

    private const float TransformDriver = 0.12f;

    /// <summary>
    /// Boundary kind and driver on the whole relative motion (see <see cref="ShearTransform"/>). The across-border
    /// component is weighed against the shear along the border, and the boundary driver is blended in from the transform
    /// value as the motion grows past <paramref name="threshold"/>, so no kind starts at full strength.
    /// </summary>
    private void ClassifySheared(
        ref TectonicPlate plate, in PlateInfo mine, in PlateInfo across, int acrossId, CrustType acrossCrust, Vector3D position, double threshold,
        double maxConvergence, double maxShear, double minConvergence, double minShear)
    {
        // The stronger of the two signed motions decides, as the legacy code lets convergence win over divergence.
        bool converging = maxConvergence > threshold && maxConvergence >= -minConvergence;
        bool diverging = !converging && minConvergence < -threshold;
        double motion = converging ? maxConvergence : diverging ? -minConvergence : 0.0;
        double shear = converging ? maxShear : minShear;

        // Share of the relative motion that goes across the border: the weight of a convergent or divergent kind.
        double across01 = motion + shear > 1e-9 ? motion / (motion + shear) : 0.0;
        double gate = Smooth((motion - threshold) / (2.0 * threshold)) * Smooth((across01 - 0.2) / 0.25);
        double speed = Math.Sqrt(motion * motion + shear * shear);

        if ((!converging && !diverging) || gate < 0.5)
        {
            plate.Boundary = PlateBoundaryType.Transform;
            // A pair that barely moves relative to each other leaves only a faint scar, not a full fault ridge.
            plate.Orogeny = (float)(TransformDriver * Smooth(speed / 0.3));
            return;
        }

        double target;
        if (converging)
        {
            plate.Boundary = PlateBoundaryType.Convergent;
            target = ConvergentDriver(plate.Crust, acrossCrust, maxConvergence, SubductsPair(plate.Crust, acrossCrust, plate.Id, acrossId, mine, across)) * SegmentFactor(position);
        }
        else
        {
            plate.Boundary = PlateBoundaryType.Divergent;
            target = plate.Crust == CrustType.Oceanic ? 0.5 : -0.55;
        }
        plate.Orogeny = (float)(TransformDriver + (target - TransformDriver) * gate);
    }

    // Smoothstep on [0, 1], clamped.
    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>
    /// Magnitude of the relative motion along the border: what is left of the surface-tangent part of
    /// <c>rel</c> once the across-border component <paramref name="convergence"/> is taken out.
    /// </summary>
    private static double ShearComponent(double relX, double relY, double relZ, Vector3D position, double convergence)
    {
        var n = position.Normalize();
        double radial = relX * n.X + relY * n.Y + relZ * n.Z;
        double tangentSq = relX * relX + relY * relY + relZ * relZ - radial * radial;
        return Math.Sqrt(Math.Max(0.0, tangentSq - convergence * convergence));
    }

    private bool Subducts(in TectonicPlate plate, int plateId, CrustType acrossCrust, int acrossPlateId, in PlateInfo mine, ReadOnlySpan<PlateInfo> table)
        => SubductsPair(plate.Crust, acrossCrust, plateId, acrossPlateId, mine, table[acrossPlateId - 1]);

    /// <summary>
    /// True when this side of an oceanic-oceanic convergent border is the plate that dives (see
    /// <see cref="OceanicPolarity"/>). Always false for any other pair, and with the option off.
    /// </summary>
    private bool SubductsPair(CrustType mine, CrustType across, int mineId, int acrossId, in PlateInfo mineInfo, in PlateInfo acrossInfo)
    {
        if (!OceanicPolarity || mine != CrustType.Oceanic || across != CrustType.Oceanic) return false;
        // The lower base elevation is the older, denser plate. Ties fall to the lower id, so exactly one side dives.
        if (mineInfo.Elevation != acrossInfo.Elevation) return mineInfo.Elevation < acrossInfo.Elevation;
        return mineId < acrossId;
    }

    /// <summary>
    /// Signed convergent driver with subduction polarity: the oceanic side dives
    /// (trench, negative) while the overriding side piles up (arc/orogen, positive).
    /// </summary>
    internal static double ConvergentDriver(CrustType mine, CrustType across, double convergence, bool subducting = false)
    {
        if (subducting)
            return -convergence * 1.0; // the diving side of an oceanic-oceanic border: trench
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
    /// the widest plateaus, trenches and transforms stay narrow. Widths stay near
    /// ~1 ring so the exp(-d/w) apron fades within 2 rings of the boundary line —
    /// wider values paint whole plate interiors as uplift (mountain blurbs).
    /// </summary>
    internal static double BeltWidth(PlateBoundaryType boundary, float driver, CrustType mine, CrustType across)
    {
        return boundary switch
        {
            PlateBoundaryType.Convergent when driver < 0 => 0.6, // trench: narrow, deep
            PlateBoundaryType.Convergent when mine == CrustType.Continental && across == CrustType.Continental => 1.2,
            PlateBoundaryType.Convergent when mine == CrustType.Continental => 1.0, // Andean arc
            PlateBoundaryType.Convergent => 0.8, // island arc
            PlateBoundaryType.Divergent when driver < 0 => 0.9, // rift valley
            PlateBoundaryType.Divergent => 0.7, // mid-ocean ridge
            PlateBoundaryType.Transform => 0.6,
            _ => 1.0,
        };
    }

    /// <summary>
    /// Multi-source BFS from every boundary tile: each tile learns its distance to
    /// the nearest boundary, that boundary's type, and the orogeny driver decayed
    /// by exp(-d/w). Deterministic (index-ordered seeding, sorted neighbors) and
    /// allocation-free once warm (all scratch lives in the stage's reused buffers).
    /// </summary>
    private static void PropagateOrogeny(
        WorldDataStore store, Span<TectonicPlate> span, ReadOnlySpan<PlateInfo> table, OrogenyScratch scratch, int maxDistance)
    {
        int n = store.TileCount;
        scratch.EnsureCapacity(n);
        // Spans sliced to n: the buffers may be larger after a bigger map ran on this stage.
        var dist = scratch.Dist.AsSpan(0, n);
        var srcDriver = scratch.SrcDriver.AsSpan(0, n);
        var srcWidth = scratch.SrcWidth.AsSpan(0, n);
        var srcType = scratch.SrcType.AsSpan(0, n);
        // Each tile enters the queue at most once (dist is set before enqueue), so n slots suffice.
        var queue = scratch.Queue.AsSpan(0, n);
        int tail = 0;
        dist.Fill(-1);
        srcDriver.Clear();
        srcWidth.Fill(1f);
        srcType.Fill((byte)PlateBoundaryType.None);

        // Seed in index order so ties resolve deterministically.
        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < n; i++)
        {
            if (span[i].Boundary == PlateBoundaryType.None) continue;
            dist[i] = 0;
            srcDriver[i] = span[i].Orogeny;
            srcType[i] = (byte)span[i].Boundary;
            srcWidth[i] = (float)SourceWidth(store, span, table, i, neighbors);
            queue[tail++] = i;
        }

        int head = 0;
        while (head < tail)
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
                queue[tail++] = nb;
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (dist[i] < 0)
            {
                span[i].BoundaryRings = byte.MaxValue;
                span[i].NearestBoundary = PlateBoundaryType.None;
                span[i].Orogeny = 0f;
                continue;
            }
            double decay = DetMath.Exp(-dist[i] / Math.Max(0.25, (double)srcWidth[i]));
            span[i].BoundaryRings = (byte)dist[i];
            span[i].NearestBoundary = (PlateBoundaryType)srcType[i];
            span[i].Orogeny = (float)(srcDriver[i] * decay);
        }
    }

    private static double SourceWidth(WorldDataStore store, Span<TectonicPlate> span, ReadOnlySpan<PlateInfo> table, int i, Span<int> neighbors)
    {
        var plate = span[i];
        var mine = table[plate.Id - 1];
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
            var theirs = table[other.Id - 1];
            // Recompute convergence cheaply from stored drift (positions cancel out
            // of the argmax only if directions align; use the true projection).
            var dir = (store.GetTileVectors()[j] - store.GetTileVectors()[i]).Normalize();
            double relX = theirs.DriftX * theirs.DriftSpeed - mine.DriftX * mine.DriftSpeed;
            double relY = theirs.DriftY * theirs.DriftSpeed - mine.DriftY * mine.DriftSpeed;
            double relZ = theirs.DriftZ * theirs.DriftSpeed - mine.DriftZ * mine.DriftSpeed;
            double convergence = -(relX * dir.X + relY * dir.Y + relZ * dir.Z);
            if (convergence > best) { best = convergence; across = other.Crust; }
        }
        return BeltWidth(plate.Boundary, plate.Orogeny, plate.Crust, across);
    }

    /// <summary>Nothing unmanaged is held any more; kept so existing <c>using</c> sites compile.</summary>
    public void Dispose()
    {
    }

    private static void Grow<T>(ref T[] buffer, int length)
    {
        if (buffer.Length < length) buffer = new T[length];
    }

    /// <summary>Per-tile BFS scratch for <see cref="PropagateOrogeny"/>, owned by one stage instance.</summary>
    private sealed class OrogenyScratch
    {
        public int[] Dist = Array.Empty<int>();
        public float[] SrcDriver = Array.Empty<float>();
        public float[] SrcWidth = Array.Empty<float>();
        public byte[] SrcType = Array.Empty<byte>();
        public int[] Queue = Array.Empty<int>();

        public void EnsureCapacity(int n)
        {
            Grow(ref Dist, n);
            Grow(ref SrcDriver, n);
            Grow(ref SrcWidth, n);
            Grow(ref SrcType, n);
            Grow(ref Queue, n);
        }
    }
}
