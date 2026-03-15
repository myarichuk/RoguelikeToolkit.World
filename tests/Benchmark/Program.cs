using System;
using System.Diagnostics;
using System.Reflection;

namespace Benchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            var glControl = new MockGlControl();

            // recursion 4 creates a certain number of vertices. Let's just mock a decent array size.
            // 4 recursion = 15360 triangles = 46080 vertices * 3 coords
            int numVertices = 46080;
            float[] pos = new float[numVertices * 3];
            Random rnd = new Random(42);
            for(int i = 0; i < pos.Length; i++) pos[i] = (float)rnd.NextDouble() * 2 - 1;

            glControl.SetPositions(pos);
            glControl.Width = 800;
            glControl.Height = 600;

            Console.WriteLine("Warming up...");
            for(int i=0; i<10; i++)
            {
                glControl.TryPickHex(400, 300, out _, out _, out _);
            }

            Console.WriteLine("Running benchmark...");
            Stopwatch sw = Stopwatch.StartNew();
            int iters = 1000;
            for(int i=0; i<iters; i++)
            {
                glControl.TryPickHex(400, 300, out _, out _, out _);
            }
            sw.Stop();

            Console.WriteLine($"Total time for {iters} iterations: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Average time per TryPickHex: {sw.Elapsed.TotalMilliseconds / iters:F4} ms");
        }
    }

    class MockGlControl
    {
        public float Yaw { get; set; } = 0f;
        public float Pitch { get; set; } = 0f;
        public float Distance { get; set; } = 2.2f;
        public float PanX { get; set; } = 0f;
        public float PanY { get; set; } = 0f;

        public double Width = 800;
        public double Height = 600;

        private float[] _positions = Array.Empty<float>();

        private class KdNode
        {
            public float X, Y, Z;
            public int Index;
            public KdNode? Left;
            public KdNode? Right;
        }

        private KdNode? _kdRoot;

        public void SetPositions(float[] pos)
        {
            _positions = pos;
            BuildKdTree();
        }

        private void BuildKdTree()
        {
            int numPoints = _positions.Length / 3;
            if (numPoints == 0) return;

            var indices = new int[numPoints];
            for (int i = 0; i < numPoints; i++) indices[i] = i;

            _kdRoot = BuildKdTreeRecursive(indices, 0, numPoints - 1, 0);
        }

        private KdNode? BuildKdTreeRecursive(int[] indices, int start, int end, int depth)
        {
            if (start > end) return null;

            int axis = depth % 3;

            Array.Sort(indices, start, end - start + 1, Comparer<int>.Create((a, b) =>
            {
                float valA = _positions[a * 3 + axis];
                float valB = _positions[b * 3 + axis];
                return valA.CompareTo(valB);
            }));

            int mid = start + (end - start) / 2;
            int idx = indices[mid];

            return new KdNode
            {
                X = _positions[idx * 3],
                Y = _positions[idx * 3 + 1],
                Z = _positions[idx * 3 + 2],
                Index = idx,
                Left = BuildKdTreeRecursive(indices, start, mid - 1, depth + 1),
                Right = BuildKdTreeRecursive(indices, mid + 1, end, depth + 1)
            };
        }

        private KdNode? NearestKdNode(KdNode? node, float tx, float ty, float tz, int depth, KdNode? best, ref float bestDistSq)
        {
            if (node == null) return best;

            float dx = node.X - tx;
            float dy = node.Y - ty;
            float dz = node.Z - tz;
            float distSq = dx * dx + dy * dy + dz * dz;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = node;
            }

            int axis = depth % 3;
            float axisDist = 0;
            if (axis == 0) axisDist = tx - node.X;
            else if (axis == 1) axisDist = ty - node.Y;
            else axisDist = tz - node.Z;

            KdNode? first = axisDist <= 0 ? node.Left : node.Right;
            KdNode? second = axisDist <= 0 ? node.Right : node.Left;

            best = NearestKdNode(first, tx, ty, tz, depth + 1, best, ref bestDistSq);

            if (axisDist * axisDist < bestDistSq)
            {
                best = NearestKdNode(second, tx, ty, tz, depth + 1, best, ref bestDistSq);
            }

            return best;
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

            float aspect = (float)(Width / Height);
            var projection = CreatePerspective(45.0f, aspect, 0.1f, 100.0f);
            var view = CreateTranslation(-PanX, -PanY, -Distance);
            var modelX = CreateRotationX(Pitch);
            var modelY = CreateRotationY(Yaw);
            var model = MultiplyMatrix(modelX, modelY);
            var viewProj = MultiplyMatrix(projection, view);
            var mvp = MultiplyMatrix(viewProj, model);

            var invMvp = InvertMatrix(mvp);

            // NDC Coordinates
            float ndcX = (float)((2.0 * mouseX) / Width - 1.0);
            float ndcY = (float)(1.0 - (2.0 * mouseY) / Height); // Invert Y

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

            if (_kdRoot != null)
            {
                var bestNode = NearestKdNode(_kdRoot, hitX, hitY, hitZ, 0, null, ref minDistsq);
                if (bestNode != null)
                {
                    nearestIdx = bestNode.Index;
                }
            }
            else
            {
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
            }

            if (nearestIdx != -1)
            {
                return true;
            }

            return false;
        }
    }
}