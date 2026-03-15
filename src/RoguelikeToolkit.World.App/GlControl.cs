using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.App
{
    public unsafe class GlControl : OpenGlControlBase
    {
        public float Yaw { get; set; } = 0f;
        public float Pitch { get; set; } = 0f;
        public float Distance { get; set; } = 2.2f;
        public float PanX { get; set; } = 0f;
        public float PanY { get; set; } = 0f;
        public bool ShowPlates { get; set; } = true;
        public bool ShowHexes { get; set; } = false;
        public RoguelikeToolkit.World.App.Vector3 SelectedHexCenter { get; set; } = new RoguelikeToolkit.World.App.Vector3(0, 0, 0);


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

        private TectonicPlateOverlay _plateOverlay;

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

                // We use vIsSelectedVertex passed from the vertex. Because barycentric coordinates
                // linearly interpolate, the fragment's max barycentric component corresponds to the
                // vertex that is closest. If that closest vertex is the selected one, then
                // we are inside the hex.
                // To do this right, we evaluate whether vIsSelectedVertex corresponds to the max component.
                // Wait, vIsSelectedVertex is interpolated. We should instead check distance or something.
                // Actually, vIsSelectedVertex will be 1.0 at the selected vertex, and 0.0 at the others.
                // So inside the triangle, it interpolates. The barycentric coordinate for that vertex
                // exactly equals vIsSelectedVertex!
                // So the fragment is in the selected hex if its vIsSelectedVertex is the maximum among the 3 barycentric coords.

                if (vIsSelectedVertex >= maxBary - 0.0001)
                {
                    baseColor = mix(baseColor, vec3(1.0, 1.0, 0.0), 0.5); // Highlight with yellow
                }

                float edgeFactor = 1.0;

                if (uShowHexes == 1) {
                    // Hex border (dual mesh) detection
                    // The boundary between two hexes is where the two largest barycentric coordinates are equal.

                    float b1, b2, b3;
                    if (Barycentric.x > Barycentric.y) {
                        if (Barycentric.x > Barycentric.z) { b1 = Barycentric.x; b2 = max(Barycentric.y, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.x; }
                    } else {
                        if (Barycentric.y > Barycentric.z) { b1 = Barycentric.y; b2 = max(Barycentric.x, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.y; }
                    }

                    // Hex border is when difference between largest and second largest is 0
                    float val = b1 - b2;
                    float d = fwidth(val);
                    edgeFactor = smoothstep(0.0, d * 1.5, val);
                } else {
                    // Triangle edge detection (Wireframe) using fwidth
                    vec3 d = fwidth(Barycentric);
                    vec3 a3 = smoothstep(vec3(0.0), d * 1.5, Barycentric);
                    edgeFactor = min(min(a3.x, a3.y), a3.z);
                }

                vec3 edgeColor = vec3(0.6, 0.6, 0.6);

                // Blend edge and base color
                vec3 finalColor = mix(edgeColor, baseColor, edgeFactor);

                FragColor = vec4(finalColor, 1.0);
            }
        ";

        public GlControl()
        {
            _plateOverlay = new TectonicPlateOverlay(5, 12);
            _plateOverlay.Generate();
        }

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
            int recursionLevel = 4;
            RoguelikeToolkit.World.App.Vector3[] vPos;
            RoguelikeToolkit.World.App.Vector3[] vNorm;
            RoguelikeToolkit.World.App.Vector3[] vBary;

            IcosphereGenerator.GenerateFlat(recursionLevel, out vPos, out vNorm, out vBary);

            _vertexCount = vPos.Length;

            _positions = new float[_vertexCount * 3];
            _normals = new float[_vertexCount * 3];
            _barycentric = new float[_vertexCount * 3];
            _colors = new float[_vertexCount * 3];

            Random rnd = new Random(42);
            var plateColors = new System.Collections.Generic.Dictionary<int, RoguelikeToolkit.World.App.Vector3>();

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

                // Tectonic plate coloring
                double lat = Math.Asin(vPos[i].Z) * 180.0 / Math.PI;
                double lon = Math.Atan2(vPos[i].Y, vPos[i].X) * 180.0 / Math.PI;

                var coord = new GeoCoord(lat, lon);
                var plate = _plateOverlay.GetValue(coord);

                if (!plateColors.TryGetValue(plate.Id, out var color))
                {
                    color = new RoguelikeToolkit.World.App.Vector3(
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

            // Position
            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboPos);
            fixed (float* p = _positions)
            {
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_positions.Length * sizeof(float)), (IntPtr)p, GlConsts.GL_STATIC_DRAW);
            }
            gl.VertexAttribPointer(0, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(0);

            // Normal
            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboNormal);
            fixed (float* p = _normals)
            {
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_normals.Length * sizeof(float)), (IntPtr)p, GlConsts.GL_STATIC_DRAW);
            }
            gl.VertexAttribPointer(1, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(1);

            // Barycentric
            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboBary);
            fixed (float* p = _barycentric)
            {
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_barycentric.Length * sizeof(float)), (IntPtr)p, GlConsts.GL_STATIC_DRAW);
            }
            gl.VertexAttribPointer(2, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(2);

            // Color
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
            var scale = VisualRoot?.RenderScaling ?? 1.0;
            gl.Viewport(0, 0, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));

            gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
            gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT | GlConsts.GL_DEPTH_BUFFER_BIT);
            gl.Enable(GlConsts.GL_DEPTH_TEST);

            gl.UseProgram(_shaderProgram);

            float aspect = (float)(Bounds.Width / Bounds.Height);
            var projection = CreatePerspective(45.0f, aspect, 0.1f, 100.0f);
            var view = CreateTranslation(-PanX, -PanY, -Distance);
            var modelX = CreateRotationX(Pitch);
            var modelY = CreateRotationY(Yaw);
            var model = MultiplyMatrix(modelX, modelY);
            var viewProj = MultiplyMatrix(projection, view);
            var mvp = MultiplyMatrix(viewProj, model);

            fixed (float* pMvp = mvp)
            {
                gl.UniformMatrix4fv(_uMvpMatrix, 1, false, pMvp);
            }

            fixed (float* pModel = model)
            {
                gl.UniformMatrix4fv(_uModelMatrix, 1, false, pModel);
            }

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

        private float[] CreatePerspective(float fov, float aspect, float zNear, float zFar)
        {
            float[] result = new float[16];
            float tanHalfFovy = (float)Math.Tan(fov / 2.0f * Math.PI / 180.0f);

            result[0] = 1.0f / (aspect * tanHalfFovy);
            result[5] = 1.0f / (tanHalfFovy);
            result[10] = -(zFar + zNear) / (zFar - zNear);
            result[11] = -1.0f;
            result[14] = -(2.0f * zFar * zNear) / (zFar - zNear);

            return result;
        }

        private float[] CreateTranslation(float x, float y, float z)
        {
            return new float[] {
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                x, y, z, 1
            };
        }

        private float[] CreateRotationX(float angleDegrees)
        {
            float angle = angleDegrees * (float)Math.PI / 180.0f;
            float c = (float)Math.Cos(angle);
            float s = (float)Math.Sin(angle);

            return new float[] {
                1, 0, 0, 0,
                0, c, s, 0,
                0, -s, c, 0,
                0, 0, 0, 1
            };
        }

        private float[] CreateRotationY(float angleDegrees)
        {
            float angle = angleDegrees * (float)Math.PI / 180.0f;
            float c = (float)Math.Cos(angle);
            float s = (float)Math.Sin(angle);

            return new float[] {
                c, 0, -s, 0,
                0, 1, 0, 0,
                s, 0, c, 0,
                0, 0, 0, 1
            };
        }

        private float[] MultiplyMatrix(float[] a, float[] b)
        {
            float[] result = new float[16];
            for (int c = 0; c < 4; c++)
            {
                for (int r = 0; r < 4; r++)
                {
                    result[c * 4 + r] =
                        a[r] * b[c * 4] +
                        a[r + 4] * b[c * 4 + 1] +
                        a[r + 8] * b[c * 4 + 2] +
                        a[r + 12] * b[c * 4 + 3];
                }
            }
            return result;
        }

        private float[] InvertMatrix(float[] m)
        {
            float[] inv = new float[16];

            inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
            inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
            inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
            inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
            inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
            inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
            inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
            inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
            inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
            inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
            inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
            inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
            inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
            inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
            inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
            inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];

            float det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
            if (det == 0) return inv;

            det = 1.0f / det;
            for (int i = 0; i < 16; i++)
            {
                inv[i] = inv[i] * det;
            }

            return inv;
        }

        private float[] MultiplyMatrixVector(float[] matrix, float[] vector)
        {
            float[] result = new float[4];
            result[0] = matrix[0] * vector[0] + matrix[4] * vector[1] + matrix[8] * vector[2] + matrix[12] * vector[3];
            result[1] = matrix[1] * vector[0] + matrix[5] * vector[1] + matrix[9] * vector[2] + matrix[13] * vector[3];
            result[2] = matrix[2] * vector[0] + matrix[6] * vector[1] + matrix[10] * vector[2] + matrix[14] * vector[3];
            result[3] = matrix[3] * vector[0] + matrix[7] * vector[1] + matrix[11] * vector[2] + matrix[15] * vector[3];
            return result;
        }

        public bool TryPickHex(double mouseX, double mouseY, out double lat, out double lon, out int tileIndex)
        {
            lat = 0;
            lon = 0;
            tileIndex = -1;

            if (_positions == null || _positions.Length == 0) return false;

            float aspect = (float)(Bounds.Width / Bounds.Height);
            var projection = CreatePerspective(45.0f, aspect, 0.1f, 100.0f);
            var view = CreateTranslation(-PanX, -PanY, -Distance);
            var modelX = CreateRotationX(Pitch);
            var modelY = CreateRotationY(Yaw);
            var model = MultiplyMatrix(modelX, modelY);
            var viewProj = MultiplyMatrix(projection, view);
            var mvp = MultiplyMatrix(viewProj, model);

            var invMvp = InvertMatrix(mvp);

            // NDC Coordinates
            float ndcX = (float)((2.0 * mouseX) / Bounds.Width - 1.0);
            float ndcY = (float)(1.0 - (2.0 * mouseY) / Bounds.Height); // Invert Y

            float[] rayClipNear = new float[] { ndcX, ndcY, -1.0f, 1.0f };
            float[] rayClipFar = new float[] { ndcX, ndcY, 1.0f, 1.0f };

            float[] rayObjNear = MultiplyMatrixVector(invMvp, rayClipNear);
            float[] rayObjFar = MultiplyMatrixVector(invMvp, rayClipFar);

            if (rayObjNear[3] != 0.0f) { rayObjNear[0] /= rayObjNear[3]; rayObjNear[1] /= rayObjNear[3]; rayObjNear[2] /= rayObjNear[3]; }
            if (rayObjFar[3] != 0.0f) { rayObjFar[0] /= rayObjFar[3]; rayObjFar[1] /= rayObjFar[3]; rayObjFar[2] /= rayObjFar[3]; }

            float rayDirX = rayObjFar[0] - rayObjNear[0];
            float rayDirY = rayObjFar[1] - rayObjNear[1];
            float rayDirZ = rayObjFar[2] - rayObjNear[2];

            float len = (float)Math.Sqrt(rayDirX * rayDirX + rayDirY * rayDirY + rayDirZ * rayDirZ);
            rayDirX /= len; rayDirY /= len; rayDirZ /= len;

            // Sphere intersection (rayObjNear as origin, rayDir as direction)
            // Unit sphere at origin (0,0,0), radius 1
            float a = rayDirX * rayDirX + rayDirY * rayDirY + rayDirZ * rayDirZ;
            float b = 2.0f * (rayDirX * rayObjNear[0] + rayDirY * rayObjNear[1] + rayDirZ * rayObjNear[2]);
            float c = (rayObjNear[0] * rayObjNear[0] + rayObjNear[1] * rayObjNear[1] + rayObjNear[2] * rayObjNear[2]) - 1.0f;

            float discriminant = b * b - 4 * a * c;

            if (discriminant < 0) return false;

            float t = (-b - (float)Math.Sqrt(discriminant)) / (2.0f * a);
            if (t < 0) return false; // Intersect behind camera?

            float hitX = rayObjNear[0] + t * rayDirX;
            float hitY = rayObjNear[1] + t * rayDirY;
            float hitZ = rayObjNear[2] + t * rayDirZ;

            // Find nearest vertex
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
                SelectedHexCenter = new RoguelikeToolkit.World.App.Vector3(
                    _positions[nearestIdx * 3],
                    _positions[nearestIdx * 3 + 1],
                    _positions[nearestIdx * 3 + 2]
                );

                lat = Math.Asin(SelectedHexCenter.Z) * 180.0 / Math.PI;
                lon = Math.Atan2(SelectedHexCenter.Y, SelectedHexCenter.X) * 180.0 / Math.PI;

                using (var store = new HexSphereStore<int>(5))
                {
                    tileIndex = store.GetTileIndex(new GeoCoord(lat, lon));
                }

                RenderFrame();
                return true;
            }

            return false;
        }
    }
}
