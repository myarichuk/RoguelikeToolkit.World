using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
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

        private WorldMap _map = null!;
        private TectonicPlateLayer _plateLayer = null!;
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
            // Clean up existing map if it exists
            _map?.Dispose();

            int size = RecursionLevel;
            _map = new WorldMap(size);

            int seedCount = _plateLayer?.SeedCount ?? 12;

            _plateLayer = new TectonicPlateLayer(_map.DataStore, seedCount);
            _map.RegisterLayer(_plateLayer);

            _localLayer = new LocalMapLayer(_map.DataStore, 42, _plateLayer);
            _map.RegisterLayer(_localLayer);

            _map.DataStore.Allocate();

            _pipeline = new WorldGenerationPipeline();
            _pipeline.Discover("Plugins"); // Try to discover external plugins if any

            // Set params on stages before execution
            var tectonicStage = _pipeline.Stages.OfType<TectonicPlateGenerationStage>().FirstOrDefault();
            if (tectonicStage != null)
            {
                tectonicStage.SeedCount = seedCount;
            }

            _pipeline.Execute(_map);
        }

        public void SetRecursionLevel(int level)
        {
            if (level == RecursionLevel) return;
            RecursionLevel = level;

            InitializeLayers();

            _needsMeshRebuild = true;
            RenderFrame();
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

            _pipeline.Execute(_map);

            Random rnd = new Random(42);
            var plateColors = new System.Collections.Generic.Dictionary<int, System.Numerics.Vector3>();

            IProjection? projectionObj = ProjectionMode switch
            {
                ProjectionType.Equirectangular => new EquirectangularProjection(1.0),
                ProjectionType.Mercator => new MercatorProjection(1.0),
                ProjectionType.Gnomonic => new GnomonicProjection(1.0),
                _ => null
            };

            for (int i = 0; i < _vertexCount; i++)
            {
                double lat, lon;
                if (projectionObj != null)
                {
                    if (float.IsNaN(_positions[i * 3]))
                    {
                        lat = 0; lon = 0;
                    }
                    else
                    {
                        var geo = projectionObj.Inverse(new Vector2D(_positions[i * 3], _positions[i * 3 + 1]));
                        lat = geo.Latitude;
                        lon = geo.Longitude;
                    }
                }
                else
                {
                    lat = Math.Asin(_positions[i * 3 + 2]) * 180.0 / Math.PI;
                    lon = Math.Atan2(_positions[i * 3 + 1], _positions[i * 3]) * 180.0 / Math.PI;
                }

                var coord = new GeoCoord(lat, lon);
                var plate = _plateLayer.GetValue(coord);

                if (!plateColors.TryGetValue(plate.Id, out var color))
                {
                    color = new System.Numerics.Vector3(
                        (float)rnd.NextDouble(),
                        (float)rnd.NextDouble(),
                        (float)rnd.NextDouble());
                    plateColors[plate.Id] = color;
                }

                _colors[i * 3] = color.X;
                _colors[i * 3 + 1] = color.Y;
                _colors[i * 3 + 2] = color.Z;
            }

            _needsColorBufferUpdate = true;
            RenderFrame();
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
                Console.WriteLine($"Shader program link error: {Marshal.PtrToStringAnsi((IntPtr)infoLog)}");
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
                Console.WriteLine($"Shader compile error: {Marshal.PtrToStringAnsi((IntPtr)infoLog)}");
            }
        }

        private void SetupMesh(GlInterface gl)
        {
            System.Numerics.Vector3[] vPos;
            System.Numerics.Vector3[] vNorm;
            System.Numerics.Vector3[] vBary;

            IcosphereGenerator.GenerateFlat(RecursionLevel, out vPos, out vNorm, out vBary);

            IProjection? projectionObj = ProjectionMode switch
            {
                ProjectionType.Equirectangular => new EquirectangularProjection(1.0),
                ProjectionType.Mercator => new MercatorProjection(1.0),
                ProjectionType.Gnomonic => new GnomonicProjection(1.0),
                _ => null
            };

            // Calculate projections
            if (projectionObj != null)
            {
                for (int i = 0; i < vPos.Length; i += 3)
                {
                    var p1 = vPos[i];
                    var p2 = vPos[i + 1];
                    var p3 = vPos[i + 2];

                    double lat1 = Math.Asin(p1.Z) * 180.0 / Math.PI;
                    double lon1 = Math.Atan2(p1.Y, p1.X) * 180.0 / Math.PI;
                    double lat2 = Math.Asin(p2.Z) * 180.0 / Math.PI;
                    double lon2 = Math.Atan2(p2.Y, p2.X) * 180.0 / Math.PI;
                    double lat3 = Math.Asin(p3.Z) * 180.0 / Math.PI;
                    double lon3 = Math.Atan2(p3.Y, p3.X) * 180.0 / Math.PI;

                    // Hide triangles spanning more than 180 deg in longitude (wrap-around dateline)
                    if (Math.Max(lon1, Math.Max(lon2, lon3)) - Math.Min(lon1, Math.Min(lon2, lon3)) > 180.0)
                    {
                        vPos[i] = new Vector3(float.NaN, float.NaN, float.NaN);
                        vPos[i + 1] = new Vector3(float.NaN, float.NaN, float.NaN);
                        vPos[i + 2] = new Vector3(float.NaN, float.NaN, float.NaN);
                        continue;
                    }

                    var c1 = projectionObj.Project(new GeoCoord(lat1, lon1));
                    var c2 = projectionObj.Project(new GeoCoord(lat2, lon2));
                    var c3 = projectionObj.Project(new GeoCoord(lat3, lon3));

                    vPos[i] = new Vector3((float)c1.X, (float)c1.Y, 0);
                    vPos[i + 1] = new Vector3((float)c2.X, (float)c2.Y, 0);
                    vPos[i + 2] = new Vector3((float)c3.X, (float)c3.Y, 0);

                    vNorm[i] = new Vector3(0, 0, 1);
                    vNorm[i + 1] = new Vector3(0, 0, 1);
                    vNorm[i + 2] = new Vector3(0, 0, 1);
                }
            }

            _vertexCount = vPos.Length;

            _positions = new float[_vertexCount * 3];
            _normals = new float[_vertexCount * 3];
            _barycentric = new float[_vertexCount * 3];
            _colors = new float[_vertexCount * 3];

            Random rnd = new Random(42);
            var plateColors = new System.Collections.Generic.Dictionary<int, System.Numerics.Vector3>();

            for (int i = 0; i < _vertexCount; i++)
            {
                _positions[i * 3] = vPos[i].X;
                _positions[i * 3 + 1] = vPos[i].Y;
                _positions[i * 3 + 2] = vPos[i].Z;

                _normals[i * 3] = vNorm[i].X;
                _normals[i * 3 + 1] = vNorm[i].Y;
                _normals[i * 3 + 2] = vNorm[i].Z;

                _barycentric[i * 3] = vBary[i].X;
                _barycentric[i * 3 + 1] = vBary[i].Y;
                _barycentric[i * 3 + 2] = vBary[i].Z;

                // Even in 2D projection, we still need to assign colors based on their original latitude/longitude
                // which unfortunately we lost by projecting.
                // We'll calculate lat/lon here depending on projection, or just store the original position temporarily.
                double lat, lon;
                if (projectionObj != null)
                {
                    if (float.IsNaN(vPos[i].X))
                    {
                        lat = 0; lon = 0;
                    }
                    else
                    {
                        var geo = projectionObj.Inverse(new Vector2D(vPos[i].X, vPos[i].Y));
                        lat = geo.Latitude;
                        lon = geo.Longitude;
                    }
                }
                else
                {
                    lat = Math.Asin(vPos[i].Z) * 180.0 / Math.PI;
                    lon = Math.Atan2(vPos[i].Y, vPos[i].X) * 180.0 / Math.PI;
                }

                var coord = new GeoCoord(lat, lon);
                var plate = _plateLayer.GetValue(coord);

                if (!plateColors.TryGetValue(plate.Id, out var color))
                {
                    color = new System.Numerics.Vector3(
                        (float)rnd.NextDouble(),
                        (float)rnd.NextDouble(),
                        (float)rnd.NextDouble());
                    plateColors[plate.Id] = color;
                }

                _colors[i * 3] = color.X;
                _colors[i * 3 + 1] = color.Y;
                _colors[i * 3 + 2] = color.Z;
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
                int[] buffers = new int[] { _vboPos, _vboNormal, _vboBary, _vboColor };
                fixed (int* pBuffers = buffers)
                {
                    gl.DeleteBuffers(4, pBuffers);
                }

                int vao = _vao;
                gl.DeleteVertexArrays(1, &vao);

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

                double normalizedLon = (lon + 180.0) / 360.0;
                double normalizedLat = (lat + 90.0) / 180.0;
                if (normalizedLon < 0) normalizedLon = 0;
                if (normalizedLon >= 1) normalizedLon = 0.999999;
                if (normalizedLat < 0) normalizedLat = 0;
                if (normalizedLat >= 1) normalizedLat = 0.999999;

                int size = RecursionLevel;
                int tileCount = WorldDataStore.GetTileCount(size);
                int rings = (1 << size) * 3;
                if (rings == 0) rings = 3;
                int ringIndex = (int)(normalizedLat * rings);
                int tilesInRing = tileCount / rings;
                if (tilesInRing == 0) tilesInRing = 1;
                int tileInRing = (int)(normalizedLon * tilesInRing);
                tileIndex = ringIndex * tilesInRing + tileInRing;
                if (tileIndex >= tileCount) tileIndex = tileCount - 1;

                RenderFrame();
                return true;
            }

            return false;
        }
    }
}
