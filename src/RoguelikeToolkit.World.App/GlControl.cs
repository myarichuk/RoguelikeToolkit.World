using System;
using System.Runtime.InteropServices;
using System.Numerics;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Geometry;

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
        public int RecursionLevel { get; set; } = 4;

        private int _shaderProgram;
        private int _vao;
        private int _vboPos;
        private int _vboColor;
        private int _vertexCount;
        private int _uMvpMatrix;

        private float[] _positions = new float[0];
        private float[] _colors = new float[0];
        private bool _needsMeshRebuild = true;

        protected override void OnOpenGlInit(GlInterface gl)
        {
            _shaderProgram = CreateShaderProgram(gl);
            _uMvpMatrix = gl.GetUniformLocationString(_shaderProgram, "uMvpMatrix");
            SetupMesh(gl);
        }

        private int CreateShaderProgram(GlInterface gl)
        {
            string vertexShaderSource = @"
            #version 330 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aColor;
            out vec3 ourColor;
            uniform mat4 uMvpMatrix;
            void main() {
                gl_Position = uMvpMatrix * vec4(aPos, 1.0);
                ourColor = aColor;
            }";

            string fragmentShaderSource = @"
            #version 330 core
            in vec3 ourColor;
            out vec4 FragColor;
            void main() {
                FragColor = vec4(ourColor, 1.0);
            }";

            int vertexShader = gl.CreateShader(GlConsts.GL_VERTEX_SHADER);
            byte[] vsBytes = System.Text.Encoding.UTF8.GetBytes(vertexShaderSource);
            fixed (byte* p = vsBytes) {
                var ptr = new IntPtr(p);
                var length = vsBytes.Length;
                gl.ShaderSource(vertexShader, 1, ptr, length);
            }
            gl.CompileShader(vertexShader);

            int fragmentShader = gl.CreateShader(GlConsts.GL_FRAGMENT_SHADER);
            byte[] fsBytes = System.Text.Encoding.UTF8.GetBytes(fragmentShaderSource);
            fixed (byte* p = fsBytes) {
                var ptr = new IntPtr(p);
                var length = fsBytes.Length;
                gl.ShaderSource(fragmentShader, 1, ptr, length);
            }
            gl.CompileShader(fragmentShader);

            int program = gl.CreateProgram();
            gl.AttachShader(program, vertexShader);
            gl.AttachShader(program, fragmentShader);
            gl.LinkProgram(program);

            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);

            return program;
        }

        private void SetupMesh(GlInterface gl)
        {
            IcosphereGenerator.GenerateFlat(RecursionLevel, out var vPos, out _, out _);
            _vertexCount = vPos.Length;
            _positions = new float[_vertexCount * 3];
            _colors = new float[_vertexCount * 3];

            for (int i = 0; i < _vertexCount; i++)
            {
                _positions[i * 3] = vPos[i].X;
                _positions[i * 3 + 1] = vPos[i].Y;
                _positions[i * 3 + 2] = vPos[i].Z;

                _colors[i * 3] = 0.5f;
                _colors[i * 3 + 1] = 0.5f;
                _colors[i * 3 + 2] = 0.5f;
            }

            int[] buffers = new int[2];
            fixed (int* pBuffers = buffers)
            {
                gl.GenBuffers(2, pBuffers);
            }
            _vboPos = buffers[0];
            _vboColor = buffers[1];

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

            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _vboColor);
            fixed (float* p = _colors)
            {
                gl.BufferData(GlConsts.GL_ARRAY_BUFFER, (IntPtr)(_colors.Length * sizeof(float)), (IntPtr)p, GlConsts.GL_STATIC_DRAW);
            }
            gl.VertexAttribPointer(1, 3, GlConsts.GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(1);

            gl.BindVertexArray(0);
        }

        public void SetRecursionLevel(int level)
        {
            if (level < 1) level = 1;
            if (level > 6) level = 6;
            RecursionLevel = level;
            _needsMeshRebuild = true;
            RenderFrame();
        }

        public void SetProjectionMode(ProjectionType mode)
        {
            ProjectionMode = mode;
            _needsMeshRebuild = true;
            RenderFrame();
        }

        public void RenderFrame()
        {
            RequestNextFrameRendering();
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            if (_needsMeshRebuild)
            {
                int[] buffers = new int[] { _vboPos, _vboColor };
                fixed (int* pBuffers = buffers)
                {
                    gl.DeleteBuffers(2, pBuffers);
                }

                int vao = _vao;
                gl.DeleteVertexArrays(1, &vao);

                SetupMesh(gl);
                _needsMeshRebuild = false;
            }

            var scale = VisualRoot?.RenderScaling ?? 1.0;
            gl.Viewport(0, 0, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));

            gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
            gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT | GlConsts.GL_DEPTH_BUFFER_BIT);
            gl.Enable(GlConsts.GL_DEPTH_TEST);

            // wireframe:
            // int GL_FRONT_AND_BACK = 0x0408;
            // int GL_LINE = 0x1B01;
            // gl.PolygonMode(GL_FRONT_AND_BACK, GL_LINE);

            gl.UseProgram(_shaderProgram);

            float aspect = (float)(Bounds.Width / Bounds.Height);

            var projection = Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (float)Math.PI / 180.0f, aspect, 0.1f, 100.0f);
            var view = Matrix4x4.CreateTranslation(-PanX, -PanY, -Distance);
            var modelX = Matrix4x4.CreateRotationX(Pitch * (float)Math.PI / 180.0f);
            var modelY = Matrix4x4.CreateRotationY(Yaw * (float)Math.PI / 180.0f);

            var mvp = modelX * modelY * view * projection;

            float* mvpPtr = stackalloc float[16];
            System.Runtime.CompilerServices.Unsafe.Write(mvpPtr, mvp);
            gl.UniformMatrix4fv(_uMvpMatrix, 1, false, mvpPtr);

            gl.BindVertexArray(_vao);

            int GL_TRIANGLES = 0x0004;
            gl.DrawArrays(GL_TRIANGLES, 0, _vertexCount);

            gl.BindVertexArray(0);
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            gl.DeleteProgram(_shaderProgram);
            base.OnOpenGlDeinit(gl);
        }
    }
}
