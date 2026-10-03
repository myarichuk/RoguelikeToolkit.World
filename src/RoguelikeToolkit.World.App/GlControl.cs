using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.App
{
    public enum ProjectionType
    {
        Sphere,
        Equirectangular,
        Mercator,
        Gnomonic
    }

    public enum ColorMode
    {
        Plates,
        Biome,
        Elevation,
        Water,
        Climate,
        Deposits
    }

    /// <summary>
    /// Hex debug view (flat per-tile colors + hex edges) or Terrain 3D view
    /// (vertices displaced by elevation, textured biome/river/lake colors,
    /// no hex lines).
    /// </summary>
    public enum ViewMode
    {
        Hex,
        Terrain
    }

    public unsafe class GlControl : OpenGlControlBase
    {
        public ProjectionType ProjectionMode { get; set; } = ProjectionType.Sphere;

        public float Yaw { get; set; } = 0f;
        public float Pitch { get; set; } = 0f;
        public float Distance { get; set; } = 2.2f;
        public float PanX { get; set; } = 0f;
        public float PanY { get; set; } = 0f;
        public bool ShowPlates { get; set; } = true;
        public bool ShowHexes { get; set; } = true;
        public ViewMode ViewMode { get; private set; } = ViewMode.Hex;
        public float HeightScale { get; private set; } = TerrainShading.DefaultHeightScale;
        public float DepressionExaggeration { get; private set; } = 1f;
        public bool BathymetryParity { get; private set; } = false;
        public System.Numerics.Vector3 SelectedHexCenter { get; set; } = new System.Numerics.Vector3(0, 0, 0);
        public int RecursionLevel { get; set; } = 4;

        public WorldMap Map => _map;
        public TectonicPlateLayer PlateLayer => _plateLayer;
        public LocalMapLayer LocalLayer => _localLayer;
        public ElevationLayer ElevationLayer => _elevLayer;

        public int WorldSeed { get; private set; } = 42;
        public long LastGenMs { get; private set; }
        public int TileCount => _map.DataStore.TileCount;
        public ColorMode ColorMode { get; private set; } = ColorMode.Plates;
        public string StatusText { get; private set; } = string.Empty;

        public event EventHandler? StatusChanged;
        public Action<string>? OnDiagnostic;

        private int _shaderProgram;
        private int _vao;
        private int _vboPos;
        private int _vboNormal;
        private int _vboBary;
        private int _vboColor;
        private int _vertexCount;

        private int _uMvpMatrix;
        private int _uModelMatrix;
        private int _uShowPlates;
        private int _uShowHexes;
        private int _uTerrainMode;
        private int _uSelectedHexCenter;

        private float[] _positions = Array.Empty<float>();
        private float[] _normals = Array.Empty<float>();
        private float[] _barycentric = Array.Empty<float>();
        private float[] _colors = Array.Empty<float>();
        private int[] _vertexTile = Array.Empty<int>();
        private int _meshTileCount = -1;

        private WorldMap _map = null!;
        private TectonicPlateLayer _plateLayer = null!;
        private ElevationLayer _elevLayer = null!;
        private HydrologyLayer _hydroLayer = null!;
        private ClimateLayer _climateLayer = null!;
        private LocalMapLayer _localLayer = null!;
        private WorldGenerationPipeline _pipeline = null!;

        // Sparse catalogs + per-tile classifications, rebuilt after every
        // pipeline run. Drives the Water/Deposits color modes and the hex panel.
        private RiverCatalog _rivers = new();
        private WaterBodyCatalog _bodies = new();
        private RangeCatalog _ranges = new();
        private DepositCatalog _deposits = new();
        private int _requestedPlateCount = 12;
        private WaterBodyKind?[] _waterKindByTile = Array.Empty<WaterBodyKind?>();
        private bool[] _glacierByTile = Array.Empty<bool>();
        private DepositType?[] _depositByTile = Array.Empty<DepositType?>();

        // GLSL sources live in Presentation.GlShaders: desktop `#version 330 core`
        // plus a GLSL ES 1.00-compatible variant for ANGLE/GLES contexts.

        public GlControl()
        {
            // First world is built synchronously: the control needs a map to exist.
            // Every later (re)generation runs off the UI thread (RequestGeneration).
            Adopt(Generate(RecursionLevel, _requestedPlateCount, WorldSeed));
        }

        /// <summary>
        /// A fully generated world plus everything derived from it for display.
        /// Built on a worker thread with no access to control state, then swapped
        /// in on the UI thread, so the visible world is never mutated mid-render.
        /// </summary>
        private sealed class GeneratedWorld : IDisposable
        {
            public WorldMap Map = null!;
            public WorldGenerationPipeline Pipeline = null!;
            public TectonicPlateLayer PlateLayer = null!;
            public ElevationLayer ElevLayer = null!;
            public HydrologyLayer HydroLayer = null!;
            public ClimateLayer ClimateLayer = null!;
            public LocalMapLayer LocalLayer = null!;
            public RiverCatalog Rivers = new();
            public WaterBodyCatalog Bodies = new();
            public RangeCatalog Ranges = new();
            public DepositCatalog Deposits = new();
            public WaterBodyKind?[] WaterKindByTile = Array.Empty<WaterBodyKind?>();
            public bool[] GlacierByTile = Array.Empty<bool>();
            public DepositType?[] DepositByTile = Array.Empty<DepositType?>();
            public long GenMs;

            public void Dispose()
            {
                Map?.Dispose();
                Pipeline?.Dispose();
            }
        }

        private static GeneratedWorld Generate(int size, int seedCount, int seed)
        {
            var g = new GeneratedWorld();
            var sw = Stopwatch.StartNew();
            try
            {
                g.Map = new WorldMap(size);

                g.PlateLayer = new TectonicPlateLayer(g.Map.DataStore, seedCount, seed);
                g.Map.RegisterLayer(g.PlateLayer);
                g.ElevLayer = new ElevationLayer(g.Map.DataStore);
                g.Map.RegisterLayer(g.ElevLayer);
                // Full field-layer set: the discovered pipeline writes hydrology
                // and climate too (missing layers used to crash Execute). Keep in
                // sync with WorldBuilder's registration.
                g.HydroLayer = new HydrologyLayer(g.Map.DataStore);
                g.Map.RegisterLayer(g.HydroLayer);
                g.ClimateLayer = new ClimateLayer(g.Map.DataStore);
                g.Map.RegisterLayer(g.ClimateLayer);
                g.LocalLayer = new LocalMapLayer(g.Map.DataStore, seed, g.PlateLayer);
                g.Map.RegisterLayer(g.LocalLayer);
                g.Map.DataStore.Allocate();

                g.Pipeline = new WorldGenerationPipeline();
                g.Pipeline.Discover("Plugins"); // Try to discover external plugins if any

                // Set params on stages before execution
                foreach (var seeded in g.Pipeline.Stages.OfType<ISeededStage>())
                    seeded.Seed = seed;
                var tectonicStage = g.Pipeline.Stages.OfType<TectonicPlateGenerationStage>().FirstOrDefault();
                if (tectonicStage != null)
                    tectonicStage.SeedCount = seedCount;

                g.Pipeline.Execute(g.Map);
                g.GenMs = sw.ElapsedMilliseconds;

                BuildOverlays(g, seed);
                return g;
            }
            catch
            {
                g.Dispose();
                throw;
            }
        }

        private static void BuildOverlays(GeneratedWorld g, int seed)
        {
            var map = g.Map;
            HydrologyStage.PopulateCatalogs(map, g.Rivers, g.Bodies);
            RangeCatalogBuilder.Populate(map, g.Ranges);
            DepositCatalogBuilder.Populate(map, g.Deposits, seed);

            int n = map.DataStore.TileCount;
            var kinds = new WaterBodyKind?[n];
            foreach (var body in g.Bodies.Bodies)
            {
                foreach (int t in body.Tiles)
                {
                    if ((uint)t < (uint)n) kinds[t] = body.Kind;
                }
            }
            g.WaterKindByTile = kinds;

            var elev = map.DataStore.GetSpan<ElevationInfo>();
            var climate = map.DataStore.GetSpan<ClimateInfo>();
            var vectors = map.DataStore.GetTileVectors();
            var glac = new bool[n];
            for (int t = 0; t < n; t++)
                glac[t] = Glaciology.IsGlacierTile(vectors[t], elev[t].Height,
                    climate[t].Temperature, climate[t].Precipitation);
            g.GlacierByTile = glac;

            // Richest deposit per tile wins the overlay marker.
            var markers = new DepositType?[n];
            var best = new float[n];
            for (int k = 0; k < n; k++) best[k] = -1f;
            foreach (var d in g.Deposits.Deposits)
            {
                if ((uint)d.TileIndex >= (uint)n) continue;
                if (d.Richness > best[d.TileIndex])
                {
                    best[d.TileIndex] = d.Richness;
                    markers[d.TileIndex] = d.Type;
                }
            }
            g.DepositByTile = markers;
        }

        /// <summary>UI thread only: make a generated world the visible one and release the old one.</summary>
        private void Adopt(GeneratedWorld g)
        {
            var oldMap = _map;
            var oldPipeline = _pipeline;

            _map = g.Map;
            _pipeline = g.Pipeline;
            _plateLayer = g.PlateLayer;
            _elevLayer = g.ElevLayer;
            _hydroLayer = g.HydroLayer;
            _climateLayer = g.ClimateLayer;
            _localLayer = g.LocalLayer;
            _rivers = g.Rivers;
            _bodies = g.Bodies;
            _ranges = g.Ranges;
            _deposits = g.Deposits;
            _waterKindByTile = g.WaterKindByTile;
            _glacierByTile = g.GlacierByTile;
            _depositByTile = g.DepositByTile;
            LastGenMs = g.GenMs;

            oldMap?.Dispose();
            oldPipeline?.Dispose();
            UpdateStatus();
        }

        private bool _generating;
        private bool _generationQueued;

        /// <summary>
        /// Generates a world for the current size / plate count / seed on a worker
        /// thread and swaps it in when done. Requests that arrive meanwhile
        /// coalesce: the stale result is discarded and the latest parameters win.
        /// </summary>
        private void RequestGeneration()
        {
            if (_generating)
            {
                _generationQueued = true;
                return;
            }

            _generating = true;
            int size = RecursionLevel, plates = _requestedPlateCount, seed = WorldSeed;
            StatusText = $"Generating seed {seed}, size {size}...";
            StatusChanged?.Invoke(this, EventArgs.Empty);

            System.Threading.Tasks.Task.Run(() => Generate(size, plates, seed)).ContinueWith(t =>
                Dispatcher.UIThread.Post(() => OnGenerated(t)));
        }

        private void OnGenerated(System.Threading.Tasks.Task<GeneratedWorld> task)
        {
            _generating = false;

            if (task.IsFaulted)
            {
                OnDiagnostic?.Invoke($"World generation failed: {task.Exception?.GetBaseException().Message}");
                StatusText = "Generation failed (see diagnostics)";
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (_generationQueued)
            {
                task.Result.Dispose(); // superseded by a newer request
            }
            else
            {
                Adopt(task.Result);
                _needsMeshRebuild = true;
                RenderFrame();
            }

            if (_generationQueued)
            {
                _generationQueued = false;
                RequestGeneration();
            }
        }

        /// <summary>Everything attached to one hex (river reach, water, glacier, deposits...).</summary>
        /// <summary>B2 drill-down: region grid for one planet tile, derived from this view's store.</summary>
        public RegionHandle DeriveRegion(int tileIndex, int regionSize = RegionMaps.DefaultRegionSize)
        {
            if ((uint)tileIndex >= (uint)_map.DataStore.TileCount) throw new IndexOutOfRangeException();
            var parent = TerrainOrientation.Sample(_map.DataStore, tileIndex);
            var center = _map.DataStore.GetGeoCoord(tileIndex);
            double radiusKm = MapBounds.ForPlanetTile(_map.DataStore, tileIndex).RadiusKm;
            uint seed = MapSeeds.DeriveRegionSeed(WorldSeed, tileIndex);
            return RegionMaps.DeriveRegionMap(
                MapAddress.ForRegion(WorldSeed, tileIndex, -1), parent, center, radiusKm, seed, regionSize);
        }

        public TileFeatureInfo GetTileFeatures(int tileIndex)
            => TileFeatures.Query(_map.DataStore, tileIndex, _rivers, _bodies, _ranges, _deposits);

        public void SetRecursionLevel(int level)
        {
            if (level == RecursionLevel) return;
            RecursionLevel = level;

            // Debounced: slider drags rebuild the whole world; wait for the user to settle.
            Debounce(RequestGeneration);
        }

        public void SetPlateCount(int count)
        {
            if (count == _requestedPlateCount) return;
            _requestedPlateCount = count;

            // Debounced: slider drags regenerate; wait for the user to settle.
            Debounce(RequestGeneration);
        }

        public void Regenerate(int seed)
        {
            WorldSeed = seed;
            RequestGeneration();
        }

        public void SetColorMode(ColorMode mode)
        {
            if (mode == ColorMode) return;
            ColorMode = mode;
            Recolor();
            UpdateStatus();
        }

        public void SetViewMode(ViewMode mode)
        {
            if (mode == ViewMode) return;
            ViewMode = mode;
            _needsMeshRebuild = true;
            UpdateStatus();
            RenderFrame();
        }

        public void SetHeightScale(float scale)
        {
            float clamped = MathF.Min(0.35f, MathF.Max(0f, scale));
            HeightScale = clamped;
            if (ViewMode == ViewMode.Terrain)
            {
                _needsMeshRebuild = true;
                RenderFrame();
            }
        }

        public void SetDepressionScale(float scale)
        {
            float clamped = MathF.Min(3f, MathF.Max(0.5f, scale));
            DepressionExaggeration = clamped;
            if (ViewMode == ViewMode.Terrain)
            {
                _needsMeshRebuild = true;
                RenderFrame();
            }
        }

        public void SetBathymetryParity(bool enabled)
        {
            BathymetryParity = enabled;
            if (ViewMode == ViewMode.Terrain)
            {
                _needsMeshRebuild = true;
                RenderFrame();
            }
        }

        private void Recolor()
        {
            // Terrain view displaces vertices by elevation, so any recolor needs
            // positions rebuilt too (heights may have changed under it).
            if (ViewMode == ViewMode.Terrain)
            {
                _needsMeshRebuild = true;
                RenderFrame();
                return;
            }

            // Stale mesh (e.g. recursion changed but rebuild hasn't run yet): rebuild instead.
            var plates = _plateLayer.Store.GetSpan<TectonicPlate>();
            if (_vertexTile.Length != _vertexCount || _meshTileCount != plates.Length)
            {
                _needsMeshRebuild = true;
                RenderFrame();
                return;
            }

            var locals = _localLayer.Store.GetSpan<LocalMapInfo>();
            var heights = _elevLayer.Store.GetSpan<ElevationInfo>();

            // Per-tile recolor with zero lookups via the stored vertex->tile map.
            for (int i = 0; i < _vertexCount; i++)
            {
                int tile = _vertexTile[i];
                var color = TileDebugColor(plates[tile].Id, locals[tile].Biome, heights[tile].Height, tile);
                _colors[i * 3] = color.X;
                _colors[i * 3 + 1] = color.Y;
                _colors[i * 3 + 2] = color.Z;
            }

            _needsColorBufferUpdate = true;
            RenderFrame();
        }

        private System.Numerics.Vector3 TileDebugColor(int plateId, BiomeType biome, float height, int tile)
        {
            if (ViewMode == ViewMode.Terrain)
                return TileTerrainColor(tile);
            if (ColorMode == ColorMode.Water)
                return TileWaterColor(tile, biome);
            if (ColorMode == ColorMode.Climate)
                return TileClimateColor(tile);
            if (ColorMode == ColorMode.Deposits)
                return TileDepositColor(tile, biome);
            if (ColorMode == ColorMode.Elevation)
                return TileElevationColor(tile, height);
            return ColorMode switch
            {
                ColorMode.Biome => BiomePalette.ColorFor(biome),
                _ => PlatePalette.ColorFor(plateId),
            };
        }

        private System.Numerics.Vector3 TileWaterColor(int tile, BiomeType biome)
        {
            if ((uint)tile < (uint)_glacierByTile.Length && _glacierByTile[tile])
                return HydroPalette.ColorForGlacier();
            var hydro = _hydroLayer.Store.GetSpan<HydrologyInfo>();
            if ((uint)tile < (uint)hydro.Length && hydro[tile].IsRiver == 1)
                return HydroPalette.ColorForRiver(hydro[tile].Flow);
            if ((uint)tile < (uint)_waterKindByTile.Length && _waterKindByTile[tile].HasValue)
                return HydroPalette.ColorFor(_waterKindByTile[tile]!.Value);
            return HydroPalette.DimLand(BiomePalette.ColorFor(biome));
        }

        private System.Numerics.Vector3 TileClimateColor(int tile)
        {
            var climate = _climateLayer.Store.GetSpan<ClimateInfo>();
            if ((uint)tile >= (uint)climate.Length) return new System.Numerics.Vector3(0.5f, 0.5f, 0.5f);
            return ClimatePalette.ColorFor(climate[tile].Temperature, climate[tile].Precipitation);
        }

        private System.Numerics.Vector3 TileDepositColor(int tile, BiomeType biome)
        {
            if ((uint)tile < (uint)_depositByTile.Length && _depositByTile[tile].HasValue)
                return DepositPalette.ColorFor(_depositByTile[tile]!.Value);
            return DepositPalette.DimLand(BiomePalette.ColorFor(biome));
        }

        private System.Numerics.Vector3 TileElevationColor(int tile, float height)
        {
            var color = ElevationPalette.ColorFor(height);
            // Two-sided hillshade (D2): signed relief vs the neighbor mean -
            // pits darken, peaks brighten, so relief reads instead of flat bands.
            var heights = _elevLayer.Store.GetSpan<ElevationInfo>();
            if ((uint)tile >= (uint)heights.Length) return color;
            Span<int> scratch = stackalloc int[6];
            int adjacent = _map.DataStore.GetAdjacent(tile, scratch);
            float neighborSum = 0f;
            for (int k = 0; k < adjacent; k++)
            {
                float d = heights[scratch[k]].Height;
                neighborSum += d;
            }
            float shade = TerrainShading.ReliefShadeFactor(adjacent > 0 ? height - neighborSum / adjacent : 0f);
            return color * shade;
        }

        /// <summary>Textured terrain color for one tile (Terrain view).</summary>
        private System.Numerics.Vector3 TileTerrainColor(int tile)
        {
            var locals = _localLayer.Store.GetSpan<LocalMapInfo>();
            var heights = _elevLayer.Store.GetSpan<ElevationInfo>();
            var hydro = _hydroLayer.Store.GetSpan<HydrologyInfo>();
            var climate = _climateLayer.Store.GetSpan<ClimateInfo>();
            if ((uint)tile >= (uint)heights.Length)
                return new System.Numerics.Vector3(0.5f, 0.5f, 0.5f);
            var h = hydro[tile];
            bool glacier = (uint)tile < (uint)_glacierByTile.Length && _glacierByTile[tile];
            WaterBodyKind? kind = (uint)tile < (uint)_waterKindByTile.Length ? _waterKindByTile[tile] : null;
            var c = climate[tile];
            var terrainBase = TerrainShading.ColorFor(locals[tile].Biome, heights[tile].Height,
                h.IsRiver == 1, h.Flow, h.LakeDepth, kind, glacier, h.IsPlaya == 1,
                c.Temperature, c.Precipitation, tile);
            if (heights[tile].Height >= 0f)
                terrainBase = TerrainShading.DepressionCue(terrainBase, TileRelief(tile));
            return terrainBase;
        }

        /// <summary>Displaced sphere radius for one tile (Terrain view).</summary>
        /// <summary>Signed height relief vs the neighbor mean (D2/D4).</summary>
        private float TileRelief(int tile)
        {
            var heights = _elevLayer.Store.GetSpan<ElevationInfo>();
            if ((uint)tile >= (uint)heights.Length) return 0f;
            Span<int> scratch = stackalloc int[6];
            int adjacent = _map.DataStore.GetAdjacent(tile, scratch);
            if (adjacent <= 0) return 0f;
            float sum = 0f;
            for (int k = 0; k < adjacent; k++) sum += heights[scratch[k]].Height;
            return heights[tile].Height - sum / adjacent;
        }

        private float TileTerrainRadius(int tile)
        {
            var heights = _elevLayer.Store.GetSpan<ElevationInfo>();
            var hydro = _hydroLayer.Store.GetSpan<HydrologyInfo>();
            if ((uint)tile >= (uint)heights.Length) return 1f;
            var h = hydro[tile];
            return TerrainShading.DisplacedRadius(heights[tile].Height,
                h.IsRiver == 1, h.Flow, h.LakeDepth, HeightScale, DepressionExaggeration, BathymetryParity ? HeightScale : 0.025f);
        }

        private void UpdateStatus()
        {
            int lakes = 0;
            foreach (var b in _bodies.Bodies) if (b.Kind == WaterBodyKind.Lake) lakes++;
            StatusText = $"Seed {WorldSeed} | Tiles {TileCount:N0} | Gen {LastGenMs} ms | Plates {_plateLayer.SeedCount} | Rivers {_rivers.Rivers.Count} | Lakes {lakes} | Deposits {_deposits.Deposits.Count} | View {ViewMode}";
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private DispatcherTimer? _debounceTimer;
        private Action? _pendingDebounceAction;

        private void Debounce(Action action)
        {
            _pendingDebounceAction = action;
            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                _debounceTimer.Tick += (s, e) =>
                {
                    _debounceTimer.Stop();
                    var pending = _pendingDebounceAction;
                    _pendingDebounceAction = null;
                    try { pending?.Invoke(); }
                    catch (Exception ex) { OnDiagnostic?.Invoke($"Deferred action failed: {ex.Message}"); }
                };
            }
            else
            {
                _debounceTimer.Stop();
            }
            _debounceTimer.Start();
        }

        public void SetProjectionMode(ProjectionType mode)
        {
            if (mode == ProjectionMode) return;
            ProjectionMode = mode;

            if (mode != ProjectionType.Sphere)
            {
                Yaw = 0;
                Pitch = 0;
                PanX = 0;
                PanY = 0;
            }

            _needsMeshRebuild = true;
            RenderFrame();
        }

        private bool _needsColorBufferUpdate = false;
        private bool _needsMeshRebuild = false;

        public void RenderFrame()
        {
            RequestNextFrameRendering();
        }

        protected override void OnOpenGlInit(GlInterface gl)
        {
            base.OnOpenGlInit(gl);

            // Windows often lands on an ANGLE-backed GLES context (ES 2.0-style),
            // which rejects `#version 330 core` outright; macOS/Linux usually get
            // desktop GL. Pick the variant for the actual context, then fall back
            // to the other one if the first choice fails to build.
            bool isGles = GlVersion.Type == GlProfileType.OpenGLES;
            GlShaders.Selection shaders = GlShaders.Select(isGles);
            if (!TryBuildShaderProgram(gl, shaders, out _shaderProgram))
            {
                shaders = GlShaders.Select(!isGles);
                if (!TryBuildShaderProgram(gl, shaders, out _shaderProgram))
                {
                    // Loud failure, not a black screen: the status line shows this
                    // (MainWindow wires OnDiagnostic to TxtStatus), and the console
                    // keeps the per-shader compiler logs with GL identification
                    // for bug reports.
                    var msg = $"World view disabled: no shader variant compiled. " +
                        $"GL '{gl.Version}' ({gl.Renderer} / {gl.Vendor}), Avalonia context {GlVersion}. " +
                        $"See the console output or earlier status messages for the compiler logs.";
                    Console.WriteLine(msg);
                    OnDiagnostic?.Invoke(msg);
                    return;
                }
            }

            _uMvpMatrix = gl.GetUniformLocationString(_shaderProgram, "uMvpMatrix");
            _uModelMatrix = gl.GetUniformLocationString(_shaderProgram, "uModelMatrix");
            _uShowPlates = gl.GetUniformLocationString(_shaderProgram, "uShowPlates");
            _uShowHexes = gl.GetUniformLocationString(_shaderProgram, "uShowHexes");
            _uTerrainMode = gl.GetUniformLocationString(_shaderProgram, "uTerrainMode");
            _uSelectedHexCenter = gl.GetUniformLocationString(_shaderProgram, "uSelectedHexCenter");

            // Resolved once per context, not per frame.
            _glUniform1i = Marshal.GetDelegateForFunctionPointer<glUniform1i_t>(gl.GetProcAddress("glUniform1i"));
            _glUniform3f = Marshal.GetDelegateForFunctionPointer<glUniform3f_t>(gl.GetProcAddress("glUniform3f"));

            SetupMesh(gl);
        }

        private bool TryBuildShaderProgram(GlInterface gl, GlShaders.Selection shaders, out int program)
        {
            program = 0;

            int vertexShader = gl.CreateShader(GlConsts.GL_VERTEX_SHADER);
            gl.ShaderSourceString(vertexShader, shaders.VertexSource);
            gl.CompileShader(vertexShader);
            if (!CheckShaderCompilation(gl, vertexShader, shaders.Name + " vertex"))
            {
                gl.DeleteShader(vertexShader);
                return false;
            }

            int fragmentShader = gl.CreateShader(GlConsts.GL_FRAGMENT_SHADER);
            gl.ShaderSourceString(fragmentShader, shaders.FragmentSource);
            gl.CompileShader(fragmentShader);
            if (!CheckShaderCompilation(gl, fragmentShader, shaders.Name + " fragment"))
            {
                gl.DeleteShader(vertexShader);
                gl.DeleteShader(fragmentShader);
                return false;
            }

            program = gl.CreateProgram();
            gl.AttachShader(program, vertexShader);
            gl.AttachShader(program, fragmentShader);
            if (shaders.BindAttributeLocations)
            {
                // GLES 1.00-style shaders have no `layout(location =)` qualifiers,
                // so pin the mesh VBO layout before linking.
                foreach (var (location, name) in GlShaders.Attributes)
                    gl.BindAttribLocationString(program, location, name);
            }
            gl.LinkProgram(program);

            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);

            int linkStatus;
            gl.GetProgramiv(program, GlConsts.GL_LINK_STATUS, &linkStatus);
            if (linkStatus == 0)
            {
                int maxLength;
                gl.GetProgramiv(program, GlConsts.GL_INFO_LOG_LENGTH, &maxLength);
                byte* infoLog = stackalloc byte[maxLength];
                gl.GetProgramInfoLog(program, maxLength, out int length, infoLog);
                var msg = $"Shader program link error ({shaders.Name}): {Marshal.PtrToStringAnsi((IntPtr)infoLog)}";
                Console.WriteLine(msg);
                OnDiagnostic?.Invoke(msg);
                gl.DeleteProgram(program);
                program = 0;
                return false;
            }

            return true;
        }

        private bool CheckShaderCompilation(GlInterface gl, int shader, string stage)
        {
            int success;
            gl.GetShaderiv(shader, GlConsts.GL_COMPILE_STATUS, &success);
            if (success == 0)
            {
                int maxLength;
                gl.GetShaderiv(shader, GlConsts.GL_INFO_LOG_LENGTH, &maxLength);
                byte* infoLog = stackalloc byte[maxLength];
                gl.GetShaderInfoLog(shader, maxLength, out int length, infoLog);
                var msg = $"Shader compile error ({stage}): {Marshal.PtrToStringAnsi((IntPtr)infoLog)}";
                Console.WriteLine(msg);
                OnDiagnostic?.Invoke(msg);
                return false;
            }

            return true;
        }

        private void SetupMesh(GlInterface gl)
        {
            // Release previous GL resources first (SetupMesh always runs with a current GL context).
            if (_vao != 0)
            {
                int[] oldBuffers = new int[] { _vboPos, _vboNormal, _vboBary, _vboColor };
                fixed (int* pOld = oldBuffers)
                {
                    gl.DeleteBuffers(4, pOld);
                }

                int oldVao = _vao;
                gl.DeleteVertexArrays(1, &oldVao);

                _vao = 0;
                _vboPos = 0;
                _vboNormal = 0;
                _vboBary = 0;
                _vboColor = 0;
            }

            // Indexed generation: face corners ARE tile indices, so coloring needs
            // no per-vertex store lookups and no project->inverse roundtrips.
            IcosphereGenerator.Generate(_map.DataStore.Size, out RoguelikeToolkit.World.Core.Vector3D[] tileVerts, out TriangleIndices[] faces);
            int tileCount = tileVerts.Length;

            IProjection? projectionObj = ProjectionMode switch
            {
                ProjectionType.Equirectangular => new EquirectangularProjection(1.0),
                ProjectionType.Mercator => new MercatorProjection(1.0),
                ProjectionType.Gnomonic => new GnomonicProjection(1.0),
                _ => null
            };

            bool flat = projectionObj != null;

            // Per-tile caches: projected anchor position and debug color.
            var tileX = new float[tileCount];
            var tileY = new float[tileCount];
            var tileZ = new float[tileCount];
            var tileColor = new System.Numerics.Vector3[tileCount];

            var plates = _plateLayer.Store.GetSpan<TectonicPlate>();
            var locals = _localLayer.Store.GetSpan<LocalMapInfo>();
            var heights = _elevLayer.Store.GetSpan<ElevationInfo>();
            for (int t = 0; t < tileCount; t++)
            {
                var v = tileVerts[t];
                tileX[t] = (float)v.X;
                tileY[t] = (float)v.Y;
                tileZ[t] = (float)v.Z;
                tileColor[t] = TileDebugColor(plates[t].Id, locals[t].Biome, heights[t].Height, t);
            }

            bool[]? faceHidden = null;
            if (flat)
            {
                var tileGeo = new GeoCoord[tileCount];
                for (int t = 0; t < tileCount; t++)
                    tileGeo[t] = tileVerts[t].ToGeoCoord();

                for (int t = 0; t < tileCount; t++)
                {
                    var c = projectionObj!.Project(tileGeo[t]);
                    tileX[t] = (float)c.X;
                    tileY[t] = (float)c.Y;
                    tileZ[t] = 0f;
                }

                faceHidden = new bool[faces.Length];
                for (int f = 0; f < faces.Length; f++)
                {
                    var face = faces[f];
                    double lon1 = tileGeo[face.v1].Longitude;
                    double lon2 = tileGeo[face.v2].Longitude;
                    double lon3 = tileGeo[face.v3].Longitude;

                    // Hide triangles spanning more than 180 deg in longitude (wrap-around dateline)
                    if (Math.Max(lon1, Math.Max(lon2, lon3)) - Math.Min(lon1, Math.Min(lon2, lon3)) > 180.0)
                        faceHidden[f] = true;
                }
            }

            // Terrain 3D view (sphere only): push vertices out by elevation so
            // mountains, ridges, and ocean basins read as real relief. Flat
            // projections keep terrain colors without displacement.
            bool terrainRelief = ViewMode == ViewMode.Terrain && !flat;
            if (terrainRelief)
            {
                for (int t = 0; t < tileCount; t++)
                {
                    float r = TileTerrainRadius(t);
                    tileX[t] *= r;
                    tileY[t] *= r;
                    tileZ[t] *= r;
                }
            }

            _vertexCount = faces.Length * 3;
            _meshTileCount = tileCount;

            _positions = new float[_vertexCount * 3];
            _normals = new float[_vertexCount * 3];
            _barycentric = new float[_vertexCount * 3];
            _colors = new float[_vertexCount * 3];
            _vertexTile = new int[_vertexCount];

            for (int f = 0; f < faces.Length; f++)
            {
                var face = faces[f];
                bool hidden = faceHidden != null && faceHidden[f];

                for (int k = 0; k < 3; k++)
                {
                    int tile = k == 0 ? face.v1 : (k == 1 ? face.v2 : face.v3);
                    int idx = f * 3 + k;

                    _vertexTile[idx] = tile;

                    if (hidden)
                    {
                        _positions[idx * 3] = float.NaN;
                        _positions[idx * 3 + 1] = float.NaN;
                        _positions[idx * 3 + 2] = float.NaN;
                    }
                    else
                    {
                        _positions[idx * 3] = tileX[tile];
                        _positions[idx * 3 + 1] = tileY[tile];
                        _positions[idx * 3 + 2] = tileZ[tile];
                    }

                    if (flat)
                    {
                        _normals[idx * 3] = 0f;
                        _normals[idx * 3 + 1] = 0f;
                        _normals[idx * 3 + 2] = 1f;
                    }
                    else
                    {
                        // Radial normal: exact for the unit sphere; Terrain relief
                        // overwrites these per-face below (D1); fallback for hidden faces.
                        float nx = tileX[tile], ny = tileY[tile], nz = tileZ[tile];
                        float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (len > 1e-6f) { nx /= len; ny /= len; nz /= len; }
                        _normals[idx * 3] = nx;
                        _normals[idx * 3 + 1] = ny;
                        _normals[idx * 3 + 2] = nz;
                    }

                    // Barycentric coordinates (unshared vertices for the edge shader)
                    _barycentric[idx * 3] = k == 0 ? 1f : 0f;
                    _barycentric[idx * 3 + 1] = k == 1 ? 1f : 0f;
                    _barycentric[idx * 3 + 2] = k == 2 ? 1f : 0f;

                    var color = tileColor[tile];
                    _colors[idx * 3] = color.X;
                    _colors[idx * 3 + 1] = color.Y;
                    _colors[idx * 3 + 2] = color.Z;
                }
            }

            // D1: true displaced normals for Terrain relief. Per-face normals
            // from the displaced positions (CPU, once per rebuild), oriented
            // outward against the face centroid so winding can't invert them.
            // Hidden (NaN) and degenerate faces keep the radial fallback above.
            if (terrainRelief)
            {
                for (int f = 0; f < faces.Length; f++)
                {
                    if (faceHidden != null && faceHidden[f]) continue;
                    int i0 = f * 3;
                    var pa = new Vector3(_positions[i0 * 3], _positions[i0 * 3 + 1], _positions[i0 * 3 + 2]);
                    var pb = new Vector3(_positions[i0 * 3 + 3], _positions[i0 * 3 + 4], _positions[i0 * 3 + 5]);
                    var pc = new Vector3(_positions[i0 * 3 + 6], _positions[i0 * 3 + 7], _positions[i0 * 3 + 8]);
                    var centroid = (pa + pb + pc) / 3f;
                    float cl = centroid.Length();
                    var radial = cl > 1e-6f ? centroid / cl : new Vector3(0f, 0f, 1f);
                    var n = TerrainNormals.Outward(TerrainNormals.FaceNormal(pa, pb, pc, radial), centroid);
                    for (int k = 0; k < 3; k++)
                    {
                        _normals[(i0 + k) * 3] = n.X;
                        _normals[(i0 + k) * 3 + 1] = n.Y;
                        _normals[(i0 + k) * 3 + 2] = n.Z;
                    }
                }
            }

            int[] buffers = new int[4];
            fixed (int* pBuffers = buffers)
            {
                gl.GenBuffers(4, pBuffers);
            }
            _vboPos = buffers[0];
            _vboNormal = buffers[1];
            _vboBary = buffers[2];
            _vboColor = buffers[3];

            int vao;
            gl.GenVertexArrays(1, &vao);
            _vao = vao;

            gl.BindVertexArray(_vao);

            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboPos);
            fixed (float* p = _positions)
            {
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_positions.Length * sizeof(float)), (IntPtr)p, GlConsts.GL_STATIC_DRAW);
            }
            gl.VertexAttribPointer(0, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(0);

            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboNormal);
            fixed (float* p = _normals)
            {
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_normals.Length * sizeof(float)), (IntPtr)p, GlConsts.GL_STATIC_DRAW);
            }
            gl.VertexAttribPointer(1, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(1);

            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboBary);
            fixed (float* p = _barycentric)
            {
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_barycentric.Length * sizeof(float)), (IntPtr)p, GlConsts.GL_STATIC_DRAW);
            }
            gl.VertexAttribPointer(2, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(2);

            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboColor);
            fixed (float* p = _colors)
            {
                int GL_DYNAMIC_DRAW = 0x88E8;
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_colors.Length * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);
            }
            gl.VertexAttribPointer(3, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(3);

            gl.BindVertexArray(0);
        }

        delegate void glUniform1i_t(int location, int v0);
        delegate void glUniform3f_t(int location, float v0, float v1, float v2);
        private glUniform1i_t? _glUniform1i;
        private glUniform3f_t? _glUniform3f;

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            if (_vao != 0)
            {
                int[] buffers = { _vboPos, _vboNormal, _vboBary, _vboColor };
                fixed (int* pBuffers = buffers)
                {
                    gl.DeleteBuffers(4, pBuffers);
                }
                int vao = _vao;
                gl.DeleteVertexArrays(1, &vao);
                _vao = _vboPos = _vboNormal = _vboBary = _vboColor = 0;
            }
            if (_shaderProgram != 0)
            {
                gl.DeleteProgram(_shaderProgram);
                _shaderProgram = 0;
            }
            _glUniform1i = null;
            _glUniform3f = null;
            base.OnOpenGlDeinit(gl);
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            if (_shaderProgram == 0)
                return; // Init already reported the build failure; don't draw with an invalid program.

            if (_needsMeshRebuild)
            {
                SetupMesh(gl);
                _needsMeshRebuild = false;
                _needsColorBufferUpdate = false;
            }
            else if (_needsColorBufferUpdate && _vboColor != 0)
            {
                gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboColor);
                fixed (float* p = _colors)
                {
                    int GL_DYNAMIC_DRAW = 0x88E8;
                    gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_colors.Length * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);
                }
                gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, 0);
                _needsColorBufferUpdate = false;
            }

            var scale = VisualRoot?.RenderScaling ?? 1.0;
            gl.Viewport(0, 0, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));

            gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
            gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT | GlConsts.GL_DEPTH_BUFFER_BIT);
            gl.Enable(GlConsts.GL_DEPTH_TEST);

            gl.UseProgram(_shaderProgram);

            float aspect = (float)(Bounds.Width / Bounds.Height);

            var projection = Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (float)Math.PI / 180.0f, aspect, 0.1f, 100.0f);
            var view = Matrix4x4.CreateTranslation(-PanX, -PanY, -Distance);
            var modelX = Matrix4x4.CreateRotationX(Pitch * (float)Math.PI / 180.0f);
            var modelY = Matrix4x4.CreateRotationY(Yaw * (float)Math.PI / 180.0f);

            var model = modelX * modelY;
            var viewProj = view * projection;
            var mvp = model * viewProj;

            float* modelPtr = stackalloc float[16];
            float* mvpPtr = stackalloc float[16];

            System.Runtime.CompilerServices.Unsafe.Write(modelPtr, model);
            System.Runtime.CompilerServices.Unsafe.Write(mvpPtr, mvp);

            gl.UniformMatrix4fv(_uMvpMatrix, 1, false, mvpPtr);
            gl.UniformMatrix4fv(_uModelMatrix, 1, false, modelPtr);

            var glUniform1i = _glUniform1i!;
            var glUniform3f = _glUniform3f!;

            bool terrain = ViewMode == ViewMode.Terrain;
            glUniform1i(_uShowPlates, ShowPlates ? 1 : 0);
            // Terrain view drops all hex/triangle edges by design.
            glUniform1i(_uShowHexes, ShowHexes && !terrain ? 1 : 0);
            glUniform1i(_uTerrainMode, terrain ? 1 : 0);
            glUniform3f(_uSelectedHexCenter, SelectedHexCenter.X, SelectedHexCenter.Y, SelectedHexCenter.Z);

            gl.BindVertexArray(_vao);

            int GL_TRIANGLES = 0x0004;
            gl.DrawArrays(GL_TRIANGLES, 0, _vertexCount);

            gl.BindVertexArray(0);
        }








        public bool TryPickHex(double mouseX, double mouseY, out double lat, out double lon, out int tileIndex)
        {
            lat = 0;
            lon = 0;
            tileIndex = -1;

            if (_positions == null || _positions.Length == 0) return false;

            // Terrain relief rises above the unit sphere (TerrainShading clamps
            // displacement to radius 1.38), so intersect a slightly larger sphere
            // or clicks on tall terrain near the limb would miss the pick entirely.
            float pickRadius = ViewMode == ViewMode.Terrain ? 1.42f : 1.0f;

            IProjection? projectionObj = ProjectionMode switch
            {
                ProjectionType.Equirectangular => new EquirectangularProjection(1.0),
                ProjectionType.Mercator => new MercatorProjection(1.0),
                ProjectionType.Gnomonic => new GnomonicProjection(1.0),
                _ => null
            };

            // All ray math lives in Presentation.HexPicker (pure, unit-tested).
            // mouseX/mouseY must already be in GlView.Bounds space (see pointer handlers).
            var camera = new PickCamera(Yaw, Pitch, PanX, PanY, Distance);
            if (!HexPicker.TryPick(mouseX, mouseY, Bounds.Width, Bounds.Height, camera,
                    pickRadius, _positions, projectionObj,
                    geo => _map.DataStore.GetTileIndex(geo), _map.DataStore.TileCount, out var pick))
                return false;

            lat = pick.Latitude;
            lon = pick.Longitude;
            tileIndex = pick.TileIndex;

            // Snap the highlight to the true tile center.
            var centerVec = RoguelikeToolkit.World.Core.Vector3D.FromGeoCoord(_map.DataStore.GetGeoCoord(tileIndex));
            SelectedHexCenter = new System.Numerics.Vector3((float)centerVec.X, (float)centerVec.Y, (float)centerVec.Z);
            if (ViewMode == ViewMode.Terrain && tileIndex >= 0)
                SelectedHexCenter *= TileTerrainRadius(tileIndex);

            RenderFrame();
            return true;
        }    }
}
