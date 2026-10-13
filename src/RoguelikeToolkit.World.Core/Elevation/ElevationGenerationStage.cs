using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Elevation;

/// <summary>Builds the base height field from tectonics: continentality sets the broad hypsometry, orogeny raises and depresses plate-boundary belts, noise adds hills and ridged crests, and hotspots add volcanic domes. Writes <see cref="ElevationInfo"/> for every tile.</summary>
[Preserve] // created via reflection by WorldGenerationPipeline.Discover; keep it through Unity code stripping
[WorldGeneratorStage(StageOrder.Elevation, Reads = new[] { typeof(TectonicPlate) }, ReadsOptional = new[] { typeof(UserEditInfo) }, Writes = new[] { typeof(ElevationInfo) })]
public class ElevationGenerationStage : IWorldGeneratorStage, ISeededStage
{
    /// <summary>Height of the waterline: tiles below it are ocean, tiles at or above it are land.</summary>
    public const double SeaLevel = 0.0;

    // Hotspot dome is exp(-a^2 / (2 * 0.055^2)); it falls below the 0.01 floor at
    // a = 0.055 * sqrt(2 ln 100) ~= 0.1669 rad. Skip Acos/Exp for tiles beyond it.
    private static readonly double HotspotCutoffDot = DetMath.Cos(0.055 * Math.Sqrt(2.0 * DetMath.Log(100.0))) - 1e-9;

    /// <summary>Seeds the noise octaves and the hotspot positions. Default 42.</summary>
    public int Seed { get; set; } = 42;
    /// <summary>Scale of the legacy broad-relief noise term (applied at 0.15 of this value).</summary>
    public double NoiseAmplitude { get => _noiseAmplitude; set => _noiseAmplitude = StageKnob.Finite(value, nameof(NoiseAmplitude)); }
    private double _noiseAmplitude = 0.55;
    /// <summary>Spatial frequency of the legacy broad-relief noise term.</summary>
    public double NoiseFrequency { get => _noiseFrequency; set => _noiseFrequency = StageKnob.Finite(value, nameof(NoiseFrequency)); }
    private double _noiseFrequency = 3.0;

    /// <summary>
    /// Height gained per unit of orogeny driver. Deliberately above 1: belts
    /// are narrow now (widths ~1 ring), so cores must rise steeply to keep
    /// relief-driven climate (rain shadows, glacial tarns) working. The
    /// uplift still fades within ~2 rings, so this sharpens ranges instead of
    /// widening them.
    /// </summary>
    public double BoundaryUplift { get => _boundaryUplift; set => _boundaryUplift = StageKnob.Finite(value, nameof(BoundaryUplift)); }
    private double _boundaryUplift = 1.15;

    /// <summary>Rolling-hill amplitude on land (abyssal texture is ~40% of this).</summary>
    public double HillAmplitude { get => _hillAmplitude; set => _hillAmplitude = StageKnob.Finite(value, nameof(HillAmplitude)); }
    private double _hillAmplitude = 0.085;

    /// <summary>Ridged-crest amplitude per unit of positive orogeny.</summary>
    public double RidgeAmplitude { get => _ridgeAmplitude; set => _ridgeAmplitude = StageKnob.Finite(value, nameof(RidgeAmplitude)); }
    private double _ridgeAmplitude = 0.38;

    /// <summary>Hotspot island-chain count (deterministic positions from seed).</summary>
    public int HotspotCount { get => _hotspotCount; set => _hotspotCount = StageKnob.NonNegative(value, nameof(HotspotCount)); }
    private int _hotspotCount = 3;

    /// <summary>Hotspot swell height; oceanic hotspots breach as volcanic islands.</summary>
    public double HotspotAmplitude { get => _hotspotAmplitude; set => _hotspotAmplitude = StageKnob.Finite(value, nameof(HotspotAmplitude)); }
    private double _hotspotAmplitude = 0.55;

    /// <summary>
    /// Tile count at which volcanic arcs on oceanic crust reach full height; on a coarser grid they are scaled down by
    /// <c>sqrt(tileCount / ReferenceTileCount)</c>. A belt is about one tile wide whatever the resolution, so on a coarse grid
    /// it covers far more of the globe than its real width does (at size 3, 22% of all tiles were arc land on oceanic crust,
    /// against 4.6% at size 6) and every small world came out half land. A tile's height stands for its whole area, so a belt
    /// narrower than the tile lifts it proportionally less. 0 (default) leaves arcs at full height at every size.
    /// </summary>
    public int ReferenceTileCount { get => _referenceTileCount; set => _referenceTileCount = StageKnob.NonNegative(value, nameof(ReferenceTileCount)); }
    private int _referenceTileCount;

