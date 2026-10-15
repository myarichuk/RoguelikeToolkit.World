using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core;

/// <summary>Fluent builder for a fully generated <see cref="World"/>.</summary>
public sealed class WorldBuilder
{
    private int _size = 3;
    private int _seed = 42;
    private int _seedCount = 12;
    private string? _filePath;
    private readonly List<IWorldGeneratorStage> _extraStages = new();
    private bool _useDefaults = true;
    private Action<TectonicPlateGenerationStage>? _tectonicsConfig;
    private Action<BiomeGenerationStage>? _biomesConfig;
    private Action<ClimateStage>? _climateConfig;
    private Action<HydrologyStage>? _hydrologyConfig;
    private Action<ErosionGenerationStage>? _erosionConfig;
    private bool _userEdits;
    private bool _landUse;
    private bool _history = true;
    private readonly HistoryContext _historyConfig = new();
    private bool _climateRefresh = true;
    private bool _islandChains = true;
    private bool _balancedPlates = true;
    private Action<ElevationGenerationStage>? _elevationConfig;
    private const int ElevationReferenceTileCount = 40962; // size 6, where the arc height was tuned
    private Action<IslandChainStage>? _islandChainsConfig;

    /// <summary>
    /// Largest supported planet size (backlog A2, from the Phase 2
    /// benchmarks: size 7 builds in ~2s; size 8 extrapolates to ~20-30s and
    /// ~3.9M-vert meshes, so it stays batch-only and unsupported).
    /// </summary>
    public const int MaxSupportedSize = 7;

    /// <summary>Sets the planet's subdivision level (default 3), which gives <c>10 * 4^size + 2</c> tiles. Throws <see cref="ArgumentOutOfRangeException"/> outside 0..<see cref="MaxSupportedSize"/>.</summary>
    public WorldBuilder WithSize(int size)
    {
        if (size < 0 || size > MaxSupportedSize)
            throw new ArgumentOutOfRangeException(nameof(size), size,
                $"Planet size must be 0..{MaxSupportedSize} (size 8+ is unsupported; see docs/ARCHITECTURE.md).");
        _size = size;
        return this;
    }
    /// <summary>Sets the world seed (default 42). The same seed and configuration always produce a bit-identical world.</summary>
    public WorldBuilder WithSeed(int seed) { _seed = seed; return this; }
    /// <summary>Sets the number of tectonic plates (default 12). <see cref="Build"/> throws <see cref="ArgumentOutOfRangeException"/> when it is outside 0..<see cref="TectonicPlate.MaxPlates"/>.</summary>
    public WorldBuilder WithPlateCount(int seedCount) { _seedCount = seedCount; return this; }
    /// <summary>Backs the store with a memory-mapped file at <paramref name="path"/>, resolved against the current directory; null (the default) keeps it in memory. <see cref="Build"/> throws <see cref="UnauthorizedAccessException"/> for a path that escapes the current directory and <see cref="InvalidDataException"/> for an existing file that does not match this configuration.</summary>
    public WorldBuilder WithFilePath(string? path) { _filePath = path; return this; }
    /// <summary>Adds a custom stage. It runs among the built-in stages in the order given by its declared <c>Order</c> (see <see cref="WorldGeneratorStageAttribute"/>); <see cref="Build"/> throws <see cref="InvalidOperationException"/> if two stages declare the same order.</summary>
    public WorldBuilder AddStage(IWorldGeneratorStage stage) { _extraStages.Add(stage); return this; }
    /// <summary>Skip the default stage set; only <see cref="AddStage"/> stages run.</summary>
    public WorldBuilder WithoutDefaultStages() { _useDefaults = false; return this; }
    /// <summary>Select the plate area strategy (default: noisy flood-fill).</summary>
    public WorldBuilder WithPartitioner(IPlatePartitioner partitioner)
        => WithTectonics(t => t.Partitioner = partitioner);
    /// <summary>Tweak the default tectonics stage (partitioner, segmentation...).</summary>
    public WorldBuilder WithTectonics(Action<TectonicPlateGenerationStage> configure)
    {
        var prev = _tectonicsConfig;
        _tectonicsConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }
    /// <summary>
    /// Selects the balanced plate preset, which is <b>on by default</b>: seeds spread over the sphere (not crowded at the
    /// poles), plate growth speeds within a factor of 1.6 so no plate swallows the globe, and a crust field whose coastline
    /// is displaced and blurred off the plate outlines instead of tracing them, with its spread restored so relief and sea
    /// depth match the legacy worlds. Measured over 12 seeds at size 6 against the legacy generator: the largest plate is
    /// 2.7 times the smallest (was 65), plates that are all land or all sea fall from 69% to 32%, and coastline tiles on a
    /// plate border from 71% to 45% (14% of all tiles sit there). Land share stays near 36% (legacy 38%) and varies far
    /// less between seeds. Land share still falls with world size, as it always has, because erosion moves a fixed height
    /// per tile and coarse worlds fill their shallows: at size 3 and 4 expect noticeably more land than at size 6.
    /// It also sets <see cref="IslandChainOptions.BridgeReach"/> to 2 so the island-chain repair cuts the land that bridges
    /// its straits. The preset is applied before <see cref="WithTectonics"/>, so later calls override its individual knobs; pass
    /// <c>false</c> (or call <see cref="WithLegacyPlates"/>) to get the pre-balanced worlds back bit for bit.
    /// </summary>
    /// <param name="enabled">True (the default) applies the preset; false leaves the tectonics stage at its own defaults.</param>
    public WorldBuilder WithBalancedPlates(bool enabled = true) { _balancedPlates = enabled; return this; }

