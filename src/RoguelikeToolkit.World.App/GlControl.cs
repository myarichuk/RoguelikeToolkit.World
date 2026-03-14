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
        public float Distance { get; set; } = 3f;
        public float PanX { get; set; } = 0f;
        public float PanY { get; set; } = 0f;
        public bool ShowPlates { get; set; } = false;

        private Point _lastMousePosition;
        private bool _isLeftDown = false;
        private bool _isRightDown = false;

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

            uniform mat4 uMvpMatrix;
            uniform mat4 uModelMatrix;

            void main()
            {
                gl_Position = uMvpMatrix * vec4(aPos, 1.0);
                FragPos = vec3(uModelMatrix * vec4(aPos, 1.0));

                // For a sphere, the normal is just the position
                // Also, no non-uniform scaling, so we can just use the model matrix
                Normal = mat3(uModelMatrix) * aNormal;

                Barycentric = aBary;
                VertexColor = aColor;
            }
        ";

        private const string FragmentShaderSource = @"
            #version 330 core
            #extension GL_OES_standard_derivatives : enable

            in vec3 FragPos;
            in vec3 Normal;
            in vec3 Barycentric;
            in vec3 VertexColor;

            out vec4 FragColor;

            uniform int uShowPlates;

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

                // Edge detection (Wireframe) using fwidth
                vec3 d = fwidth(Barycentric);
                vec3 a3 = smoothstep(vec3(0.0), d * 1.5, Barycentric);
                float edgeFactor = min(min(a3.x, a3.y), a3.z);

                vec3 edgeColor = vec3(0.8, 0.8, 0.8);

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

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            _lastMousePosition = point.Position;

            if (point.Properties.IsLeftButtonPressed) _isLeftDown = true;
            if (point.Properties.IsRightButtonPressed) _isRightDown = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.InitialPressMouseButton == MouseButton.Left) _isLeftDown = false;
            if (e.InitialPressMouseButton == MouseButton.Right) _isRightDown = false;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var point = e.GetCurrentPoint(this);

            if (_isLeftDown)
            {
                var deltaX = (float)(point.Position.X - _lastMousePosition.X);
                var deltaY = (float)(point.Position.Y - _lastMousePosition.Y);
                Yaw += deltaX * 0.5f;
                Pitch += deltaY * 0.5f;

                if (Pitch > 89.0f) Pitch = 89.0f;
                if (Pitch < -89.0f) Pitch = -89.0f;

                RequestNextFrameRendering();
            }

            if (_isRightDown)
            {
                var deltaX = (float)(point.Position.X - _lastMousePosition.X);
                var deltaY = (float)(point.Position.Y - _lastMousePosition.Y);

                float panSpeed = 0.005f * Distance;
                PanX -= deltaX * panSpeed;
                PanY += deltaY * panSpeed;

                RequestNextFrameRendering();
            }

            _lastMousePosition = point.Position;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            Distance -= (float)e.Delta.Y * 0.5f;
            if (Distance < 1.1f) Distance = 1.1f;
            if (Distance > 20.0f) Distance = 20.0f;
            RequestNextFrameRendering();
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

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
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

            glUniform1i(_uShowPlates, ShowPlates ? 1 : 0);

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
    }
}