    /// <summary>Creates a stage with the default <see cref="Seed"/> (42); this is the constructor <see cref="WorldGenerationPipeline.Discover(string?, Action{StageDiscoveryDiagnostic}?)"/> uses.</summary>
    public ElevationGenerationStage()
    {
    }

    /// <summary>Creates a stage with the given <see cref="Seed"/>.</summary>
    /// <param name="seed">Seed for noise and hotspot placement.</param>
    public ElevationGenerationStage(int seed = 42)
    {
        Seed = seed;
    }

    /// <summary>Computes the height of every tile of <paramref name="map"/> from the <see cref="TectonicPlate"/> layer, replacing the <see cref="ElevationInfo"/> layer. Painted height offsets are added when the user-edit layer is present.</summary>
    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>();
        var vectors = store.GetTileVectors();
        // Hand sculpting rides on the tectonic relief, ahead of climate and erosion: a drawn ridge
        // shades the lee, sheds talus and drains like any other mountain.
        bool hasEdits = store.IsLayerRegistered<UserEditInfo>();
        var edits = hasEdits ? store.GetSpan<UserEditInfo>() : default;

        // Deterministic hotspot anchors (mantle plumes, independent of plates).
        var hotspots = new Vector3D[Math.Max(0, HotspotCount)];
        for (int h = 0; h < hotspots.Length; h++)
        {
            var r = Rng.Create(Seed + 9000, h);
            double lat = r.NextDouble() * 180.0 - 90.0;
            double lon = r.NextDouble() * 360.0 - 180.0;
            hotspots[h] = Vector3D.FromGeoCoord(new GeoCoord(lat, lon));
        }

        double arcScale = ReferenceTileCount > 0 ? Math.Min(1.0, Math.Sqrt(store.TileCount / (double)ReferenceTileCount)) : 1.0;

        for (int i = 0; i < store.TileCount; i++)
        {
            var plate = plates[i];
            var v = vectors[i];

            // Base hypsometry from continentality: continents ride high, ocean
            // basins low, with broad swells + rolling hills + micro ripples.
            bool land = plate.Continentality > 0;
            double height = plate.Continentality * 0.85;
            height += SphereNoise.Fbm(v * 2.3, Seed + 7) * 0.20;
            height += SphereNoise.Fbm(v * 6.5, Seed + 71) * HillAmplitude * (land ? 1.0 : 0.4);
            height += SphereNoise.Fbm(v * 15.0, Seed + 717) * 0.03;

            // Legacy broad relief term (kept for continuity of the height range).
            height += SphereNoise.Fbm(v * NoiseFrequency, Seed) * NoiseAmplitude * 0.15;

            // Orogeny: signed belt driver already decayed by boundary distance
            // (fold belts / arcs uplift, trenches and rifts depress).
            double orogeny = plate.Orogeny * BoundaryUplift;
            if (plate.Crust == CrustType.Oceanic && orogeny > 0.0) orogeny *= arcScale;
            height += Math.Min(Math.Max(orogeny, -1.0), 2.0);

            // Ridged crests inside uplift belts (sharp ranges, wide plateaus).
            if (plate.Orogeny > 0.02 && plate.Continentality > -0.2)
            {
                double ridge = SphereNoise.RidgedFbm(v * 5.0, Seed + 313) - 0.55;
                height += ridge * Math.Min(plate.Orogeny, 1.5) * RidgeAmplitude;
            }

            // Hotspot swells: gaussian domes that breach as islands/seamounts.
            for (int h = 0; h < hotspots.Length; h++)
            {
                double dot = Math.Clamp(Vector3D.Dot(v, hotspots[h]), -1.0, 1.0);
                if (dot < HotspotCutoffDot) continue; // swell < 0.01 beyond this angle
                double ang = DetMath.Acos(dot);
                double swell = DetMath.Exp(-(ang * ang) / (2.0 * 0.055 * 0.055));
                if (swell > 0.01) height += swell * HotspotAmplitude;
            }

            // Oceanic crust sits lower on average (abyssal plains).
            if (plate.Crust == CrustType.Oceanic) height -= 0.10;
            if (hasEdits) height += edits[i].HeightDelta;
            elev[i] = new ElevationInfo { Height = (float)height };
        }
    }
}