    /// <summary>Restores the pre-balanced plate generator: uniform-latitude seeds, a 0.6 to 2.0 growth-speed spread and plate-offset-dominated crust. The same seed then produces the same world as before the balanced preset became the default.</summary>
    public WorldBuilder WithLegacyPlates() => WithBalancedPlates(false);

    private static void ApplyBalancedPlates(TectonicPlateGenerationStage t)
    {
        t.BalancedSeeding = true;
        t.PlateOffsetWeight = 0.6;
        t.LandFraction = 0.28;
        t.CrustWarp = 0.5;
        t.CrustBlurPasses = 3;
        t.DisperseContinents = true;
        t.CrustContrast = 0.8;
        t.OceanContrast = 2.0;
        t.ShearTransform = true;
        t.OceanicPolarity = true;
        if (t.Partitioner is NoisyFloodFillPlatePartitioner flood)
        {
            flood.MinSpeed = 0.85;
            flood.SpeedRange = 0.5;
        }
    }

    /// <summary>Tweak the island-chain repair stage (see <see cref="IslandChainDetector"/>).</summary>
    public WorldBuilder WithIslandChains(Action<IslandChainStage> configure)
    {
        var prev = _islandChainsConfig;
        _islandChainsConfig = prev == null ? configure : s => { prev(s); configure(s); };
        _islandChains = true;
        return this;
    }

    /// <summary>Keep plate-boundary land ribbons as the tectonics drew them instead of breaking them into islands.</summary>
    public WorldBuilder WithoutIslandChains() { _islandChains = false; return this; }

    /// <summary>Tweak the default climate stage (smoothing passes...).</summary>
    public WorldBuilder WithClimate(Action<ClimateStage> configure)
    {
        var prev = _climateConfig;
        _climateConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }
    /// <summary>
    /// Sets the planet's axial tilt, stellar flux, spin and (optionally) a custom insolation field; see
    /// <see cref="PlanetParameters"/>. Earth by default. The parameters are copied, so later changes to
    /// <paramref name="planet"/> do not affect this builder.
    /// </summary>
    public WorldBuilder WithPlanet(PlanetParameters planet)
    {
        if (planet == null) throw new ArgumentNullException(nameof(planet));
        var copy = planet.Clone();
        return WithClimate(c => c.Planet = copy);
    }

