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
        Elevation
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
        private LocalMapLayer _localLayer = null!;
        private WorldGenerationPipeline _pipeline = null!;

        private const string VertexShaderSource = @"
            #version 330 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in vec3 aBary;
            layout (location = 3) in vec3 aColor;

            out vec3 FragPos;
            out vec3 Normal;
            out vec3 Barycentric;
            out vec3 VertexColor;
            out float vIsSelectedVertex;

            uniform mat4 uMvpMatrix;
            uniform mat4 uModelMatrix;
            uniform vec3 uSelectedHexCenter;

            void main()
            {
                gl_Position = uMvpMatrix * vec4(aPos, 1.0);
                FragPos = vec3(uModelMatrix * vec4(aPos, 1.0));

                // For a sphere, the normal is just the position
                // Also, no non-uniform scaling, so we can just use the model matrix
                Normal = mat3(uModelMatrix) * aNormal;

                Barycentric = aBary;
                VertexColor = aColor;

                float dist = distance(aPos, uSelectedHexCenter);
                if (dist < 0.001) {
                    vIsSelectedVertex = 1.0;
                } else {
                    vIsSelectedVertex = 0.0;
                }
            }
        ";

        private const string FragmentShaderSource = @"
            #version 330 core
            #extension GL_OES_standard_derivatives : enable

            in vec3 FragPos;
            in vec3 Normal;
            in vec3 Barycentric;
            in vec3 VertexColor;
            in float vIsSelectedVertex;

            out vec4 FragColor;

            uniform int uShowPlates;
            uniform int uShowHexes;

            void main()
            {
                // Simple directional light
                vec3 norm = normalize(Normal);
                vec3 lightDir = normalize(vec3(1.0, 1.0, 1.0));

                // Ambient + diffuse
                float ambient = 0.2;
                float diff = max(dot(norm, lightDir), 0.0);
                float lightIntensity = ambient + diff * 0.8;

                // Base color
                vec3 baseColor = uShowPlates == 1 ? VertexColor : vec3(0.3, 0.3, 0.35);
                baseColor *= lightIntensity;

                // Highlight selected hex
                float maxBary = max(max(Barycentric.x, Barycentric.y), Barycentric.z);
                bool isFragmentInSelectedHex = (vIsSelectedVertex > 0.5 && Barycentric.x == maxBary) ||
                                               (vIsSelectedVertex > 0.5 && Barycentric.y == maxBary) ||
                                               (vIsSelectedVertex > 0.5 && Barycentric.z == maxBary);

                if (vIsSelectedVertex >= maxBary - 0.0001)
                {
                    baseColor = mix(baseColor, vec3(1.0, 1.0, 0.0), 0.5); // Highlight with yellow
                }

                float edgeFactor = 1.0;

                if (uShowHexes == 1) {
                    float b1, b2, b3;
                    if (Barycentric.x > Barycentric.y) {
                        if (Barycentric.x > Barycentric.z) { b1 = Barycentric.x; b2 = max(Barycentric.y, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.x; }
                    } else {
                        if (Barycentric.y > Barycentric.z) { b1 = Barycentric.y; b2 = max(Barycentric.x, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.y; }
                    }

                    float val = b1 - b2;
                    float d = fwidth(val);
                    edgeFactor = smoothstep(0.0, d * 1.5, val);
                } else {
                    vec3 d = fwidth(Barycentric);
                    vec3 a3 = smoothstep(vec3(0.0), d * 1.5, Barycentric);
                    edgeFactor = min(min(a3.x, a3.y), a3.z);
                }

                vec3 edgeColor = vec3(0.6, 0.6, 0.6);

                vec3 finalColor = mix(edgeColor, baseColor, edgeFactor);

                FragColor = vec4(finalColor, 1.0);
            }
        ";

        public GlControl()
        {
            InitializeLayers();
        }

        private void InitializeLayers()
        {
            // Clean up existing map and pipeline if they exist
            _map?.Dispose();
            _pipeline?.Dispose();

            int size = RecursionLevel;
            _map = new WorldMap(size);

            int seedCount = _plateLayer?.SeedCount ?? 12;

            _plateLayer = new TectonicPlateLayer(_map.DataStore, seedCount);
            _map.RegisterLayer(_plateLayer);

            _elevLayer = new ElevationLayer(_map.DataStore);
            _map.RegisterLayer(_elevLayer);

            _localLayer = new LocalMapLayer(_map.DataStore, 42, _plateLayer);
            _map.RegisterLayer(_localLayer);

            _map.DataStore.Allocate();

            _pipeline = new WorldGenerationPipeline();
            _pipeline.Discover("Plugins"); // Try to discover external plugins if any

            // Set params on stages before execution
            foreach (var seeded in _pipeline.Stages.OfType<ISeededStage>())
            {
                seeded.Seed = WorldSeed;
            }

            var tectonicStage = _pipeline.Stages.OfType<TectonicPlateGenerationStage>().FirstOrDefault();
            if (tectonicStage != null)
            {
                tectonicStage.SeedCount = seedCount;
            }

            var sw = Stopwatch.StartNew();
            _pipeline.Execute(_map);
            sw.Stop();
            LastGenMs = sw.ElapsedMilliseconds;

            UpdateStatus();
        }

        public void SetRecursionLevel(int level)
        {
            if (level == RecursionLevel) return;
            RecursionLevel = level;

            // Debounced: slider drags rebuild the whole world; wait for the user to settle.
            Debounce(() =>
            {
                InitializeLayers();

                _needsMeshRebuild = true;
                RenderFrame();
            });
        }

        public void SetPlateCount(int count)
        {
            if (_plateLayer == null || count == _plateLayer.SeedCount) return;

            _plateLayer.SeedCount = count;

            var tectonicStage = _pipeline.Stages.OfType<TectonicPlateGenerationStage>().FirstOrDefault();
            if (tectonicStage != null)
            {
                tectonicStage.SeedCount = count;
            }

            // Debounced: slider drags re-run the pipeline; wait for the user to settle.
            Debounce(() =>
            {
                var sw = Stopwatch.StartNew();
                _pipeline.Execute(_map);
                sw.Stop();
                LastGenMs = sw.ElapsedMilliseconds;

                Recolor();
                UpdateStatus();
            });
        }

        public void Regenerate(int seed)
        {
            WorldSeed = seed;

            foreach (var seeded in _pipeline.Stages.OfType<ISeededStage>())
            {
                seeded.Seed = seed;
            }

            var tectonicStage = _pipeline.Stages.OfType<TectonicPlateGenerationStage>().FirstOrDefault();
            if (tectonicStage != null)
            {
                tectonicStage.SeedCount = _plateLayer.SeedCount;
            }

            var sw = Stopwatch.StartNew();
            _pipeline.Execute(_map);
            sw.Stop();
            LastGenMs = sw.ElapsedMilliseconds;

            _needsMeshRebuild = true;
            UpdateStatus();
            RenderFrame();
        }

        public void SetColorMode(ColorMode mode)
        {
            if (mode == ColorMode) return;
            ColorMode = mode;
            Recolor();
            UpdateStatus();
        }

        private void Recolor()
        {
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
                var color = TileDebugColor(plates[tile].Id, locals[tile].Biome, heights[tile].Height);
                _colors[i * 3] = color.X;
                _colors[i * 3 + 1] = color.Y;
                _colors[i * 3 + 2] = color.Z;
            }

            _needsColorBufferUpdate = true;
            RenderFrame();
        }

        private System.Numerics.Vector3 TileDebugColor(int plateId, BiomeType biome, float height)
            => ColorMode switch
            {
                ColorMode.Biome => BiomePalette.ColorFor(biome),
                ColorMode.Elevation => ElevationPalette.ColorFor(height),
                _ => PlatePalette.ColorFor(plateId),
            };

        private void UpdateStatus()
        {
            StatusText = $"Seed {WorldSeed} | Tiles {TileCount:N0} | Gen {LastGenMs} ms | Plates {_plateLayer.SeedCount}";
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
                    pending?.Invoke();
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

            int vertexShader = gl.CreateShader(GlConsts.GL_VERTEX_SHADER);
            gl.ShaderSourceString(vertexShader, VertexShaderSource);
            gl.CompileShader(vertexShader);
            CheckShaderCompilation(gl, vertexShader);

            int fragmentShader = gl.CreateShader(GlConsts.GL_FRAGMENT_SHADER);
            gl.ShaderSourceString(fragmentShader, FragmentShaderSource);
            gl.CompileShader(fragmentShader);
            CheckShaderCompilation(gl, fragmentShader);

            _shaderProgram = gl.CreateProgram();
            gl.AttachShader(_shaderProgram, vertexShader);
            gl.AttachShader(_shaderProgram, fragmentShader);
            gl.LinkProgram(_shaderProgram);

            int linkStatus;
            gl.GetProgramiv(_shaderProgram, GlConsts.GL_LINK_STATUS, &linkStatus);
            if (linkStatus == 0)
            {
                int maxLength;
                gl.GetProgramiv(_shaderProgram, GlConsts.GL_INFO_LOG_LENGTH, &maxLength);
                byte* infoLog = stackalloc byte[maxLength];
                gl.GetProgramInfoLog(_shaderProgram, maxLength, out int length, infoLog);
                var msg = $"Shader program link error: {Marshal.PtrToStringAnsi((IntPtr)infoLog)}";
                Console.WriteLine(msg);
                OnDiagnostic?.Invoke(msg);
            }

            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);

            _uMvpMatrix = gl.GetUniformLocationString(_shaderProgram, "uMvpMatrix");
            _uModelMatrix = gl.GetUniformLocationString(_shaderProgram, "uModelMatrix");
            _uShowPlates = gl.GetUniformLocationString(_shaderProgram, "uShowPlates");
            _uShowHexes = gl.GetUniformLocationString(_shaderProgram, "uShowHexes");
            _uSelectedHexCenter = gl.GetUniformLocationString(_shaderProgram, "uSelectedHexCenter");

            SetupMesh(gl);
        }

        private void CheckShaderCompilation(GlInterface gl, int shader)
        {
            int success;
            gl.GetShaderiv(shader, GlConsts.GL_COMPILE_STATUS, &success);
            if (success == 0)
            {
                int maxLength;
                gl.GetShaderiv(shader, GlConsts.GL_INFO_LOG_LENGTH, &maxLength);
                byte* infoLog = stackalloc byte[maxLength];
                gl.GetShaderInfoLog(shader, maxLength, out int length, infoLog);
                var msg = $"Shader compile error: {Marshal.PtrToStringAnsi((IntPtr)infoLog)}";
                Console.WriteLine(msg);
                OnDiagnostic?.Invoke(msg);
            }
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
            IcosphereGenerator.Generate(RecursionLevel, out RoguelikeToolkit.World.Core.Vector3D[] tileVerts, out TriangleIndices[] faces);
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
                tileColor[t] = TileDebugColor(plates[t].Id, locals[t].Biome, heights[t].Height);
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
                        _normals[idx * 3] = tileX[tile];
                        _normals[idx * 3 + 1] = tileY[tile];
                        _normals[idx * 3 + 2] = tileZ[tile];
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

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
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

            var glUniform1i = Marshal.GetDelegateForFunctionPointer<glUniform1i_t>(gl.GetProcAddress("glUniform1i"));
            var glUniform3f = Marshal.GetDelegateForFunctionPointer<glUniform3f_t>(gl.GetProcAddress("glUniform3f"));

            glUniform1i(_uShowPlates, ShowPlates ? 1 : 0);
            glUniform1i(_uShowHexes, ShowHexes ? 1 : 0);
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

            float aspect = (float)(Bounds.Width / Bounds.Height);

            var projection = Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (float)Math.PI / 180.0f, aspect, 0.1f, 100.0f);
            var view = Matrix4x4.CreateTranslation(-PanX, -PanY, -Distance);
            var modelX = Matrix4x4.CreateRotationX(Pitch * (float)Math.PI / 180.0f);
            var modelY = Matrix4x4.CreateRotationY(Yaw * (float)Math.PI / 180.0f);

            var model = modelX * modelY;
            var viewProj = view * projection;
            var mvp = model * viewProj;

            if (!Matrix4x4.Invert(mvp, out Matrix4x4 invMvp)) return false;

            // NDC Coordinates
            float ndcX = (float)((2.0 * mouseX) / Bounds.Width - 1.0);
            float ndcY = (float)(1.0 - (2.0 * mouseY) / Bounds.Height); // Invert Y

            Vector4 rayClipNear = new Vector4(ndcX, ndcY, -1.0f, 1.0f);
            Vector4 rayClipFar = new Vector4(ndcX, ndcY, 1.0f, 1.0f);

            Vector4 rayObjNearV = Vector4.Transform(rayClipNear, invMvp);
            Vector4 rayObjFarV = Vector4.Transform(rayClipFar, invMvp);

            if (rayObjNearV.W != 0.0f) { rayObjNearV.X /= rayObjNearV.W; rayObjNearV.Y /= rayObjNearV.W; rayObjNearV.Z /= rayObjNearV.W; }
            if (rayObjFarV.W != 0.0f) { rayObjFarV.X /= rayObjFarV.W; rayObjFarV.Y /= rayObjFarV.W; rayObjFarV.Z /= rayObjFarV.W; }

            float[] rayObjNear = new float[] { rayObjNearV.X, rayObjNearV.Y, rayObjNearV.Z, rayObjNearV.W };
            float[] rayObjFar = new float[] { rayObjFarV.X, rayObjFarV.Y, rayObjFarV.Z, rayObjFarV.W };

            float rayDirX = rayObjFar[0] - rayObjNear[0];
            float rayDirY = rayObjFar[1] - rayObjNear[1];
            float rayDirZ = rayObjFar[2] - rayObjNear[2];

            float len = (float)Math.Sqrt(rayDirX * rayDirX + rayDirY * rayDirY + rayDirZ * rayDirZ);
            rayDirX /= len; rayDirY /= len; rayDirZ /= len;

            float a = rayDirX * rayDirX + rayDirY * rayDirY + rayDirZ * rayDirZ;
            float b = 2.0f * (rayDirX * rayObjNear[0] + rayDirY * rayObjNear[1] + rayDirZ * rayObjNear[2]);
            float c = (rayObjNear[0] * rayObjNear[0] + rayObjNear[1] * rayObjNear[1] + rayObjNear[2] * rayObjNear[2]) - 1.0f;

            float discriminant = b * b - 4 * a * c;

            if (discriminant < 0) return false;

            float t = (-b - (float)Math.Sqrt(discriminant)) / (2.0f * a);
            if (t < 0) return false;

            float hitX = rayObjNear[0] + t * rayDirX;
            float hitY = rayObjNear[1] + t * rayDirY;
            float hitZ = rayObjNear[2] + t * rayDirZ;

            float minDistsq = float.MaxValue;
            int nearestIdx = -1;

            for (int i = 0; i < _positions.Length / 3; i++)
            {
                float dx = _positions[i * 3] - hitX;
                float dy = _positions[i * 3 + 1] - hitY;
                float dz = _positions[i * 3 + 2] - hitZ;
                float distSq = dx * dx + dy * dy + dz * dz;

                if (distSq < minDistsq)
                {
                    minDistsq = distSq;
                    nearestIdx = i;
                }
            }

            if (nearestIdx != -1)
            {
                SelectedHexCenter = new System.Numerics.Vector3(
                    _positions[nearestIdx * 3],
                    _positions[nearestIdx * 3 + 1],
                    _positions[nearestIdx * 3 + 2]
                );

                IProjection? projectionObj = ProjectionMode switch
                {
                    ProjectionType.Equirectangular => new EquirectangularProjection(1.0),
                    ProjectionType.Mercator => new MercatorProjection(1.0),
                    ProjectionType.Gnomonic => new GnomonicProjection(1.0),
                    _ => null
                };

                if (projectionObj != null)
                {
                    var geo = projectionObj.Inverse(new Vector2D(SelectedHexCenter.X, SelectedHexCenter.Y));
                    lat = geo.Latitude;
                    lon = geo.Longitude;
                }
                else
                {
                    lat = Math.Asin(SelectedHexCenter.Z) * 180.0 / Math.PI;
                    lon = Math.Atan2(SelectedHexCenter.Y, SelectedHexCenter.X) * 180.0 / Math.PI;
                }

                // Resolve the tile through the store's canonical topology (exact nearest-center
                // search). The previous ring-based heuristic disagreed with the store on
                // ~160/162 tiles at size 2 and routinely displayed the wrong plate/biome.
                tileIndex = _map.DataStore.GetTileIndex(new GeoCoord(lat, lon));

                // Snap the highlight to the true tile center.
                var centerVec = RoguelikeToolkit.World.Core.Vector3D.FromGeoCoord(_map.DataStore.GetGeoCoord(tileIndex));
                SelectedHexCenter = new System.Numerics.Vector3((float)centerVec.X, (float)centerVec.Y, (float)centerVec.Z);

                RenderFrame();
                return true;
            }

            return false;
        }
    }
}