    /// <summary>
    /// Re-runs the climate on the eroded relief before hydrology and biomes (see <see cref="ClimateRefreshStage"/>),
    /// so rain shadows and biome borders follow the mountains the player sees rather than the raw tectonic
    /// surface. On by default; <c>WithClimateRefresh(false)</c> restores the single-pass pipeline (pair it with
    /// <c>RiverThresholdScale = 1</c>, which is what that pipeline was calibrated at).
    /// <para>
    /// Why it is the default: erosion moves the coastline (sediment fills shallows, so a size-4 world gains
    /// around 20% more land tiles), and the single pass left every one of those tiles with the ocean's 0.55
    /// precipitation floor and no maritime-distance correction. The refresh measured on seven worlds: land
    /// 0.01-0.04 drier, 0.015-0.033 warmer, jungle 16% to 12% of land, desert 7% to 10%, glacier 13% to 10%;
    /// river count restored by the hydrology scale (0.92), lakes unchanged.
    /// </para>
    /// </summary>
    public WorldBuilder WithClimateRefresh(bool enabled = true) { _climateRefresh = enabled; return this; }

    /// <summary>Tweak the default hydrology stage (river threshold, max rivers...).</summary>
    public WorldBuilder WithHydrology(Action<HydrologyStage> configure)
    {
        var prev = _hydrologyConfig;
        _hydrologyConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }

    /// <summary>Configures the <see cref="ElevationGenerationStage"/> after the balanced-plates preset (when it is on) has set its defaults.</summary>
    /// <param name="configure">Callback that adjusts the stage.</param>
    public WorldBuilder WithElevation(Action<ElevationGenerationStage> configure) { _elevationConfig = configure ?? throw new ArgumentNullException(nameof(configure)); return this; }

    /// <summary>Tweak the default erosion stage (iteration counts, wind erosion...).</summary>
    public WorldBuilder WithErosion(Action<ErosionGenerationStage> configure)
    {
        var prev = _erosionConfig;
        _erosionConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }
    /// <summary>Tweak the default biome stage (smoothing passes...).</summary>
    public WorldBuilder WithBiomes(Action<BiomeGenerationStage> configure)
    {
        var prev = _biomesConfig;
        _biomesConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }

    /// <summary>
    /// Registers the <see cref="UserEditInfo"/> input layer so the result supports hand edits and
    /// <see cref="World.Recompute"/>. Costs 20 bytes per tile; an unedited layer changes nothing.
    /// </summary>
    public WorldBuilder WithUserEdits() { _userEdits = true; return this; }

    /// <summary>
    /// Configures the history simulation (turn length, span, per-turn budgets, peoples roster) through
    /// <paramref name="configure"/>; history is on by default. The same seed and configuration always
    /// produce the same catalog bytes.
    /// </summary>
    public WorldBuilder WithHistory(Action<HistoryContext>? configure = null) { _history = true; configure?.Invoke(_historyConfig); return this; }

    /// <summary>Skips the history stages (orders 26..91); the world still carries an empty <c>History</c> catalog.</summary>
    public WorldBuilder WithoutHistory() { _history = false; return this; }

    /// <summary>
    /// Adds the land-use layers and their two framing stages: <see cref="LandCapacityInfo"/> (what each tile
    /// could support, order 25) and <see cref="LandUseInfo"/> (what is decided to be done with it), which starts
    /// empty and is filled by the allocation stages you add with <see cref="AddStage"/> at orders 26..98
    /// (history occupies 26..28 and 91 when enabled, so give allocation stages other orders).
    /// <see cref="LandUseResolveStage"/> (99) then applies hand-painted pins. Implies <see cref="WithUserEdits"/>.
    /// Off by default: a default world carries no people, and costs nothing. Land use is never read by the
    /// climate, erosion, hydrology or biome stages, so it cannot change them.
    /// </summary>
    public WorldBuilder WithLandUse() { _landUse = true; _userEdits = true; return this; }

    /// <summary>Runs the full generation pipeline and returns the finished world, with the feature catalogs and spatial index built. Synchronous and CPU-bound (seconds at the largest sizes). Deterministic in the seed and configuration. The caller owns the returned <see cref="World"/> and must dispose it.</summary>
    public World Build()
    {
        var map = new WorldMap(_size, _filePath);
        // Field layers (dense, in-store). Feature catalogs attach to World, not the store.
        var platesLayer = new TectonicPlateLayer(map.DataStore, _seedCount, _seed);
        map.RegisterLayer<TectonicPlate>(platesLayer);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<BiomeInfo>(new BiomeLayer(map.DataStore, _seed,
            platesLayer));
        if (_userEdits) map.RegisterLayer<UserEditInfo>(new UserEditLayer(map.DataStore));
        if (_landUse)
        {
            map.RegisterLayer<LandCapacityInfo>(new LandCapacityLayer(map.DataStore));
            map.RegisterLayer<LandUseInfo>(new LandUseLayer(map.DataStore));
        }
        map.DataStore.Allocate();

        HistoryCatalog? history = null;
        var pipeline = new WorldGenerationPipeline();
        if (_history)
        {
            history = new HistoryCatalog(_seed);
            map.SetAttachment(history);
            map.SetAttachment(_historyConfig);
            pipeline.AddStage(new CultureSeedStage());
            pipeline.AddStage(new PolityFormStage());
            pipeline.AddStage(new HistoryEventsStage());
            pipeline.AddStage(new TradeRoadStage());
            pipeline.AddStage(new HolySiteStage());
            pipeline.AddStage(new KhanStage());
            pipeline.AddStage(new TreatyStage());
            pipeline.AddStage(new WarStage());
            pipeline.AddStage(new ConquestStage());
            pipeline.AddStage(new RebellionStage());
            pipeline.AddStage(new VillainStage());
            pipeline.AddStage(new ExchangeStage());
            pipeline.AddStage(new FaithStage());
            pipeline.AddStage(new CityHistoryProjectStage());
        }
        HydrologyStage? hydrologyStage = null;
        PlanetParameters planet = new PlanetParameters();
        float riverScale = 1f;
        if (_useDefaults)
        {
            var tectonics = new TectonicPlateGenerationStage(_seedCount, _seed);
            if (_balancedPlates) ApplyBalancedPlates(tectonics);
            _tectonicsConfig?.Invoke(tectonics);
            pipeline.AddStage(tectonics);
            var elevation = new ElevationGenerationStage(_seed);
            if (_balancedPlates) elevation.ReferenceTileCount = ElevationReferenceTileCount;
            _elevationConfig?.Invoke(elevation);
            pipeline.AddStage(elevation);
            var climate = new ClimateStage(_seed);
            _climateConfig?.Invoke(climate);
            climate.CurrentsInRefreshPassOnly = _climateRefresh;
            planet = climate.Planet ?? planet;
            pipeline.AddStage(climate);
            hydrologyStage = new HydrologyStage();
            _hydrologyConfig?.Invoke(hydrologyStage);
            riverScale = hydrologyStage.RiverThresholdScale;
            var erosion = new ErosionGenerationStage { RiverThresholdScale = riverScale, OrderIndependent = _balancedPlates };
            _erosionConfig?.Invoke(erosion);
            pipeline.AddStage(erosion);
            if (_islandChains) // ties erosion's order; the stable sort keeps it after erosion and ahead of the refresh
            {
                var chains = new IslandChainStage(_seed);
                if (_balancedPlates) chains.Options.BridgeReach = 2; // balanced plates leave wider land beside each ribbon, which bridges the straits
                _islandChainsConfig?.Invoke(chains);
                pipeline.AddStage(chains);
            }
            if (_climateRefresh) pipeline.AddStage(new ClimateRefreshStage(climate)); // ties erosion's order; the stable sort keeps it after
            pipeline.AddStage(hydrologyStage);
            var biomes = new BiomeGenerationStage(_seed) { RiverThresholdScale = riverScale };
            _biomesConfig?.Invoke(biomes);
            pipeline.AddStage(biomes);
        }
        if (_landUse) pipeline.AddStage(new LandCapacityStage());
        foreach (var s in _extraStages) pipeline.AddStage(s);
        if (_landUse) pipeline.AddStage(new LandUseResolveStage());

        pipeline.Execute(map);

        var world = new World(map, _seed) { Planet = planet };
        world.History = history ?? new HistoryCatalog(_seed);
        world.Attach(pipeline, hydrologyStage?.MaxRivers ?? new HydrologyStage().MaxRivers, riverScale);
        world.PopulateFeatureCatalogs();
        world.RebuildIndex();
        return world;
    }
}
